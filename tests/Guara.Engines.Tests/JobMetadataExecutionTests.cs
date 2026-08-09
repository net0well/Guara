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
        return (Build(registry, storage, TimeProvider.System), storage);
    }

    private static GuaraExecutor Build(
        JobHandlerRegistry registry, IStorage storage, TimeProvider time, RetryOptions? retry = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new GuaraExecutor(
            storage, new NullPublisher(), new RegistryJobInvoker(registry, services), registry,
            retry ?? new RetryOptions { MaxAttempts = 3, Backoff = static _ => TimeSpan.Zero },
            time, [], NullLogger<GuaraExecutor>.Instance);
    }

    /// <summary>
    /// Os dois modos de retentativa se <b>compõem</b>: as repetições em processo acontecem
    /// dentro de uma única tentativa persistente, e só o que sobrevive a elas gasta uma
    /// tentativa no storage. Se o middleware voltasse a contar por <c>MaxAttempts</c>, os
    /// números se multiplicariam em vez de somar.
    /// </summary>
    [Fact]
    public async Task InProcessRetry_ExhaustsLocally_BeforeSpendingAPersistentAttempt()
    {
        var chamadas = 0;
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Instavel",
            (_, _) =>
            {
                Interlocked.Increment(ref chamadas);
                throw new InvalidOperationException("oscilação");
            });

        var storage = new MemoryStorage();
        var executor = Build(
            registry,
            storage,
            TimeProvider.System,
            new RetryOptions { MaxAttempts = 3, InProcessAttempts = 2, Backoff = static _ => TimeSpan.Zero });
        var id = await CreateJobAsync(storage, "Instavel");

        await executor.ExecuteAsync(id, Ct);

        // Uma execução original + 2 repetições em processo, tudo dentro da mesma tentativa.
        Assert.Equal(3, chamadas);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.Equal(JobState.Retrying, job!.State);
        Assert.Equal(1, job.Attempt); // uma única tentativa persistente foi gasta
    }

    /// <summary>Desligada por padrão: sem opt-in, a falha vira tentativa persistente direto.</summary>
    [Fact]
    public async Task InProcessRetry_OffByDefault_FailsStraightToPersistentAttempt()
    {
        var chamadas = 0;
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Instavel",
            (_, _) =>
            {
                Interlocked.Increment(ref chamadas);
                throw new InvalidOperationException("oscilação");
            });

        var (executor, storage) = Setup(registry);
        var id = await CreateJobAsync(storage, "Instavel");

        await executor.ExecuteAsync(id, Ct);

        Assert.Equal(1, chamadas);
        Assert.Equal(JobState.Retrying, (await storage.Jobs.GetAsync(id, Ct))!.State);
    }

    /// <summary>
    /// Relógio cujos timers disparam de imediato. Deixa uma espera longa do produto — o
    /// intervalo de renovação do mutex — acontecer sem o teste esperar em tempo real.
    /// </summary>
    private sealed class TempoAcelerado : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => base.CreateTimer(
                callback,
                state,
                dueTime == Timeout.InfiniteTimeSpan ? dueTime : TimeSpan.FromMilliseconds(1),
                period == Timeout.InfiniteTimeSpan ? period : TimeSpan.FromMilliseconds(1));
    }

    /// <summary>Storage real com os locks trocados, para simular a perda da chave.</summary>
    private sealed class StorageComLocks(IStorage inner, ILockProvider locks) : IStorage
    {
        public StorageCapabilities Capabilities => inner.Capabilities;

        public IJobStorage Jobs => inner.Jobs;

        public IQueueStorage Queues => inner.Queues;

        public ILockProvider Locks => locks;

        public IServerRegistry Servers => inner.Servers;

        public IRecurringStorage Recurring => inner.Recurring;

        public IContinuationStorage Continuations => inner.Continuations;
    }

    /// <summary>Concede a chave e depois recusa renovar — a posse expirou para outro dono.</summary>
    private sealed class LockQueRecusaRenovar : ILockProvider
    {
        public ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
            => ValueTask.FromResult<ILockHandle?>(new Handle(key));

        private sealed class Handle(string key) : ILockHandle
        {
            public string Key => key;

            public ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
                => ValueTask.FromResult(false);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Um job mais longo que o TTL do mutex não pode seguir rodando depois de perder a
    /// chave: outro nó já pode tê-la adquirido, e os dois estariam executando em paralelo —
    /// exatamente o que <c>[GuaraDesabilitarConcorrencia]</c> promete impedir. Perder a
    /// chave abandona a execução sem consumir tentativa e sem marcar falha, como na perda
    /// da posse do job.
    /// </summary>
    [Fact]
    public async Task ConcurrencyGate_LostKeyDuringExecution_AbandonsWithoutConsumingAttempt()
    {
        var cancelado = false;
        var registry = new JobHandlerRegistry().Register(
            "Teste", "Longo",
            new JobExecutionMetadata { ConcurrencyKey = static _ => "chave-longa" },
            async (_, _, jobCt) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, jobCt);
                }
                catch (OperationCanceledException)
                {
                    cancelado = true;
                    throw;
                }
            });

        var storage = new MemoryStorage();
        var executor = Build(
            registry, new StorageComLocks(storage, new LockQueRecusaRenovar()), new TempoAcelerado());
        var id = await CreateJobAsync(storage, "Longo");

        await executor.ExecuteAsync(id, Ct);

        Assert.True(cancelado, "a execução deveria ter sido cancelada ao perder a chave");

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotEqual(JobState.Failed, job!.State);
        Assert.NotEqual(JobState.Succeeded, job.State);
        Assert.Equal(0, job.Attempt);
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
