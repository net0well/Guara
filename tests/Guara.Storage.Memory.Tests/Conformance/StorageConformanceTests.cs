using Guara.Abstractions;
using Guara.Storage;
using Xunit;

namespace Guara.Storage.Conformance;

/// <summary>
/// Kit de conformidade de storage (spec 004, AC-6): TODO provider — inclusive de
/// terceiros — deve herdar esta classe e passar 100%. Cobre aquisição atômica (AC-2),
/// lease/visibility (AC-3), idempotência de estado (AC-4) e paginação limitada (AC-7).
/// O provider deve usar o <see cref="TimeProvider"/> recebido para lease/TTL.
/// </summary>
public abstract class StorageConformanceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Cria o storage sob teste usando o relógio fornecido.</summary>
    protected abstract ValueTask<IStorage> CreateStorageAsync(TimeProvider timeProvider);

    private static JobRecord NewJob(string id, string queue = "default", JobState state = JobState.Enqueued,
        DateTimeOffset? createdAt = null, DateTimeOffset? scheduledFor = null) => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor("Tipo", "Metodo", default, queue),
        State = state,
        Queue = queue,
        CreatedAt = createdAt ?? T0,
        ScheduledFor = scheduledFor,
    };

    // --- Persistência básica ---

    [Fact]
    public async Task Create_ThenGet_RoundTrips()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);

        var found = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(JobState.Enqueued, found.State);
        Assert.Equal("default", found.Queue);
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNull()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        Assert.Null(await storage.Jobs.GetAsync(new JobId("nao-existe"), CancellationToken.None));
    }

    // --- Aquisição atômica (AC-2) ---

    [Fact]
    public async Task Acquire_EnqueuedJob_SetsProcessingAndLease()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);

        var acquired = await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);

        Assert.NotNull(acquired);
        Assert.Equal(JobState.Processing, acquired.State);
        Assert.Equal(T0 + TimeSpan.FromMinutes(5), acquired.LeaseUntil);
    }

    [Fact]
    public async Task Acquire_EmptyQueue_ReturnsNull()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1", queue: "outra"), CancellationToken.None);

        Assert.Null(await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None));
    }

    [Fact]
    public async Task Acquire_IsExclusive_UnderConcurrency()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        const int jobCount = 20;
        for (var i = 0; i < jobCount; i++)
        {
            await storage.Jobs.CreateAsync(NewJob($"j{i}", createdAt: T0 + TimeSpan.FromSeconds(i)), CancellationToken.None);
        }

        var acquired = new System.Collections.Concurrent.ConcurrentBag<JobId>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            while (await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None)
                   is { } job)
            {
                acquired.Add(job.Id);
            }
        })));

        Assert.Equal(jobCount, acquired.Count);
        Assert.Equal(jobCount, acquired.Distinct().Count()); // nenhum job processado 2x
    }

    // --- Agendamento e lease/visibility (AC-3) ---

    [Fact]
    public async Task Acquire_ScheduledJob_OnlyWhenDue()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        var due = T0 + TimeSpan.FromHours(1);
        await storage.Jobs.CreateAsync(NewJob("j1", state: JobState.Scheduled, scheduledFor: due), CancellationToken.None);

        Assert.Null(await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None));
        Assert.NotNull(await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), due, CancellationToken.None));
    }

    [Fact]
    public async Task Acquire_ExpiredLease_IsReacquirable()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);

        var first = await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);
        Assert.NotNull(first);

        // dentro do lease: ninguém rouba
        Assert.Null(await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0 + TimeSpan.FromMinutes(4), CancellationToken.None));

        // lease expirado (worker morreu): reelegível
        var second = await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0 + TimeSpan.FromMinutes(6), CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task RenewLease_KeepsOwnership()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);
        var acquired = await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);
        Assert.NotNull(acquired);

        // renova aos 4 min → posse até T0+9min
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(await storage.Jobs.RenewLeaseAsync(acquired.Id, TimeSpan.FromMinutes(5), CancellationToken.None));

        // aos 6 min o lease original teria expirado, mas a renovação mantém a posse
        Assert.Null(await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0 + TimeSpan.FromMinutes(6), CancellationToken.None));

        // aos 10 min a posse renovada expirou
        Assert.NotNull(await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0 + TimeSpan.FromMinutes(10), CancellationToken.None));
    }

    [Fact]
    public async Task RenewLease_UnknownOrNotProcessing_ReturnsFalse()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None); // Enqueued, sem posse

        Assert.False(await storage.Jobs.RenewLeaseAsync(new JobId("nao-existe"), TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await storage.Jobs.RenewLeaseAsync(new JobId("j1"), TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    // --- Transições de estado (AC-4) ---

    [Fact]
    public async Task UpdateState_Succeeded_IsIdempotent_AndClearsLease()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);
        await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);

        await storage.Jobs.UpdateStateAsync(new JobId("j1"), JobState.Succeeded, "42", CancellationToken.None);
        await storage.Jobs.UpdateStateAsync(new JobId("j1"), JobState.Succeeded, "42", CancellationToken.None); // reaplicar

        var job = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal("42", job.Result);
        Assert.Null(job.LeaseUntil); // estado terminal libera a posse
    }

    [Fact]
    public async Task UpdateState_Failed_RecordsError()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);

        await storage.Jobs.UpdateStateAsync(new JobId("j1"), JobState.Failed, "boom", CancellationToken.None);

        var job = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("boom", job.Error);
    }

    // --- Exclusão ---

    [Fact]
    public async Task Delete_ExistingNotProcessing_RemovesAndReturnsTrue()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);

        Assert.True(await storage.Jobs.DeleteAsync(new JobId("j1"), CancellationToken.None));
        Assert.Null(await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_UnknownOrProcessing_ReturnsFalse()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);
        await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);

        Assert.False(await storage.Jobs.DeleteAsync(new JobId("nao-existe"), CancellationToken.None));
        Assert.False(await storage.Jobs.DeleteAsync(new JobId("j1"), CancellationToken.None)); // Processing
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None));
    }

    // --- Listagem paginada (AC-7) ---

    [Fact]
    public async Task List_CapsPageSize()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        for (var i = 0; i < JobQuery.MaxPageSize + 50; i++)
        {
            await storage.Jobs.CreateAsync(NewJob($"j{i}", createdAt: T0 + TimeSpan.FromSeconds(i)), CancellationToken.None);
        }

        var page = await storage.Jobs.ListAsync(new JobQuery(PageSize: 10_000), CancellationToken.None);

        Assert.True(page.Count <= JobQuery.MaxPageSize);
    }

    [Fact]
    public async Task List_FiltersByStateAndQueue()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1", queue: "alta"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("j2", queue: "default"), CancellationToken.None);
        await storage.Jobs.UpdateStateAsync(new JobId("j2"), JobState.Failed, "x", CancellationToken.None);

        var altas = await storage.Jobs.ListAsync(new JobQuery(Queue: "alta"), CancellationToken.None);
        var falhas = await storage.Jobs.ListAsync(new JobQuery(State: JobState.Failed), CancellationToken.None);

        Assert.Single(altas);
        Assert.Equal(new JobId("j1"), altas[0].Id);
        Assert.Single(falhas);
        Assert.Equal(new JobId("j2"), falhas[0].Id);
    }

    // --- Filas ---

    [Fact]
    public async Task Queues_ReportLengthAndNames()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1", queue: "alta"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("j2", queue: "alta"), CancellationToken.None);

        Assert.Contains("alta", await storage.Queues.GetQueuesAsync(CancellationToken.None));
        Assert.Equal(2, await storage.Queues.GetLengthAsync("alta", CancellationToken.None));
    }

    // --- Locks com TTL ---

    [Fact]
    public async Task Lock_IsExclusive_AndReleasable()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));

        var first = await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None));

        await first.DisposeAsync(); // libera
        Assert.NotNull(await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task Lock_TtlExpiry_MakesAvailable()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);

        var first = await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(first);

        time.Advance(TimeSpan.FromMinutes(2)); // dono morreu; TTL expirou
        Assert.NotNull(await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None));

        // o dono antigo perdeu a posse: renovar deve falhar (fail-safe)
        Assert.False(await first.RenewAsync(TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task Lock_Renew_ExtendsOwnership()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);

        var handle = await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(handle);

        time.Advance(TimeSpan.FromSeconds(50));
        Assert.True(await handle.RenewAsync(TimeSpan.FromMinutes(1), CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(50)); // 1m40s desde o início; posse renovada vale até 1m50s
        Assert.Null(await storage.Locks.TryAcquireAsync("chave", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    // --- Capabilities (AC-5) ---

    [Fact]
    public async Task Capabilities_AreDeclared()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));

        Assert.NotNull(storage.Capabilities);
        Assert.NotNull(storage.Jobs);
        Assert.NotNull(storage.Queues);
        Assert.NotNull(storage.Locks);
    }
}
