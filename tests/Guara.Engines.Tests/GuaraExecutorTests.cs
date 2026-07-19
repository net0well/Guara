using Guara.Abstractions;
using Guara.Core;
using Guara.Executor;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RetryOptions NoBackoffRetry = new() { MaxAttempts = 2, Backoff = static _ => TimeSpan.Zero };

    private static async Task<(GuaraExecutor Executor, MemoryStorage Storage, RecordingPublisher Events, JobId Id)>
        SetupAsync(JobHandlerRegistry registry, RetryOptions? retry = null)
    {
        var storage = new MemoryStorage();
        var events = new RecordingPublisher();
        var services = new ServiceCollection().BuildServiceProvider();
        var executor = new GuaraExecutor(
            storage, events, new RegistryJobInvoker(registry, services), registry,
            retry ?? NoBackoffRetry, new FixedTimeProvider(T0), [],
            NullLogger<GuaraExecutor>.Instance);

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
    public async Task Failure_SchedulesPersistentRetry_WithAttemptBackoffAndReason()
    {
        var invocations = 0;
        var registry = new JobHandlerRegistry().Register("Teste", "Executar", (_, _) =>
        {
            invocations++;
            throw new InvalidOperationException("falhou de propósito");
        });
        var (executor, storage, events, id) = await SetupAsync(registry,
            new RetryOptions { MaxAttempts = 2, Backoff = static _ => TimeSpan.FromSeconds(5) });

        await executor.ExecuteAsync(id, Ct);

        Assert.Equal(1, invocations); // uma execução por tentativa: a retentativa fica no storage
        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Retrying, job!.State);
        Assert.Equal(1, job.Attempt);
        Assert.Equal(T0 + TimeSpan.FromSeconds(5), job.ScheduledFor);
        Assert.Contains("falhou de propósito", job.Error);

        var retry = Assert.Single(events.Published.OfType<JobRetryScheduled>());
        Assert.Equal(1, retry.Attempt);
        Assert.Equal(T0 + TimeSpan.FromSeconds(5), retry.RetryAt);
        Assert.Empty(events.Published.OfType<JobFailed>());
    }

    [Fact]
    public async Task Failure_AfterExhaustingAttempts_MarksFailed_WithLastReason()
    {
        var invocations = 0;
        var registry = new JobHandlerRegistry().Register("Teste", "Executar", (_, _) =>
        {
            invocations++;
            throw new InvalidOperationException($"falha {invocations}");
        });
        var (executor, storage, events, id) = await SetupAsync(registry); // MaxAttempts=2, sem back-off

        await executor.ExecuteAsync(id, Ct); // tentativa 0 → Retrying
        await executor.ExecuteAsync(id, Ct); // tentativa 1 → Retrying
        await executor.ExecuteAsync(id, Ct); // tentativa 2 esgota → Failed

        Assert.Equal(3, invocations);
        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Failed, job!.State);
        Assert.Equal(2, job.Attempt);
        Assert.Contains("falha 3", job.Error); // motivo da última falha

        Assert.Equal(2, events.Published.OfType<JobRetryScheduled>().Count());
        var failed = Assert.Single(events.Published.OfType<JobFailed>());
        Assert.Contains("falha 3", failed.Reason);
    }

    [Fact]
    public async Task Failure_WithRetriesDisabled_FailsImmediately()
    {
        var registry = new JobHandlerRegistry().Register("Teste", "Executar",
            static (_, _) => throw new InvalidOperationException("efeito irreversível"));
        var (executor, storage, events, id) = await SetupAsync(registry,
            new RetryOptions { MaxAttempts = 0 });

        await executor.ExecuteAsync(id, Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Failed, job!.State);
        Assert.Equal(0, job.Attempt);
        Assert.Empty(events.Published.OfType<JobRetryScheduled>());
        Assert.Single(events.Published.OfType<JobFailed>());
    }

    [Fact]
    public async Task Execution_SeedsContextWithPersistedAttempt()
    {
        var seenAttempts = new List<int>();
        var registry = new JobHandlerRegistry().Register("Teste", "Executar", (contexto, _) =>
        {
            seenAttempts.Add(contexto.Attempt);
            if (seenAttempts.Count == 1)
            {
                throw new InvalidOperationException("primeira falha");
            }

            return ValueTask.CompletedTask;
        });
        var (executor, _, _, id) = await SetupAsync(registry);

        await executor.ExecuteAsync(id, Ct);
        await executor.ExecuteAsync(id, Ct);

        Assert.Equal([0, 1], seenAttempts);
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
        Assert.Equal(0, job.Attempt);                    // e não consome tentativa
        Assert.Empty(events.Published.OfType<JobFailed>());
        Assert.Empty(events.Published.OfType<JobRetryScheduled>());
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
