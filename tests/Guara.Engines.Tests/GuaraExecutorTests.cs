using Guara.Abstractions;
using Guara.Core;
using Guara.Executor;
using Guara.Storage;
using Guara.Storage.Memory;
using Xunit;

namespace Guara.Engines.Tests;

public class GuaraExecutorTests
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

    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RetryOptions NoBackoffRetry = new() { MaxAttempts = 2, Backoff = static _ => TimeSpan.Zero };

    private static async Task<(GuaraExecutor Executor, MemoryStorage Storage, RecordingPublisher Events, JobId Id)>
        SetupAsync(JobHandlerRegistry registry, RetryOptions? retry = null)
    {
        var storage = new MemoryStorage();
        var events = new RecordingPublisher();
        var executor = new GuaraExecutor(
            storage, events, new RegistryJobInvoker(registry),
            retry ?? NoBackoffRetry, TimeProvider.System, []);

        var id = new JobId("j1");
        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = id,
            Descriptor = new JobDescriptor("Teste", "Executar", default),
            State = JobState.Enqueued,
            CreatedAt = T0,
        }, Ct);

        return (executor, storage, events, id);
    }

    [Fact]
    public async Task Success_MarksSucceeded_AndEmitsJobCompleted()
    {
        var invocations = 0;
        var registry = new JobHandlerRegistry().Register("Teste", "Executar", (_, _) =>
        {
            invocations++;
            return ValueTask.CompletedTask;
        });
        var (executor, storage, events, id) = await SetupAsync(registry);

        await executor.ExecuteAsync(id, Ct);

        Assert.Equal(1, invocations);
        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Succeeded, job!.State);
        Assert.Single(events.Published.OfType<JobCompleted>());
    }

    [Fact]
    public async Task Failure_RetriesThenMarksFailed_WithReason()
    {
        var invocations = 0;
        var registry = new JobHandlerRegistry().Register("Teste", "Executar", (_, _) =>
        {
            invocations++;
            throw new InvalidOperationException("falhou de propósito");
        });
        var (executor, storage, events, id) = await SetupAsync(registry);

        await executor.ExecuteAsync(id, Ct);

        Assert.Equal(3, invocations); // 1 tentativa + 2 retentativas (MaxAttempts=2)
        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Failed, job!.State);
        Assert.Contains("falhou de propósito", job.Error);
        var failed = Assert.Single(events.Published.OfType<JobFailed>());
        Assert.Contains("falhou de propósito", failed.Reason);
    }

    [Fact]
    public async Task UnknownHandler_MarksFailed_WithActionableMessage()
    {
        var (executor, storage, _, id) = await SetupAsync(new JobHandlerRegistry(),
            new RetryOptions { MaxAttempts = 0, Backoff = static _ => TimeSpan.Zero });

        await executor.ExecuteAsync(id, Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Failed, job!.State);
        Assert.Contains("JobHandlerRegistry", job.Error);
    }

    [Fact]
    public async Task Cancellation_LeavesStateUntouched_ForReprocessing()
    {
        var registry = new JobHandlerRegistry().Register("Teste", "Executar",
            async (_, ct) => await Task.Delay(Timeout.InfiniteTimeSpan, ct));
        var (executor, storage, events, id) = await SetupAsync(registry);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await executor.ExecuteAsync(id, cts.Token);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotEqual(JobState.Failed, job!.State);    // cancelamento não é falha
        Assert.NotEqual(JobState.Succeeded, job.State);  // nem sucesso — lease cobre o reprocesso
        Assert.Empty(events.Published.OfType<JobFailed>());
    }

    [Fact]
    public async Task DeletedJob_IsNoOp()
    {
        var registry = new JobHandlerRegistry();
        var (executor, _, events, _) = await SetupAsync(registry);

        await executor.ExecuteAsync(new JobId("nao-existe"), Ct);

        Assert.Empty(events.Published);
    }
}
