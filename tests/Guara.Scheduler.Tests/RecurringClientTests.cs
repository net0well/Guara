using Guara.Abstractions;
using Guara.Scheduler;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Scheduler.Tests;

public class RecurringClientTests
{
    private sealed class NullPublisher : IEventPublisher
    {
        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
            where TEvent : IGuaraEvent
            => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MovableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // 2026-07-16 é quinta-feira; 17 sexta, 18 sábado.
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobDescriptor Descriptor(string queue = "default") => new("Tipo", "Metodo", default, queue);

    private static DateTimeOffset Utc(int d, int h) => new(2026, 7, d, h, 0, 0, TimeSpan.Zero);

    private static (GuaraClient Client, MemoryStorage Storage) NewClient()
    {
        var (client, storage, _) = NewClient(new FixedTimeProvider(T0));
        return (client, storage);
    }

    private static (GuaraClient Client, MemoryStorage Storage, TimeProvider Time) NewClient(TimeProvider time)
    {
        var storage = new MemoryStorage(time);
        var client = new GuaraClient(
            storage, new NullPublisher(), new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(storage, time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);
        return (client, storage, time);
    }

    [Fact]
    public async Task Upsert_CriaDefinicao_ComProximoDisparoCalculado()
    {
        var (client, storage) = NewClient();

        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("diario").Executa(Descriptor()).ComCron("0 3 * * *").ComDescricao("Relatório"), Ct);

        var record = await storage.Recurring.GetAsync("diario", Ct);
        Assert.NotNull(record);
        Assert.Equal(Utc(17, 3), record.NextRunAt);
        Assert.Equal(T0, record.CreatedAt);
        Assert.Equal("Relatório", record.Description);
        Assert.Equal("default", record.Queue);
    }

    [Fact]
    public async Task Upsert_DescriptorMarcadoPelaFactory_LigaPularSeAnterior()
    {
        var (client, storage) = NewClient();
        var descriptor = Descriptor() with
        {
            // Marca emitida pela factory gerada quando o job declara [GuaraPularSeAnteriorEmExecucao].
            Metadata = new Dictionary<string, string> { [JobMetadataKeys.SkipIfPreviousRunning] = "true" },
        };

        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("sincronia").Executa(descriptor).ACada(TimeSpan.FromMinutes(5)), Ct);

        Assert.True((await storage.Recurring.GetAsync("sincronia", Ct))!.SkipIfPreviousRunning);
    }

    [Fact]
    public async Task Upsert_NaFila_DefineFilaDasOcorrencias()
    {
        var (client, storage) = NewClient();

        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *").NaFila("relatorios"), Ct);

        Assert.Equal("relatorios", (await storage.Recurring.GetAsync("r", Ct))!.Queue);
    }

    [Fact]
    public async Task Upsert_Atualizacao_PreservaHistoricoERecalculaProximoDisparo()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        // Simula execução e pausa feitas pelo servidor entre os dois upserts.
        var executed = (await storage.Recurring.GetAsync("r", Ct))! with
        {
            LastRunAt = T0,
            LastRunJobId = new JobId("job-1"),
            LastSkippedAt = T0,
            Paused = true,
        };
        await storage.Recurring.UpsertAsync(executed, Ct);

        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 5 * * *"), Ct);

