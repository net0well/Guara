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

    // 2026-07-16 é quinta-feira; 17 sexta, 18 sábado.
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobDescriptor Descriptor(string queue = "default") => new("Tipo", "Metodo", default, queue);

    private static DateTimeOffset Utc(int d, int h) => new(2026, 7, d, h, 0, 0, TimeSpan.Zero);

    private static (GuaraClient Client, MemoryStorage Storage) NewClient()
    {
        var time = new FixedTimeProvider(T0);
        var storage = new MemoryStorage(time);
        var client = new GuaraClient(
            storage, new NullPublisher(), new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(storage, time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);
        return (client, storage);
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
