using Guara.Abstractions;
using Guara.Storage;
using Xunit;

namespace Guara.Storage.Conformance;

/// <summary>
/// Kit de conformidade de storage: TODO provider — inclusive de terceiros — deve
/// herdar esta classe e passar 100%. Cobre aquisição atômica sob concorrência,
/// lease/visibility, retentativa persistente, idempotência de estado, retenção/purga,
/// registro de servidores, recorrentes/calendários, continuações, locks com TTL e
/// paginação limitada. O provider deve usar o <see cref="TimeProvider"/> recebido
/// para lease/TTL; storages descartáveis são liberados ao fim de cada teste.
/// </summary>
public abstract class StorageConformanceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly List<IStorage> _createdStorages = [];

    /// <summary>Cria o storage sob teste usando o relógio fornecido.</summary>
    protected abstract ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider);

    /// <summary>Cria e registra o storage para descarte ao fim do teste.</summary>
    protected async ValueTask<IStorage> CreateStorageAsync(TimeProvider timeProvider)
    {
        var storage = await CreateStorageCoreAsync(timeProvider);
        _createdStorages.Add(storage);
        return storage;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var storage in _createdStorages)
        {
            if (storage is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }

        GC.SuppressFinalize(this);
    }

    private static JobRecord NewJob(string id, string queue = "default", JobState state = JobState.Enqueued,
        DateTimeOffset? createdAt = null, DateTimeOffset? scheduledFor = null,
        string typeName = "Tipo", string methodName = "Metodo") => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor(typeName, methodName, default, queue),
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

    // --- Aquisição atômica ---

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

    // --- Agendamento e lease/visibility ---

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

    // --- Transições de estado ---

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

    // --- Retentativa persistente ---

    [Fact]
    public async Task ScheduleRetry_PersistsRetryingWithAttemptScheduleAndError()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);
        await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);

        await storage.Jobs.ScheduleRetryAsync(
            new JobId("j1"), "erro 1", T0 + TimeSpan.FromSeconds(30), CancellationToken.None);

        var job = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(JobState.Retrying, job.State);
        Assert.Equal(1, job.Attempt);
        Assert.Equal("erro 1", job.Error);
        Assert.Equal(T0 + TimeSpan.FromSeconds(30), job.ScheduledFor);
        Assert.Null(job.LeaseUntil); // posse liberada

        // cada nova falha incrementa a contagem e substitui o motivo
        await storage.Jobs.ScheduleRetryAsync(
            new JobId("j1"), "erro 2", T0 + TimeSpan.FromMinutes(2), CancellationToken.None);
        job = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.Equal(2, job!.Attempt);
        Assert.Equal("erro 2", job.Error);
    }

    [Fact]
    public async Task Acquire_RetryingJob_OnlyWhenDue_PreservingAttempt()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);
        await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);
        await storage.Jobs.ScheduleRetryAsync(
            new JobId("j1"), "erro", T0 + TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Null(await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0, CancellationToken.None)); // retentativa ainda não venceu

        var acquired = await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0 + TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(acquired);
        Assert.Equal(JobState.Processing, acquired.State);
        Assert.Equal(1, acquired.Attempt);
    }

    [Fact]
    public async Task ScheduleRetry_UnknownJob_IsNoOp()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));

        await storage.Jobs.ScheduleRetryAsync(new JobId("nao-existe"), "erro", T0, CancellationToken.None);

        Assert.Null(await storage.Jobs.GetAsync(new JobId("nao-existe"), CancellationToken.None));
    }

    [Fact]
    public async Task Reschedule_ReturnsJobToQueue_WithoutConsumingAttempt()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);
        await storage.Jobs.AcquireNextDueAsync("default", TimeSpan.FromMinutes(5), T0, CancellationToken.None);

        await storage.Jobs.RescheduleAsync(new JobId("j1"), T0 + TimeSpan.FromSeconds(30), CancellationToken.None);

        var job = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(JobState.Scheduled, job.State);
        Assert.Equal(0, job.Attempt); // devolução não é falha
        Assert.Equal(T0 + TimeSpan.FromSeconds(30), job.ScheduledFor);
        Assert.Null(job.LeaseUntil);

        // volta a ser elegível só quando vencer
        Assert.Null(await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0, CancellationToken.None));
        Assert.NotNull(await storage.Jobs.AcquireNextDueAsync(
            "default", TimeSpan.FromMinutes(5), T0 + TimeSpan.FromSeconds(30), CancellationToken.None));
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

    // --- Estados terminais e retenção ---

    [Fact]
    public async Task UpdateState_Terminal_StampsFinishedAt_AndPreservesItOnReapply()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);
        await storage.Jobs.CreateAsync(NewJob("j1"), CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(1));
        await storage.Jobs.UpdateStateAsync(new JobId("j1"), JobState.Succeeded, "ok", CancellationToken.None);
        var first = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.Equal(T0 + TimeSpan.FromMinutes(1), first!.FinishedAt);

        // reaplicar a mesma transição não altera o instante de término original
        time.Advance(TimeSpan.FromMinutes(5));
        await storage.Jobs.UpdateStateAsync(new JobId("j1"), JobState.Succeeded, "ok", CancellationToken.None);
        var second = await storage.Jobs.GetAsync(new JobId("j1"), CancellationToken.None);
        Assert.Equal(first.FinishedAt, second!.FinishedAt);
    }

    [Fact]
    public async Task Purge_RemovesOnlyMatchingStateOlderThanCutoff()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);
        await storage.Jobs.CreateAsync(NewJob("velho-ok"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("novo-ok"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("velho-falho"), CancellationToken.None);

        await storage.Jobs.UpdateStateAsync(new JobId("velho-ok"), JobState.Succeeded, null, CancellationToken.None);
        await storage.Jobs.UpdateStateAsync(new JobId("velho-falho"), JobState.Failed, "erro", CancellationToken.None);
        time.Advance(TimeSpan.FromHours(2));
        await storage.Jobs.UpdateStateAsync(new JobId("novo-ok"), JobState.Succeeded, null, CancellationToken.None);

        var removed = await storage.Jobs.PurgeAsync(
            JobState.Succeeded, T0 + TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Null(await storage.Jobs.GetAsync(new JobId("velho-ok"), CancellationToken.None));
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId("novo-ok"), CancellationToken.None));    // mais novo que o corte
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId("velho-falho"), CancellationToken.None)); // estado diferente
    }

    [Fact]
    public async Task Purge_NonTerminalState_Throws()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await storage.Jobs.PurgeAsync(JobState.Enqueued, T0, CancellationToken.None));
    }

    // --- Registro de servidores ---

    [Fact]
    public async Task Servers_AnnounceHeartbeatListAndRemove()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Servers.AnnounceAsync(new ServerNode
        {
            Id = "n1",
            MachineName = "maquina",
            StartedAt = T0,
            LastHeartbeat = T0,
            Queues = ["default"],
            MaxConcurrency = 4,
        }, CancellationToken.None);

        Assert.True(await storage.Servers.HeartbeatAsync("n1", T0 + TimeSpan.FromSeconds(30), CancellationToken.None));
        var listed = Assert.Single(await storage.Servers.ListAsync(CancellationToken.None));
        Assert.Equal(T0 + TimeSpan.FromSeconds(30), listed.LastHeartbeat);

        Assert.False(await storage.Servers.HeartbeatAsync("desconhecido", T0, CancellationToken.None));

        await storage.Servers.RemoveAsync("n1", CancellationToken.None);
        Assert.Empty(await storage.Servers.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Servers_RemoveExpired_RemovesOnlyStaleNodes()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Servers.AnnounceAsync(new ServerNode
        {
            Id = "morto", MachineName = "m1", StartedAt = T0, LastHeartbeat = T0,
        }, CancellationToken.None);
        await storage.Servers.AnnounceAsync(new ServerNode
        {
            Id = "vivo", MachineName = "m2", StartedAt = T0, LastHeartbeat = T0 + TimeSpan.FromMinutes(5),
        }, CancellationToken.None);

        var removed = await storage.Servers.RemoveExpiredAsync(T0 + TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(1, removed);
        var remaining = Assert.Single(await storage.Servers.ListAsync(CancellationToken.None));
        Assert.Equal("vivo", remaining.Id);
    }

    // --- Listagem paginada ---

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

    // --- Busca ---

    [Fact]
    public async Task List_TextMatchesIdTypeAndMethod_IgnoringCase()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(
            NewJob("relatorio-mensal", typeName: "Financeiro", methodName: "Fechar"), CancellationToken.None);
        await storage.Jobs.CreateAsync(
            NewJob("j2", typeName: "RelatorioService", methodName: "Gerar"), CancellationToken.None);
        await storage.Jobs.CreateAsync(
            NewJob("j3", typeName: "Email", methodName: "EnviarRelatorio"), CancellationToken.None);
        await storage.Jobs.CreateAsync(
            NewJob("j4", typeName: "Email", methodName: "Enviar"), CancellationToken.None);

        var achados = await storage.Jobs.ListAsync(new JobQuery(Text: "RELATORIO"), CancellationToken.None);

        Assert.Equal(3, achados.Count);
        Assert.DoesNotContain(achados, j => j.Id == new JobId("j4"));
    }

    [Fact]
    public async Task List_TextTreatsWildcardsAsLiteral()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("com%porcento"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("sem"), CancellationToken.None);

        var achados = await storage.Jobs.ListAsync(new JobQuery(Text: "m%p"), CancellationToken.None);

        Assert.Single(achados);
        Assert.Equal(new JobId("com%porcento"), achados[0].Id);
    }

    [Fact]
    public async Task List_FiltersByTypeName_Exactly()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("j1", typeName: "Relatorio"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("j2", typeName: "RelatorioAvancado"), CancellationToken.None);

        var achados = await storage.Jobs.ListAsync(new JobQuery(TypeName: "Relatorio"), CancellationToken.None);

        Assert.Single(achados);
        Assert.Equal(new JobId("j1"), achados[0].Id);
    }

    [Fact]
    public async Task List_FiltersByCreatedRange_LowerInclusiveUpperExclusive()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("antes", createdAt: T0 - TimeSpan.FromSeconds(1)), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("inicio", createdAt: T0), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("meio", createdAt: T0 + TimeSpan.FromSeconds(30)), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("fim", createdAt: T0 + TimeSpan.FromMinutes(1)), CancellationToken.None);

        var achados = await storage.Jobs.ListAsync(
            new JobQuery(From: T0, To: T0 + TimeSpan.FromMinutes(1)), CancellationToken.None);

        Assert.Equal(2, achados.Count);
        Assert.Contains(achados, j => j.Id == new JobId("inicio"));
        Assert.Contains(achados, j => j.Id == new JobId("meio"));
    }

    [Fact]
    public async Task Count_AppliesFiltersAndIgnoresPaging()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        for (var i = 0; i < 7; i++)
        {
            await storage.Jobs.CreateAsync(NewJob($"alta-{i}", queue: "alta"), CancellationToken.None);
        }

        await storage.Jobs.CreateAsync(NewJob("outra"), CancellationToken.None);

        var total = await storage.Jobs.CountAsync(
            new JobQuery(Queue: "alta", Page: 2, PageSize: 3), CancellationToken.None);

        Assert.Equal(7, total);
    }

    // --- Série temporal ---

    [Fact]
    public async Task Series_CountsOutcomesPerBucketAndFillsGaps()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);
        await storage.Jobs.CreateAsync(NewJob("ok"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("falho"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("tardio"), CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(10));
        await storage.Jobs.UpdateStateAsync(new JobId("ok"), JobState.Succeeded, "r", CancellationToken.None);
        await storage.Jobs.UpdateStateAsync(new JobId("falho"), JobState.Failed, "e", CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(2));
        await storage.Jobs.UpdateStateAsync(new JobId("tardio"), JobState.Succeeded, "r", CancellationToken.None);

        var serie = await storage.Jobs.GetSeriesAsync(
            new JobSeriesQuery(T0, T0 + TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(1)), CancellationToken.None);

        Assert.Equal(3, serie.Count);
        Assert.Equal(T0, serie[0].Timestamp);
        Assert.Equal(1, serie[0].Succeeded);
        Assert.Equal(1, serie[0].Failed);
        Assert.Equal(2, serie[0].Total);

        // Balde sem desfecho vira ponto zerado: o gráfico precisa da série contínua.
        Assert.Equal(0, serie[1].Total);
        Assert.Null(serie[1].LatencyP50);

        Assert.Equal(1, serie[2].Succeeded);
        Assert.Equal(0, serie[2].Failed);
    }

    [Fact]
    public async Task Series_ReportsObservedLatencyPercentiles()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);
        for (var i = 1; i <= 4; i++)
        {
            await storage.Jobs.CreateAsync(NewJob($"j{i}"), CancellationToken.None);
        }

        // Latências de 10s, 20s, 30s e 40s, todas no primeiro balde.
        for (var i = 1; i <= 4; i++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await storage.Jobs.UpdateStateAsync(new JobId($"j{i}"), JobState.Succeeded, "r", CancellationToken.None);
        }

        var serie = await storage.Jobs.GetSeriesAsync(
            new JobSeriesQuery(T0, T0 + TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1)), CancellationToken.None);

        // Rank discreto sobre a amostra ordenada: valores realmente observados.
        Assert.Equal(TimeSpan.FromSeconds(20), serie[0].LatencyP50);
        Assert.Equal(TimeSpan.FromSeconds(40), serie[0].LatencyP95);
    }

    [Fact]
    public async Task Series_FiltersByQueue()
    {
        var time = new ManualTimeProvider(T0);
        var storage = await CreateStorageAsync(time);
        await storage.Jobs.CreateAsync(NewJob("a", queue: "alta"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("b", queue: "baixa"), CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(5));
        await storage.Jobs.UpdateStateAsync(new JobId("a"), JobState.Succeeded, "r", CancellationToken.None);
        await storage.Jobs.UpdateStateAsync(new JobId("b"), JobState.Succeeded, "r", CancellationToken.None);

        var serie = await storage.Jobs.GetSeriesAsync(
            new JobSeriesQuery(T0, T0 + TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), Queue: "alta"),
            CancellationToken.None);

        Assert.Equal(1, serie[0].Succeeded);
    }

    [Fact]
    public async Task Series_RejectsWindowThatWouldExceedMaxPoints()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        var pedidoAbsurdo = new JobSeriesQuery(
            T0, T0 + TimeSpan.FromDays(7), TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await storage.Jobs.GetSeriesAsync(pedidoAbsurdo, CancellationToken.None));
    }

    // --- Contadores agregados ---

    [Fact]
    public async Task CountByState_GroupsAndFiltersByQueue()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("e1"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("e2"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("alta-1", queue: "alta"), CancellationToken.None);
        await storage.Jobs.CreateAsync(NewJob("f1"), CancellationToken.None);
        await storage.Jobs.UpdateStateAsync(new JobId("f1"), JobState.Failed, "erro", CancellationToken.None);

        var all = await storage.Jobs.CountByStateAsync(null, CancellationToken.None);
        Assert.Equal(3, all[JobState.Enqueued]);
        Assert.Equal(1, all[JobState.Failed]);

        var alta = await storage.Jobs.CountByStateAsync("alta", CancellationToken.None);
        Assert.Equal(1, alta[JobState.Enqueued]);
        Assert.False(alta.ContainsKey(JobState.Failed));
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

    // --- Recorrentes ---

    private static RecurringJobRecord NewRecurring(string id, DateTimeOffset? nextRunAt = null, bool paused = false) => new()
    {
        Id = id,
        Descriptor = new JobDescriptor("Tipo", "Metodo", default),
        CronExpression = "0 3 * * *",
        CreatedAt = T0,
        NextRunAt = nextRunAt,
        Paused = paused,
    };

    [Fact]
    public async Task Recurring_UpsertGetDelete_RoundTrips()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Recurring.UpsertAsync(NewRecurring("r1", T0), CancellationToken.None);

        var found = await storage.Recurring.GetAsync("r1", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal("0 3 * * *", found.CronExpression);
        Assert.Equal(T0, found.NextRunAt);

        // upsert da mesma chave substitui a definição
        await storage.Recurring.UpsertAsync(
            NewRecurring("r1", T0) with { Description = "atualizado" }, CancellationToken.None);
        Assert.Equal("atualizado", (await storage.Recurring.GetAsync("r1", CancellationToken.None))!.Description);

        Assert.True(await storage.Recurring.DeleteAsync("r1", CancellationToken.None));
        Assert.False(await storage.Recurring.DeleteAsync("r1", CancellationToken.None));
        Assert.Null(await storage.Recurring.GetAsync("r1", CancellationToken.None));
    }

    [Fact]
    public async Task Recurring_ListDue_ReturnsOnlyActiveDue_OrderedByNextRun()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Recurring.UpsertAsync(NewRecurring("futuro", T0 + TimeSpan.FromHours(1)), CancellationToken.None);
        await storage.Recurring.UpsertAsync(NewRecurring("vencido-b", T0 - TimeSpan.FromMinutes(1)), CancellationToken.None);
        await storage.Recurring.UpsertAsync(NewRecurring("vencido-a", T0 - TimeSpan.FromMinutes(5)), CancellationToken.None);
        await storage.Recurring.UpsertAsync(NewRecurring("pausado", T0 - TimeSpan.FromMinutes(5), paused: true), CancellationToken.None);
        await storage.Recurring.UpsertAsync(NewRecurring("sem-proximo"), CancellationToken.None);

        var due = await storage.Recurring.ListDueAsync(T0, CancellationToken.None);

        Assert.Equal(["vencido-a", "vencido-b"], due.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Recurring_List_ReturnsAllDefinitions()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Recurring.UpsertAsync(NewRecurring("r1"), CancellationToken.None);
        await storage.Recurring.UpsertAsync(NewRecurring("r2", paused: true), CancellationToken.None);

        Assert.Equal(2, (await storage.Recurring.ListAsync(CancellationToken.None)).Count);
    }

    // --- Calendários ---

    [Fact]
    public async Task Calendars_UpsertGetListDelete()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Recurring.UpsertCalendarAsync(new CalendarRecord
        {
            Name = "feriados",
            ExcludedDates = [new DateOnly(2026, 12, 25)],
            ExcludedDaysOfWeek = [DayOfWeek.Sunday],
        }, CancellationToken.None);

        var found = await storage.Recurring.GetCalendarAsync("feriados", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Single(found.ExcludedDates);
        Assert.Single(found.ExcludedDaysOfWeek);
        Assert.Single(await storage.Recurring.ListCalendarsAsync(CancellationToken.None));
        Assert.Null(await storage.Recurring.GetCalendarAsync("desconhecido", CancellationToken.None));

        Assert.True(await storage.Recurring.DeleteCalendarAsync("feriados", CancellationToken.None));
        Assert.False(await storage.Recurring.DeleteCalendarAsync("feriados", CancellationToken.None));
        Assert.Null(await storage.Recurring.GetCalendarAsync("feriados", CancellationToken.None));
    }

    // --- Continuações ---

    private static ContinuationRecord NewContinuation(
        string child, string parent, ContinuationTrigger trigger = ContinuationTrigger.OnSucceeded) => new()
    {
        ChildId = new JobId(child),
        ParentId = new JobId(parent),
        Trigger = trigger,
        CreatedAt = T0,
    };

    [Fact]
    public async Task Continuations_AddGetList_RoundTrips()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Continuations.AddAsync(NewContinuation("c1", "p1"), CancellationToken.None);
        await storage.Continuations.AddAsync(
            NewContinuation("c2", "p1", ContinuationTrigger.OnAnyFinishedState), CancellationToken.None);
        await storage.Continuations.AddAsync(NewContinuation("c3", "p2"), CancellationToken.None);

        var found = await storage.Continuations.GetByChildAsync(new JobId("c1"), CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(new JobId("p1"), found.ParentId);
        Assert.Equal(ContinuationStatus.Pending, found.Status);
        Assert.Null(await storage.Continuations.GetByChildAsync(new JobId("desconhecido"), CancellationToken.None));

        Assert.Equal(2, (await storage.Continuations.ListByParentAsync(new JobId("p1"), CancellationToken.None)).Count);

        // registrar o mesmo filho de novo não substitui o vínculo original
        await storage.Continuations.AddAsync(
            NewContinuation("c1", "p1", ContinuationTrigger.OnAnyFinishedState), CancellationToken.None);
        var unchanged = await storage.Continuations.GetByChildAsync(new JobId("c1"), CancellationToken.None);
        Assert.Equal(ContinuationTrigger.OnSucceeded, unchanged!.Trigger);
    }

    [Fact]
    public async Task Continuations_TryResolve_OnlyOnce()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Continuations.AddAsync(NewContinuation("c1", "p1"), CancellationToken.None);

        Assert.True(await storage.Continuations.TryResolveAsync(
            new JobId("c1"), ContinuationStatus.Enqueued, null, T0 + TimeSpan.FromMinutes(1), CancellationToken.None));

        var resolved = await storage.Continuations.GetByChildAsync(new JobId("c1"), CancellationToken.None);
        Assert.Equal(ContinuationStatus.Enqueued, resolved!.Status);
        Assert.Equal(T0 + TimeSpan.FromMinutes(1), resolved.ResolvedAt);

        // segunda resolução perde: o vínculo permanece como o primeiro desfecho
        Assert.False(await storage.Continuations.TryResolveAsync(
            new JobId("c1"), ContinuationStatus.Discarded, "tarde demais", T0 + TimeSpan.FromMinutes(2), CancellationToken.None));
        Assert.Equal(ContinuationStatus.Enqueued,
            (await storage.Continuations.GetByChildAsync(new JobId("c1"), CancellationToken.None))!.Status);

        Assert.False(await storage.Continuations.TryResolveAsync(
            new JobId("desconhecido"), ContinuationStatus.Enqueued, null, T0, CancellationToken.None));
    }

    [Fact]
    public async Task Continuations_ListPending_ReturnsOnlyPending()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));
        await storage.Continuations.AddAsync(NewContinuation("c1", "p1"), CancellationToken.None);
        await storage.Continuations.AddAsync(NewContinuation("c2", "p1"), CancellationToken.None);
        await storage.Continuations.TryResolveAsync(
            new JobId("c2"), ContinuationStatus.Discarded, "motivo", T0, CancellationToken.None);

        var pending = Assert.Single(await storage.Continuations.ListPendingAsync(CancellationToken.None));
        Assert.Equal(new JobId("c1"), pending.ChildId);
    }

    // --- Capabilities ---

    [Fact]
    public async Task Capabilities_AreDeclared()
    {
        var storage = await CreateStorageAsync(new ManualTimeProvider(T0));

        Assert.NotNull(storage.Capabilities);
        Assert.NotNull(storage.Jobs);
        Assert.NotNull(storage.Queues);
        Assert.NotNull(storage.Locks);
        Assert.NotNull(storage.Recurring);
        Assert.NotNull(storage.Continuations);
    }
}
