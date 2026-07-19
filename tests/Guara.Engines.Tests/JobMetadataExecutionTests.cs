using Guara.Abstractions;
using Guara.Core;
using Guara.Executor;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Engines.Tests;

public class JobMetadataExecutionTests
{
    private sealed class NullPublisher : IEventPublisher
    {
        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
            where TEvent : IGuaraEvent
            => ValueTask.CompletedTask;
    }

    private static readonly DateTimeOffset T0 = new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (GuaraExecutor Executor, MemoryStorage Storage) Setup(JobHandlerRegistry registry)
    {
        var storage = new MemoryStorage();
        var services = new ServiceCollection().BuildServiceProvider();
        var executor = new GuaraExecutor(
            storage, new NullPublisher(), new RegistryJobInvoker(registry, services), registry,
            new RetryOptions { MaxAttempts = 3, Backoff = static _ => TimeSpan.Zero },
            TimeProvider.System, [], NullLogger<GuaraExecutor>.Instance);
        return (executor, storage);
    }

    private static async Task<JobId> CreateJobAsync(
        MemoryStorage storage, string method, string id = "j1")
    {
        var jobId = new JobId(id);
        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = jobId,
            Descriptor = new JobDescriptor("Teste", method, default),
            State = JobState.Enqueued,
            CreatedAt = T0,
        }, Ct);
        return jobId;
    }

    [Fact]
    public async Task PerJobRetries_OverrideGlobalPolicy()
    {
        // [GuaraRetentativas(0)] declarado nos metadados: falha vira Failed direto,
        // mesmo com a política global permitindo 3 retentativas.
        var registry = new JobHandlerRegistry().Register(
            "Teste", "SemRetentativa", new JobExecutionMetadata { MaxAttempts = 0 },
            static (_, _, _) => throw new InvalidOperationException("efeito irreversível"));
        var (executor, storage) = Setup(registry);
        var id = await CreateJobAsync(storage, "SemRetentativa");

        await executor.ExecuteAsync(id, Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Failed, job!.State);
        Assert.Equal(0, job.Attempt);
    }

    [Fact]
    public async Task Timeout_HonoredByJob_FollowsRetryPolicy()
    {
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Lento", new JobExecutionMetadata { TimeoutSeconds = 1 },
            static async (_, _, ct) => await Task.Delay(Timeout.InfiniteTimeSpan, ct));
        var (executor, storage) = Setup(registry);
        var id = await CreateJobAsync(storage, "Lento");

        await executor.ExecuteAsync(id, Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Retrying, job!.State); // tempo limite é falha: segue a política
        Assert.Contains("Tempo limite", job.Error);
        Assert.Equal(1, job.Attempt);
    }

    [Fact]
    public async Task Timeout_IgnoredButCompleted_SucceedsWithWarning()
    {
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Teimoso", new JobExecutionMetadata { TimeoutSeconds = 1 },
            static async (_, _, _) => await Task.Delay(TimeSpan.FromSeconds(1.5), CancellationToken.None));
        var (executor, storage) = Setup(registry);
        var id = await CreateJobAsync(storage, "Teimoso");

        await executor.ExecuteAsync(id, Ct);

        // O efeito já aconteceu: o estado reflete a realidade (aviso vai para o log).
        Assert.Equal(JobState.Succeeded, (await storage.Jobs.GetAsync(id, Ct))!.State);
    }

    [Fact]
    public async Task ConcurrencyGate_BusyKey_RequeuesWithoutAttempt()
    {
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Exclusivo",
            new JobExecutionMetadata { ConcurrencyKey = static _ => "chave-unica" },
            static (_, _, _) => ValueTask.CompletedTask);
        var (executor, storage) = Setup(registry);
        var id = await CreateJobAsync(storage, "Exclusivo");

        // Outro nó segura a chave: a execução devolve o job à fila.
        await using var held = await storage.Locks.TryAcquireAsync(
            "guara:mutex:chave-unica", TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(held);

        await executor.ExecuteAsync(id, Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Scheduled, job!.State); // devolvido, não falhou
        Assert.Equal(0, job.Attempt);
        Assert.NotNull(job.ScheduledFor);
    }

    [Fact]
    public async Task ConcurrencyGate_FreeKey_ExecutesAndReleases()
    {
        var executions = 0;
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Exclusivo",
            new JobExecutionMetadata { ConcurrencyKey = static _ => "chave-livre" },
            (_, _, _) =>
            {
                Interlocked.Increment(ref executions);
                return ValueTask.CompletedTask;
            });
        var (executor, storage) = Setup(registry);
        var id = await CreateJobAsync(storage, "Exclusivo");

        await executor.ExecuteAsync(id, Ct);

        Assert.Equal(1, executions);
        Assert.Equal(JobState.Succeeded, (await storage.Jobs.GetAsync(id, Ct))!.State);

        // Chave liberada após o desfecho: outra aquisição imediata funciona.
        await using var reacquired = await storage.Locks.TryAcquireAsync(
            "guara:mutex:chave-livre", TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(reacquired);
    }

    [Fact]
    public async Task ConcurrencyGate_KeyPerArgument_AllowsDistinctKeysInParallel()
    {
        // Chaves distintas (ex.: "cliente-1" vs "cliente-2") não competem entre si.
        var registry = new JobHandlerRegistry().Register(
            "Teste", "PorCliente",
            new JobExecutionMetadata { ConcurrencyKey = static ctx => $"cliente-{ctx.Id.Value}" },
            static (_, _, _) => ValueTask.CompletedTask);
        var (executor, storage) = Setup(registry);
        var a = await CreateJobAsync(storage, "PorCliente", "a");
        var b = await CreateJobAsync(storage, "PorCliente", "b");

        await using var heldForA = await storage.Locks.TryAcquireAsync(
            "guara:mutex:cliente-a", TimeSpan.FromMinutes(1), Ct);

        await executor.ExecuteAsync(a, Ct); // chave ocupada → devolvido
        await executor.ExecuteAsync(b, Ct); // chave própria livre → executa

        Assert.Equal(JobState.Scheduled, (await storage.Jobs.GetAsync(a, Ct))!.State);
        Assert.Equal(JobState.Succeeded, (await storage.Jobs.GetAsync(b, Ct))!.State);
    }
}
