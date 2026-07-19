using Guara.Abstractions;
using Guara.Scheduler;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Scheduler.Tests;

public class ContinuationClientTests
{
    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<IGuaraEvent> Published { get; } = [];

        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
            where TEvent : IGuaraEvent
        {
            Published.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobDescriptor Descriptor(string queue = "default") => new("Tipo", "Metodo", default, queue);

    private static (GuaraClient Client, MemoryStorage Storage, RecordingPublisher Events) NewClient()
    {
        var time = new FixedTimeProvider(T0);
        var storage = new MemoryStorage(time);
        var events = new RecordingPublisher();
        var client = new GuaraClient(
            storage, events, new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(storage, time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);
        return (client, storage, events);
    }

    [Fact]
    public async Task ContinuarCom_CreatesAwaitingChild_AndPendingLink()
    {
        var (client, storage, events) = NewClient();
        var paiId = await client.EnfileirarAsync(Descriptor(), Ct);

        var filhoId = await client.ContinuarComAsync(paiId, Descriptor("relatorios"), ct: Ct);

        // O filho aguarda sem data: nunca elegível até o pai finalizar.
        var child = await storage.Jobs.GetAsync(filhoId, Ct);
        Assert.NotNull(child);
        Assert.Equal(JobState.Scheduled, child.State);
        Assert.Null(child.ScheduledFor);
        Assert.Equal("relatorios", child.Queue);

        var link = await storage.Continuations.GetByChildAsync(filhoId, Ct);
        Assert.NotNull(link);
        Assert.Equal(paiId, link.ParentId);
        Assert.Equal(ContinuationTrigger.OnSucceeded, link.Trigger);
        Assert.Equal(ContinuationStatus.Pending, link.Status);
        Assert.Equal(0, link.Depth);

        Assert.Contains(events.Published.OfType<JobCreated>(), e => e.Id == filhoId);
    }

    [Fact]
    public async Task ContinuarCom_ParentUnknown_Throws()
    {
        var (client, _, _) = NewClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.ContinuarComAsync(new JobId("fantasma"), Descriptor(), ct: Ct));
        Assert.Contains("não existe", ex.Message);
    }

    [Fact]
    public async Task ContinuarCom_EmptyParentId_Throws()
    {
        var (client, _, _) = NewClient();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await client.ContinuarComAsync(default, Descriptor(), ct: Ct));
    }

    [Fact]
    public async Task ContinuarCom_ParentAlreadySucceeded_EnqueuesImmediately()
    {
        var (client, storage, _) = NewClient();
        var paiId = await client.EnfileirarAsync(Descriptor(), Ct);
        await storage.Jobs.UpdateStateAsync(paiId, JobState.Succeeded, null, Ct);

        var filhoId = await client.ContinuarComAsync(paiId, Descriptor(), ct: Ct);

        Assert.Equal(JobState.Enqueued, (await storage.Jobs.GetAsync(filhoId, Ct))!.State);
        Assert.Equal(ContinuationStatus.Enqueued,
            (await storage.Continuations.GetByChildAsync(filhoId, Ct))!.Status);
    }

    [Fact]
    public async Task ContinuarCom_ParentAlreadyFailed_OnSucceeded_DiscardsImmediately()
    {
        var (client, storage, _) = NewClient();
        var paiId = await client.EnfileirarAsync(Descriptor(), Ct);
        await storage.Jobs.UpdateStateAsync(paiId, JobState.Failed, "boom", Ct);

        var filhoId = await client.ContinuarComAsync(paiId, Descriptor(), ct: Ct);

        Assert.Null(await storage.Jobs.GetAsync(filhoId, Ct));
        var link = await storage.Continuations.GetByChildAsync(filhoId, Ct);
        Assert.Equal(ContinuationStatus.Discarded, link!.Status);
        Assert.Contains("falhou", link.Reason);
    }

    [Fact]
    public async Task ContinuarCom_ParentAlreadyFailed_OnAnyFinishedState_Enqueues()
    {
        var (client, storage, _) = NewClient();
        var paiId = await client.EnfileirarAsync(Descriptor(), Ct);
        await storage.Jobs.UpdateStateAsync(paiId, JobState.Failed, "boom", Ct);

        var filhoId = await client.ContinuarComAsync(
            paiId, Descriptor(), new ContinuationOptions(ContinuationTrigger.OnAnyFinishedState), Ct);

        Assert.Equal(JobState.Enqueued, (await storage.Jobs.GetAsync(filhoId, Ct))!.State);
    }

    [Fact]
    public async Task ContinuarCom_Chain_TracksDepth()
    {
        var (client, storage, _) = NewClient();
        var raiz = await client.EnfileirarAsync(Descriptor(), Ct);

        var filho = await client.ContinuarComAsync(raiz, Descriptor(), ct: Ct);
        var neto = await client.ContinuarComAsync(filho, Descriptor(), ct: Ct);

        Assert.Equal(0, (await storage.Continuations.GetByChildAsync(filho, Ct))!.Depth);
        Assert.Equal(1, (await storage.Continuations.GetByChildAsync(neto, Ct))!.Depth);
    }

    [Fact]
    public async Task ContinuarCom_ChainTooDeep_Throws()
    {
        var (client, _, _) = NewClient();
        var current = await client.EnfileirarAsync(Descriptor(), Ct);

        for (var i = 0; i < 100; i++)
        {
            current = await client.ContinuarComAsync(current, Descriptor(), ct: Ct);
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.ContinuarComAsync(current, Descriptor(), ct: Ct));
        Assert.Contains("profundidade máxima", ex.Message);
    }

    [Fact]
    public async Task Excluir_DiscardsPendingContinuationChain()
    {
        var (client, storage, _) = NewClient();
        var pai = await client.EnfileirarAsync(Descriptor(), Ct);
        var filho = await client.ContinuarComAsync(pai, Descriptor(), ct: Ct);
        var neto = await client.ContinuarComAsync(filho, Descriptor(), ct: Ct);

        Assert.True(await client.ExcluirAsync(pai, Ct));

        Assert.Null(await storage.Jobs.GetAsync(filho, Ct));
        Assert.Null(await storage.Jobs.GetAsync(neto, Ct));
        var link = await storage.Continuations.GetByChildAsync(filho, Ct);
        Assert.Equal(ContinuationStatus.Discarded, link!.Status);
        Assert.Contains("excluído", link.Reason);
        Assert.Equal(ContinuationStatus.Discarded,
            (await storage.Continuations.GetByChildAsync(neto, Ct))!.Status);
    }
}