        var updated = await storage.Recurring.GetAsync("r", Ct);
        Assert.NotNull(updated);
        Assert.Equal("0 5 * * *", updated.CronExpression);
        Assert.Equal(Utc(17, 5), updated.NextRunAt);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal(T0, updated.LastRunAt);
        Assert.Equal(new JobId("job-1"), updated.LastRunJobId);
        Assert.Equal(T0, updated.LastSkippedAt);
        Assert.True(updated.Paused);
    }

    [Fact]
    public async Task Upsert_VigenciaJaEncerrada_PersisteSemProximoDisparo()
    {
        var (client, storage) = NewClient();

        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *").TerminaEm(T0 - TimeSpan.FromHours(1)), Ct);

        Assert.Null((await storage.Recurring.GetAsync("r", Ct))!.NextRunAt);
    }

    [Fact]
    public async Task FormaPosicional_CriaRecorrentePorCron()
    {
        var (client, storage) = NewClient();

        await client.AdicionarOuAtualizarRecorrenteAsync("r", Descriptor(), "0 3 * * *", TimeZoneInfo.Utc, Ct);

        var record = await storage.Recurring.GetAsync("r", Ct);
        Assert.NotNull(record);
        Assert.Equal(TimeZoneInfo.Utc.Id, record.TimeZoneId);
        Assert.Equal(Utc(17, 3), record.NextRunAt);
    }

    [Fact]
    public async Task ExcluirRecorrente_RemoveDefinicao()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        Assert.True(await client.ExcluirRecorrenteAsync("r", Ct));
        Assert.False(await client.ExcluirRecorrenteAsync("r", Ct));
        Assert.Null(await storage.Recurring.GetAsync("r", Ct));
    }

    [Fact]
    public async Task Builder_SemId_Falha()
    {
        var (client, _) = NewClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(job => job.Executa(Descriptor()).ComCron("* * * * *"), Ct));
        Assert.Contains("ComId", ex.Message);
    }

    [Fact]
    public async Task Builder_SemJob_Falha()
    {
        var (client, _) = NewClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(job => job.ComId("r").ComCron("* * * * *"), Ct));
        Assert.Contains("Executa", ex.Message);
    }

    [Fact]
    public async Task Builder_ComDuasAgendasOuNenhuma_Falha()
    {
        var (client, _) = NewClient();

        var both = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job => job.ComId("r").Executa(Descriptor()).ComCron("* * * * *").ACada(TimeSpan.FromMinutes(1)), Ct));
        Assert.Contains("exatamente uma agenda", both.Message);

        var none = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(job => job.ComId("r").Executa(Descriptor()), Ct));
        Assert.Contains("exatamente uma agenda", none.Message);
    }

    [Fact]
    public async Task Builder_JanelaComCron_Falha()
    {
        var (client, _) = NewClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job => job.ComId("r").Executa(Descriptor()).ComCron("* * * * *")
                    .EntreHorarios(new TimeOnly(8, 0), new TimeOnly(18, 0)), Ct));
        Assert.Contains("EntreHorarios", ex.Message);
    }

    [Fact]
    public async Task Builder_CronInvalido_FalhaComMensagemDetalhada()
    {
        var (client, _) = NewClient();
        await Assert.ThrowsAsync<FormatException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job => job.ComId("r").Executa(Descriptor()).ComCron("99 * * * *"), Ct));
    }

    [Fact]
    public async Task Builder_VigenciaInvertida_Falha()
    {
        var (client, _) = NewClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job => job.ComId("r").Executa(Descriptor()).ComCron("* * * * *")
                    .IniciaEm(T0).TerminaEm(T0 - TimeSpan.FromDays(1)), Ct));
        Assert.Contains("TerminaEm", ex.Message);
    }

    [Fact]
    public async Task Builder_FusoDesconhecido_Falha()
    {
        var (client, _) = NewClient();
        await Assert.ThrowsAsync<TimeZoneNotFoundException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job => job.ComId("r").Executa(Descriptor()).ComCron("* * * * *").NoFusoHorario("Marte/Olympus"), Ct));
    }

    [Fact]
    public async Task Pausar_TiraDaBuscaDeVencidos()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        Assert.True(await client.PausarRecorrenteAsync("r", Ct));

        var vencidos = await storage.Recurring.ListDueAsync(Utc(18, 4), Ct);
        Assert.Empty(vencidos);
        Assert.True((await storage.Recurring.GetAsync("r", Ct))!.Paused);
    }

    [Fact]
    public async Task Pausar_ManteveOProximoDisparoVisivel()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        await client.PausarRecorrenteAsync("r", Ct);

        // O painel continua mostrando quando teria rodado; a busca de vencidos já ignora pausados.
        Assert.Equal(Utc(17, 3), (await storage.Recurring.GetAsync("r", Ct))!.NextRunAt);
    }

    [Fact]
    public async Task Pausar_DuasVezes_EhIdempotente()
    {
        var (client, _) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        Assert.True(await client.PausarRecorrenteAsync("r", Ct));
        Assert.True(await client.PausarRecorrenteAsync("r", Ct));
    }

    [Fact]
    public async Task Pausar_Inexistente_RetornaFalso()
    {
        var (client, _) = NewClient();
        Assert.False(await client.PausarRecorrenteAsync("fantasma", Ct));
    }

    [Fact]
    public async Task Retomar_RecalculaAPartirDeAgora_SemRecuperarOPeriodoPausado()
    {
        var time = new MovableTimeProvider(T0);
        var (client, storage, _) = NewClient(time);
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);
        await client.PausarRecorrenteAsync("r", Ct);

        // Três dias pausado: o disparo de 17/07 03:00 ficou para trás.
        time.Advance(TimeSpan.FromDays(3));
        Assert.True(await client.RetomarRecorrenteAsync("r", Ct));

        var retomado = await storage.Recurring.GetAsync("r", Ct);
        Assert.NotNull(retomado);
        Assert.False(retomado.Paused);
        Assert.Equal(Utc(20, 3), retomado.NextRunAt);
    }

    [Fact]
    public async Task Retomar_NaoPausado_NaoMexeNaAgenda()
    {
        var time = new MovableTimeProvider(T0);
        var (client, storage, _) = NewClient(time);
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        time.Advance(TimeSpan.FromDays(3));
        Assert.True(await client.RetomarRecorrenteAsync("r", Ct));

        Assert.Equal(Utc(17, 3), (await storage.Recurring.GetAsync("r", Ct))!.NextRunAt);
    }

    [Fact]
    public async Task Retomar_Inexistente_RetornaFalso()
    {
        var (client, _) = NewClient();
        Assert.False(await client.RetomarRecorrenteAsync("fantasma", Ct));
    }

    [Fact]
    public async Task DispararAgora_EnfileiraOcorrenciaSemMexerNaAgenda()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *").NaFila("relatorios"), Ct);

        var jobId = await client.DispararRecorrenteAgoraAsync("r", Ct);

        Assert.NotNull(jobId);
        var job = await storage.Jobs.GetAsync(jobId.Value, Ct);
        Assert.NotNull(job);
        Assert.Equal(JobState.Enqueued, job.State);
        Assert.Equal("relatorios", job.Queue);
        Assert.Equal("r", job.Descriptor.Metadata![JobMetadataKeys.RecurringId]);

        var record = await storage.Recurring.GetAsync("r", Ct);
        Assert.NotNull(record);
        Assert.Equal(Utc(17, 3), record.NextRunAt);
        Assert.Equal(jobId, record.LastRunJobId);
        Assert.Equal(T0, record.LastRunAt);
    }

    [Fact]
    public async Task DispararAgora_FuncionaComORecorrentePausado()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);
        await client.PausarRecorrenteAsync("r", Ct);

        // Disparo manual é execução avulsa, não retomada: a definição segue pausada.
        Assert.NotNull(await client.DispararRecorrenteAgoraAsync("r", Ct));
        Assert.True((await storage.Recurring.GetAsync("r", Ct))!.Paused);
    }

    [Fact]
    public async Task DispararAgora_Inexistente_RetornaNulo()
    {
        var (client, _) = NewClient();
        Assert.Null(await client.DispararRecorrenteAgoraAsync("fantasma", Ct));
    }

    [Fact]
    public async Task Recorrente_ComCalendarioInexistente_Falha()
    {
        var (client, _) = NewClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job => job.ComId("r").Executa(Descriptor()).ComCron("* * * * *").ComCalendario("feriados"), Ct));
        Assert.Contains("não existe", ex.Message);
    }

    [Fact]
    public async Task Calendario_Atualizado_RecalculaProximoDisparoDosUsuarios()
    {
        var (client, storage) = NewClient();
        await client.AdicionarOuAtualizarCalendarioAsync(
            "feriados", cal => cal.ExcluirData(new DateOnly(2030, 1, 1)), Ct);
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("diario").Executa(Descriptor()).ComCron("0 3 * * *").ComCalendario("feriados"), Ct);
        Assert.Equal(Utc(17, 3), (await storage.Recurring.GetAsync("diario", Ct))!.NextRunAt);

        // A nova versão do calendário exclui a data do próximo disparo.
        await client.AdicionarOuAtualizarCalendarioAsync(
            "feriados", cal => cal.ExcluirData(new DateOnly(2026, 7, 17)), Ct);

        Assert.Equal(Utc(18, 3), (await storage.Recurring.GetAsync("diario", Ct))!.NextRunAt);
    }

    [Fact]
    public async Task ExcluirCalendario_EmUso_FalhaListandoOsUsuarios()
    {
        var (client, _) = NewClient();
        await client.AdicionarOuAtualizarCalendarioAsync(
            "feriados", cal => cal.ExcluirData(new DateOnly(2026, 12, 25)), Ct);
        await client.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("diario").Executa(Descriptor()).ComCron("0 3 * * *").ComCalendario("feriados"), Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.ExcluirCalendarioAsync("feriados", Ct));
        Assert.Contains("diario", ex.Message);

        // Sem usuários, a exclusão passa a valer.
        Assert.True(await client.ExcluirRecorrenteAsync("diario", Ct));
        Assert.True(await client.ExcluirCalendarioAsync("feriados", Ct));
        Assert.False(await client.ExcluirCalendarioAsync("feriados", Ct));
    }

    [Fact]
    public async Task Calendario_CronInvalido_FalhaNaConfiguracao()
    {
        var (client, _) = NewClient();
        await Assert.ThrowsAsync<FormatException>(async () =>
            await client.AdicionarOuAtualizarCalendarioAsync("c", cal => cal.ExcluirCron("99 * * * *"), Ct));
    }
}
