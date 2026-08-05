using Guara.Abstractions;
using Guara.Scheduler;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Scheduler.Tests;

public class ContinuationPromoterTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ContinuationPromoter Promoter, MemoryStorage Storage) NewPromoter()
    {
        var time = new FixedTimeProvider(T0);
        var storage = new MemoryStorage(time);
        return (
            new ContinuationPromoter(
                storage, new RecordingQueueSignal(), time, NullLogger<ContinuationPromoter>.Instance),
            storage);
    }

    private static JobRecord Job(string id, JobState state = JobState.Scheduled) => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor("Tipo", "Metodo", default),
        State = state,
        CreatedAt = T0,
    };

    private static ContinuationRecord Link(
        string child, string parent, ContinuationTrigger trigger = ContinuationTrigger.OnSucceeded, int depth = 0) => new()
    {
        ChildId = new JobId(child),
        ParentId = new JobId(parent),
        Trigger = trigger,
        Depth = depth,
        CreatedAt = T0,
    };

    private static async Task AddChainAsync(
        MemoryStorage storage, string parent, string child, ContinuationTrigger trigger = ContinuationTrigger.OnSucceeded,
        int depth = 0)
    {
        await storage.Jobs.CreateAsync(Job(child), Ct);
        await storage.Continuations.AddAsync(Link(child, parent, trigger, depth), Ct);
    }

    [Fact]
    public async Task Promote_ParentSucceeded_EnqueuesChild()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Succeeded), Ct);
        await AddChainAsync(storage, "pai", "filho");

        await promoter.PromoteAsync(new JobId("pai"), JobState.Succeeded, Ct);

        Assert.Equal(JobState.Enqueued, (await storage.Jobs.GetAsync(new JobId("filho"), Ct))!.State);
        var link = await storage.Continuations.GetByChildAsync(new JobId("filho"), Ct);
        Assert.Equal(ContinuationStatus.Enqueued, link!.Status);
        Assert.Equal(T0, link.ResolvedAt);
    }

    [Fact]
    public async Task Promote_FanOut_EnqueuesAllChildren()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Succeeded), Ct);
        await AddChainAsync(storage, "pai", "filho-1");
        await AddChainAsync(storage, "pai", "filho-2");
        await AddChainAsync(storage, "pai", "filho-3");

        await promoter.PromoteAsync(new JobId("pai"), JobState.Succeeded, Ct);

        foreach (var child in new[] { "filho-1", "filho-2", "filho-3" })
        {
            Assert.Equal(JobState.Enqueued, (await storage.Jobs.GetAsync(new JobId(child), Ct))!.State);
        }
    }

    [Fact]
    public async Task Promote_ParentFailed_OnSucceededTrigger_DiscardsChainWithReason()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Failed), Ct);
        await AddChainAsync(storage, "pai", "filho");
        await AddChainAsync(storage, "filho", "neto", depth: 1);

        await promoter.PromoteAsync(new JobId("pai"), JobState.Failed, Ct);

        // O filho e toda a descendência nunca vão disparar: jobs somem, vínculos registram o motivo.
        Assert.Null(await storage.Jobs.GetAsync(new JobId("filho"), Ct));
        Assert.Null(await storage.Jobs.GetAsync(new JobId("neto"), Ct));
        var childLink = await storage.Continuations.GetByChildAsync(new JobId("filho"), Ct);
        Assert.Equal(ContinuationStatus.Discarded, childLink!.Status);
        Assert.Contains("falhou", childLink.Reason);
        Assert.Equal(ContinuationStatus.Discarded,
            (await storage.Continuations.GetByChildAsync(new JobId("neto"), Ct))!.Status);
    }

    [Fact]
    public async Task Promote_ParentFailed_OnAnyFinishedState_Enqueues()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Failed), Ct);
        await AddChainAsync(storage, "pai", "filho", ContinuationTrigger.OnAnyFinishedState);

        await promoter.PromoteAsync(new JobId("pai"), JobState.Failed, Ct);

        Assert.Equal(JobState.Enqueued, (await storage.Jobs.GetAsync(new JobId("filho"), Ct))!.State);
    }

    [Fact]
    public async Task Promote_Repeated_DoesNotReapplyResolution()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Succeeded), Ct);
        await AddChainAsync(storage, "pai", "filho");

        await promoter.PromoteAsync(new JobId("pai"), JobState.Succeeded, Ct);

        // O filho seguiu o fluxo normal (foi adquirido e está executando)...
        await storage.Jobs.UpdateStateAsync(new JobId("filho"), JobState.Processing, null, Ct);

        // ...uma nova promoção do mesmo pai não pode devolvê-lo à fila.
        await promoter.PromoteAsync(new JobId("pai"), JobState.Succeeded, Ct);

        Assert.Equal(JobState.Processing, (await storage.Jobs.GetAsync(new JobId("filho"), Ct))!.State);
    }

    [Fact]
    public async Task Sweep_ParentAlreadyTerminal_ResolvesPending()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Succeeded), Ct);
        await AddChainAsync(storage, "pai", "filho");

        // Nenhum evento de conclusão rodou (queda entre persistir o final e promover).
        await promoter.SweepAsync(Ct);

        Assert.Equal(JobState.Enqueued, (await storage.Jobs.GetAsync(new JobId("filho"), Ct))!.State);
        Assert.Equal(ContinuationStatus.Enqueued,
            (await storage.Continuations.GetByChildAsync(new JobId("filho"), Ct))!.Status);
    }

    [Fact]
    public async Task Sweep_ParentMissing_DiscardsChain()
    {
        var (promoter, storage) = NewPromoter();
        await AddChainAsync(storage, "pai-que-sumiu", "filho");
        await AddChainAsync(storage, "filho", "neto", depth: 1);

        await promoter.SweepAsync(Ct);

        Assert.Null(await storage.Jobs.GetAsync(new JobId("filho"), Ct));
        Assert.Null(await storage.Jobs.GetAsync(new JobId("neto"), Ct));
        var link = await storage.Continuations.GetByChildAsync(new JobId("filho"), Ct);
        Assert.Equal(ContinuationStatus.Discarded, link!.Status);
        Assert.Contains("não existe", link.Reason);
    }

    [Fact]
    public async Task Sweep_ParentStillRunning_LeavesPendingUntouched()
    {
        var (promoter, storage) = NewPromoter();
        await storage.Jobs.CreateAsync(Job("pai", JobState.Processing), Ct);
        await AddChainAsync(storage, "pai", "filho");

        await promoter.SweepAsync(Ct);

        Assert.Equal(JobState.Scheduled, (await storage.Jobs.GetAsync(new JobId("filho"), Ct))!.State);
        Assert.Equal(ContinuationStatus.Pending,
            (await storage.Continuations.GetByChildAsync(new JobId("filho"), Ct))!.Status);
    }
}
