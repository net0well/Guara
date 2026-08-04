using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Guara.Abstractions;
using Guara.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Dashboard.Api.Tests;

public sealed class DashboardOperationsApiTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplication? _app;

    private async Task<(HttpClient Client, IStorage Storage, IGuaraClient Guara)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGuara(options => options.ApplicationName = "dashboard-teste")
            .UseMemoryStorage()
            .AddGuaraScheduler()
            .AddGuaraDashboardApi();

        _app = builder.Build();
        _app.MapGuaraDashboardApi();
        await _app.StartAsync(Ct);

        return (
            _app.GetTestClient(),
            _app.Services.GetRequiredService<IStorage>(),
            _app.Services.GetRequiredService<IGuaraClient>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static JobRecord NewJob(
        string id, JobState state = JobState.Enqueued, string queue = "default",
        string typeName = "Demo.Tipo", string methodName = "Executar") => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor(typeName, methodName, default, queue),
        State = state,
        Queue = queue,
        CreatedAt = T0,
    };

    private static JobDescriptor Descriptor() => new("Demo.Tipo", "Executar", default, "default");

    // --- Busca ---

    [Fact]
    public async Task Search_PorTexto_CasaIdTipoEMetodo()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("relatorio-1"), Ct);
        await storage.Jobs.CreateAsync(NewJob("j2", typeName: "RelatorioService"), Ct);
        await storage.Jobs.CreateAsync(NewJob("j3", methodName: "GerarRelatorio"), Ct);
        await storage.Jobs.CreateAsync(NewJob("j4"), Ct);

        var page = await client.GetFromJsonAsync<PageDto<JobSummaryDto>>(
            "/api/v1/jobs/search?q=relatorio", Json, Ct);

        Assert.NotNull(page);
        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task Search_TotalIgnoraAPaginacao()
    {
        var (client, storage, _) = await StartAsync();
        for (var i = 0; i < 12; i++)
        {
            await storage.Jobs.CreateAsync(NewJob($"j{i}"), Ct);
        }

        var page = await client.GetFromJsonAsync<PageDto<JobSummaryDto>>(
            "/api/v1/jobs/search?page=1&pageSize=5", Json, Ct);

        Assert.NotNull(page);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(12, page.Total);
    }

    [Fact]
    public async Task Search_EstadoDesconhecido_Recusa()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.GetAsync("/api/v1/jobs/search?state=Inventado", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Search_IntervaloInvertido_Recusa()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.GetAsync(
            "/api/v1/jobs/search?from=2026-07-20T00:00:00Z&to=2026-07-19T00:00:00Z", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // --- Série ---

    [Fact]
    public async Task Series_JanelaPadrao_VoltaSerieContinua()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("ok"), Ct);
        await storage.Jobs.UpdateStateAsync(new JobId("ok"), JobState.Succeeded, "r", Ct);

        var serie = await client.GetFromJsonAsync<SeriesDto>("/api/v1/stats/series", Json, Ct);

        Assert.NotNull(serie);
        Assert.Equal("24h", serie.Window);
        Assert.Equal(900, serie.BucketSeconds);
        Assert.Equal(96, serie.Points.Count);

        // O desfecho acabou de acontecer: cai no último balde, que fecha no futuro imediato.
        Assert.Equal(1, serie.Points[^1].Succeeded);
        Assert.All(serie.Points, ponto => Assert.True(ponto.Total >= 0));
    }

    [Fact]
    public async Task Series_JanelaDesconhecida_RecusaListandoAsValidas()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.GetAsync("/api/v1/stats/series?window=1s", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("24h", await resposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    // --- Recorrentes ---

    [Fact]
    public async Task Recorrente_PausarERetomar()
    {
        var (client, storage, guara) = await StartAsync();
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        var pausa = await client.PostAsync("/api/v1/recurring/r/pause", content: null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, pausa.StatusCode);
        Assert.True((await storage.Recurring.GetAsync("r", Ct))!.Paused);

        var retomada = await client.PostAsync("/api/v1/recurring/r/resume", content: null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, retomada.StatusCode);
        Assert.False((await storage.Recurring.GetAsync("r", Ct))!.Paused);
    }

    [Fact]
    public async Task Recorrente_Inexistente_NaoEncontrado()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.PostAsync("/api/v1/recurring/fantasma/pause", content: null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Recorrente_Disparar_EnfileiraOcorrencia()
    {
        var (client, storage, guara) = await StartAsync();
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        var job = await client.PostAsync("/api/v1/recurring/r/trigger", content: null, Ct)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<JobSummaryDto>(Json, Ct), Ct).Unwrap();

        Assert.NotNull(job);
        Assert.Equal("Enqueued", job.State);
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId(job.Id), Ct));
    }

    [Fact]
    public async Task Recorrente_EditarCron_RecalculaProximoDisparo()
    {
        var (client, storage, guara) = await StartAsync();
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *").NaFila("antiga"), Ct);

        var resposta = await client.PatchAsJsonAsync(
            "/api/v1/recurring/r", new RecurringScheduleRequest(Cron: "0 5 * * *", Queue: "nova"), Json, Ct);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        var atualizado = await storage.Recurring.GetAsync("r", Ct);
        Assert.NotNull(atualizado);
        Assert.Equal("0 5 * * *", atualizado.CronExpression);
        Assert.Equal("nova", atualizado.Queue);
    }

    [Fact]
    public async Task Recorrente_EditarComCronEIntervalo_Recusa()
    {
        var (client, _, guara) = await StartAsync();
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        var resposta = await client.PatchAsJsonAsync(
            "/api/v1/recurring/r", new RecurringScheduleRequest(Cron: "0 5 * * *", Interval: "00:05:00"), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Recorrente_EditarComCronInvalido_RecusaSemQuebrarADefinicao()
    {
        var (client, storage, guara) = await StartAsync();
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *"), Ct);

        var resposta = await client.PatchAsJsonAsync(
            "/api/v1/recurring/r", new RecurringScheduleRequest(Cron: "nao-e-cron"), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("0 3 * * *", (await storage.Recurring.GetAsync("r", Ct))!.CronExpression);
    }

    // --- Calendários ---

    [Fact]
    public async Task Calendario_CriarLerEExcluir()
    {
        var (client, _, _) = await StartAsync();

        var criado = await client.PutAsJsonAsync(
            "/api/v1/calendars/feriados",
            new CalendarUpsertRequest(
                Dates: [new DateOnly(2026, 12, 25)],
                DaysOfWeek: ["Sunday"]),
            Json,
            Ct);

        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        var detalhe = await criado.Content.ReadFromJsonAsync<CalendarDetailDto>(Json, Ct);
        Assert.NotNull(detalhe);
        Assert.Single(detalhe.Dates);
        Assert.Equal(["Sunday"], detalhe.DaysOfWeek);
        Assert.Empty(detalhe.UsedBy);

        var lista = await client.GetFromJsonAsync<IReadOnlyList<CalendarSummaryDto>>(
            "/api/v1/calendars", Json, Ct);
        Assert.NotNull(lista);
        Assert.Single(lista);
        Assert.Equal(2, lista[0].RuleCount);

        var excluido = await client.DeleteAsync("/api/v1/calendars/feriados", Ct);
        Assert.Equal(HttpStatusCode.NoContent, excluido.StatusCode);
    }

    [Fact]
    public async Task Calendario_DiaDaSemanaDesconhecido_Recusa()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.PutAsJsonAsync(
            "/api/v1/calendars/x", new CalendarUpsertRequest(DaysOfWeek: ["Terça"]), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Calendario_EmUso_NaoPodeSerExcluido()
    {
        var (client, _, guara) = await StartAsync();
        await client.PutAsJsonAsync(
            "/api/v1/calendars/feriados",
            new CalendarUpsertRequest(Dates: [new DateOnly(2026, 12, 25)]),
            Json,
            Ct);
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("r").Executa(Descriptor()).ComCron("0 3 * * *").ComCalendario("feriados"), Ct);

        var resposta = await client.DeleteAsync("/api/v1/calendars/feriados", Ct);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Contains("r", await resposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calendario_Detalhe_MostraQuemUsaEOProximoDisparo()
    {
        var (client, _, guara) = await StartAsync();
        await client.PutAsJsonAsync(
            "/api/v1/calendars/feriados",
            new CalendarUpsertRequest(Dates: [new DateOnly(2026, 12, 25)]),
            Json,
            Ct);
        await guara.AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("limpeza").Executa(Descriptor()).ComCron("0 3 * * *").ComCalendario("feriados"), Ct);

        var detalhe = await client.GetFromJsonAsync<CalendarDetailDto>(
            "/api/v1/calendars/feriados", Json, Ct);

        Assert.NotNull(detalhe);
        var uso = Assert.Single(detalhe.UsedBy);
        Assert.Equal("limpeza", uso.RecurringId);
        Assert.NotNull(uso.NextRunAt);
    }

    [Fact]
    public async Task Calendario_Inexistente_NaoEncontrado()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.GetAsync("/api/v1/calendars/fantasma", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // --- Ações em massa ---

    [Fact]
    public async Task MassaRetentar_RelataODesfechoDeCadaItem()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("falho"), Ct);
        await storage.Jobs.UpdateStateAsync(new JobId("falho"), JobState.Failed, "erro", Ct);
        await storage.Jobs.CreateAsync(NewJob("na-fila"), Ct);

        var resposta = await client.PostAsJsonAsync(
            "/api/v1/jobs/bulk/retry", new BulkJobsRequest(["falho", "na-fila", "fantasma"]), Json, Ct);

        var resultado = await resposta.Content.ReadFromJsonAsync<BulkResultDto>(Json, Ct);
        Assert.NotNull(resultado);
        Assert.Equal(3, resultado.Requested);
        Assert.Equal(1, resultado.Succeeded);
        Assert.Equal(2, resultado.Failures.Count);
        Assert.Contains(resultado.Failures, f => f.JobId == "fantasma");
    }

    [Fact]
    public async Task MassaExcluir_ExcluiOsElegiveis()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("a"), Ct);
        await storage.Jobs.CreateAsync(NewJob("b", state: JobState.Processing), Ct);

        var resposta = await client.PostAsJsonAsync(
            "/api/v1/jobs/bulk/delete", new BulkJobsRequest(["a", "b"]), Json, Ct);

        var resultado = await resposta.Content.ReadFromJsonAsync<BulkResultDto>(Json, Ct);
        Assert.NotNull(resultado);
        Assert.Equal(1, resultado.Succeeded);
        Assert.Null(await storage.Jobs.GetAsync(new JobId("a"), Ct));
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId("b"), Ct));
    }

    [Fact]
    public async Task Massa_SelecaoVazia_Recusa()
    {
        var (client, _, _) = await StartAsync();

        var resposta = await client.PostAsJsonAsync(
            "/api/v1/jobs/bulk/retry", new BulkJobsRequest([]), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Massa_AcimaDoTeto_Recusa()
    {
        var (client, _, _) = await StartAsync();
        var ids = Enumerable.Range(0, 201).Select(i => $"j{i}").ToArray();

        var resposta = await client.PostAsJsonAsync(
            "/api/v1/jobs/bulk/delete", new BulkJobsRequest(ids), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
