using Guara.Abstractions;
using Guara.Dashboard.Api;
using Guara.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Rotas que tornam o painel operável: busca com filtros, série para os gráficos,
/// gestão de recorrentes e de calendários, e ações em massa.
/// </summary>
public static partial class DashboardEndpoints
{
    /// <summary>Teto de itens por ação em massa — uma seleção de tela, não um job de lote.</summary>
    private const int MaxBulkItems = 200;

    // Janelas oferecidas ao gráfico, com o balde que mantém a série legível: pontos demais
    // viram ruído, pontos de menos escondem o pico.
    private static readonly Dictionary<string, (TimeSpan Window, TimeSpan Bucket)> SeriesWindows =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["1h"] = (TimeSpan.FromHours(1), TimeSpan.FromMinutes(1)),
            ["24h"] = (TimeSpan.FromHours(24), TimeSpan.FromMinutes(15)),
            ["7d"] = (TimeSpan.FromDays(7), TimeSpan.FromHours(1)),
        };

    private static void MapOperations(RouteGroupBuilder group)
    {
        group.MapGet("/jobs/search", SearchJobsAsync).WithName("GuaraSearchJobs")
            .WithSummary("Busca de jobs por texto, tipo, fila, estado e intervalo de criação.")
            .RequireAction(GuaraActions.View);
        group.MapGet("/stats/series", GetSeriesAsync).WithName("GuaraStatsSeries")
            .WithSummary("Série temporal de desfechos e latência para os gráficos.")
            .RequireAction(GuaraActions.View);

        group.MapPatch("/recurring/{id}", UpdateRecurringAsync).WithName("GuaraUpdateRecurring")
            .WithSummary("Altera a agenda, a fila, a descrição ou o calendário de um recorrente.")
            .RequireAction(GuaraActions.Trigger);
        group.MapPost("/recurring/{id}/pause", PauseRecurringAsync).WithName("GuaraPauseRecurring")
            .WithSummary("Suspende as ocorrências automáticas de um recorrente.")
            .RequireAction(GuaraActions.Trigger);
        group.MapPost("/recurring/{id}/resume", ResumeRecurringAsync).WithName("GuaraResumeRecurring")
            .WithSummary("Retoma um recorrente pausado, sem recuperar o período parado.")
            .RequireAction(GuaraActions.Trigger);
        group.MapPost("/recurring/{id}/trigger", TriggerRecurringAsync).WithName("GuaraTriggerRecurring")
            .WithSummary("Enfileira uma ocorrência agora, sem mexer na agenda.")
            .RequireAction(GuaraActions.Trigger);

        group.MapGet("/calendars", GetCalendarsAsync).WithName("GuaraCalendars")
            .WithSummary("Calendários de exclusão e quem os usa.")
            .RequireAction(GuaraActions.View);
        group.MapGet("/calendars/{name}", GetCalendarAsync).WithName("GuaraCalendarDetail")
            .WithSummary("Exclusões de um calendário e o próximo disparo de quem o usa.")
            .RequireAction(GuaraActions.View);
        group.MapPut("/calendars/{name}", UpsertCalendarAsync).WithName("GuaraUpsertCalendar")
            .WithSummary("Cria ou substitui um calendário; recalcula quem o usa.")
            .RequireAction(GuaraActions.Calendars);
        group.MapDelete("/calendars/{name}", DeleteCalendarAsync).WithName("GuaraDeleteCalendar")
            .WithSummary("Exclui um calendário; bloqueado enquanto algum recorrente o usa.")
            .RequireAction(GuaraActions.Calendars);

        group.MapPost("/jobs/bulk/retry", BulkRetryAsync).WithName("GuaraBulkRetry")
            .WithSummary("Reenfileira os jobs selecionados, relatando o desfecho de cada um.")
            .RequireAction(GuaraActions.Retry);
        group.MapPost("/jobs/bulk/delete", BulkDeleteAsync).WithName("GuaraBulkDelete")
            .WithSummary("Exclui os jobs selecionados, relatando o desfecho de cada um.")
            .RequireAction(GuaraActions.Delete);
    }

    // --- Busca e série ---

    private static async Task<Results<Ok<PageDto<JobSummaryDto>>, ProblemHttpResult>> SearchJobsAsync(
        IStorage storage, string? q, string? type, string? queue, string? state,
        DateTimeOffset? from, DateTimeOffset? to, int page = 1, int pageSize = 50,
        CancellationToken ct = default)
    {
        JobState? stateFilter = null;
        if (state is not null)
        {
            if (!Enum.TryParse<JobState>(state, ignoreCase: true, out var parsed))
            {
                return TypedResults.Problem(
                    $"Estado desconhecido: '{state}'.", statusCode: StatusCodes.Status400BadRequest);
            }

            stateFilter = parsed;
        }

        if (from is { } start && to is { } end && end <= start)
        {
            return TypedResults.Problem(
                "O fim do intervalo precisa ser maior que o início.", statusCode: StatusCodes.Status400BadRequest);
        }

        var query = new JobQuery(stateFilter, queue, page, pageSize, q, type, from, to);
        var jobs = await storage.Jobs.ListAsync(query, ct);
        var total = await storage.Jobs.CountAsync(query, ct);

        return TypedResults.Ok(new PageDto<JobSummaryDto>(
            [.. jobs.Select(ToSummary)], query.EffectivePage, query.EffectivePageSize, total));
    }

    private static async Task<Results<Ok<SeriesDto>, ProblemHttpResult>> GetSeriesAsync(
        IStorage storage, TimeProvider time, string? window, string? queue, CancellationToken ct)
    {
        var name = window ?? "24h";
        if (!SeriesWindows.TryGetValue(name, out var shape))
        {
            return TypedResults.Problem(
                $"Janela desconhecida: '{name}'. Disponíveis: {string.Join(", ", SeriesWindows.Keys)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A janela termina no fim do balde corrente, senão o último ponto entraria pela
        // metade e apareceria como uma queda de throughput que não aconteceu.
        var now = time.GetUtcNow();
        var end = Floor(now, shape.Bucket) + shape.Bucket;
        var points = await storage.Jobs.GetSeriesAsync(
            new JobSeriesQuery(end - shape.Window, end, shape.Bucket, queue), ct);

        return TypedResults.Ok(new SeriesDto(
            name.ToLowerInvariant(),
            (int)shape.Bucket.TotalSeconds,
            [.. points.Select(p => new SeriesPointDto(
                p.Timestamp, p.Succeeded, p.Failed, p.Total,
                p.LatencyP50?.TotalMilliseconds, p.LatencyP95?.TotalMilliseconds))]));
    }

    private static DateTimeOffset Floor(DateTimeOffset instant, TimeSpan bucket)
        => new(instant.Ticks - (instant.Ticks % bucket.Ticks), instant.Offset);

    // --- Recorrentes ---

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UpdateRecurringAsync(
        string id, RecurringScheduleRequest request, IGuaraClient client, IStorage storage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await storage.Recurring.GetAsync(id, ct) is not { } current)
        {
            return TypedResults.NotFound();
        }

        if (request.Cron is not null && request.Interval is not null)
        {
            return TypedResults.Problem(
                "Informe cron ou intervalo, nunca os dois.", statusCode: StatusCodes.Status400BadRequest);
        }

        TimeSpan? interval = null;
        if (request.Interval is { } raw)
        {
            if (!TimeSpan.TryParse(raw, out var parsed))
            {
                return TypedResults.Problem(
                    $"Intervalo inválido: '{raw}'. Use o formato d.hh:mm:ss.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            interval = parsed;
        }

        try
        {
            // Reconstruir pelo builder mantém uma única porta de validação: cron, fuso,
            // vigência e existência do calendário são conferidos no mesmo lugar de sempre.
            await client.AdicionarOuAtualizarRecorrenteAsync(
                job =>
                {
                    job.ComId(id).Executa(current.Descriptor);

                    if (request.Cron is { } cron)
                    {
                        job.ComCron(cron);
                    }
                    else if (interval is { } every)
                    {
                        job.ACada(every);
                    }
                    else if (current.CronExpression is { } existingCron)
                    {
                        job.ComCron(existingCron);
                    }
                    else if (current.Interval is { } existingInterval)
                    {
                        job.ACada(existingInterval);
                    }

                    if (current.WindowStart is { } windowStart && current.WindowEnd is { } windowEnd
                        && request.Cron is null && interval is null)
                    {
                        job.EntreHorarios(windowStart, windowEnd);
                    }

                    job.NoFusoHorario(request.TimeZoneId ?? current.TimeZoneId ?? "UTC")
                        .NaFila(request.Queue ?? current.Queue);

                    if ((request.Description ?? current.Description) is { } description)
                    {
                        job.ComDescricao(description);
                    }

                    if ((request.CalendarName ?? current.CalendarName) is { } calendar)
                    {
                        job.ComCalendario(calendar);
                    }

                    if (current.NotBefore is { } notBefore)
                    {
                        job.IniciaEm(notBefore);
                    }

                    if (current.NotAfter is { } notAfter)
                    {
                        job.TerminaEm(notAfter);
                    }

                    if (current.SkipIfPreviousRunning)
                    {
                        job.PularSeAnteriorEmExecucao();
                    }
                },
                ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
            or FormatException or TimeZoneNotFoundException)
        {
            // Configuração recusada pelo builder é erro do pedido, não do servidor: cron
            // malformada, fuso inexistente, vigência invertida, calendário ausente.
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> PauseRecurringAsync(
        string id, IGuaraClient client, CancellationToken ct)
        => await client.PausarRecorrenteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> ResumeRecurringAsync(
        string id, IGuaraClient client, CancellationToken ct)
        => await client.RetomarRecorrenteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<Ok<JobSummaryDto>, NotFound>> TriggerRecurringAsync(
        string id, IGuaraClient client, IStorage storage, CancellationToken ct)
    {
        if (await client.DispararRecorrenteAgoraAsync(id, ct) is not { } jobId)
        {
            return TypedResults.NotFound();
        }

        var job = await storage.Jobs.GetAsync(jobId, ct);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok(ToSummary(job));
    }

    // --- Calendários ---

    private static async Task<Ok<IReadOnlyList<CalendarSummaryDto>>> GetCalendarsAsync(
        IStorage storage, CancellationToken ct)
    {
        var calendars = await storage.Recurring.ListCalendarsAsync(ct);
        var recurring = await storage.Recurring.ListAsync(ct);

        IReadOnlyList<CalendarSummaryDto> dtos =
        [
            .. calendars.Select(calendar => new CalendarSummaryDto(
                calendar.Name,
                RuleCount(calendar),
                [.. UsersOf(recurring, calendar.Name).Select(r => r.Id)])),
        ];

        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<CalendarDetailDto>, NotFound>> GetCalendarAsync(
        string name, IStorage storage, CancellationToken ct)
    {
        if (await storage.Recurring.GetCalendarAsync(name, ct) is not { } calendar)
        {
            return TypedResults.NotFound();
        }

        var recurring = await storage.Recurring.ListAsync(ct);
        return TypedResults.Ok(ToDetail(calendar, recurring));
    }

    private static async Task<Results<Ok<CalendarDetailDto>, ProblemHttpResult>> UpsertCalendarAsync(
        string name, CalendarUpsertRequest request, IGuaraClient client, IStorage storage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var days = new List<DayOfWeek>();
        foreach (var raw in request.DaysOfWeek ?? [])
        {
            if (!Enum.TryParse<DayOfWeek>(raw, ignoreCase: true, out var day))
            {
                return TypedResults.Problem(
                    $"Dia da semana desconhecido: '{raw}'.", statusCode: StatusCodes.Status400BadRequest);
            }

            days.Add(day);
        }

        try
        {
            await client.AdicionarOuAtualizarCalendarioAsync(
                name,
                calendar =>
                {
                    foreach (var date in request.Dates ?? [])
                    {
                        calendar.ExcluirData(date);
                    }

                    foreach (var range in request.Ranges ?? [])
                    {
                        calendar.ExcluirIntervalo(range.Start, range.End);
                    }

                    if (days.Count > 0)
                    {
                        calendar.ExcluirDiasDaSemana([.. days]);
                    }

                    foreach (var cron in request.CronWindows ?? [])
                    {
                        calendar.ExcluirCron(cron);
                    }
                },
                ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        // Devolve o estado já recalculado: é como a UI confirma o efeito da edição.
        var saved = await storage.Recurring.GetCalendarAsync(name, ct);
        var recurring = await storage.Recurring.ListAsync(ct);
        return TypedResults.Ok(ToDetail(saved!, recurring));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteCalendarAsync(
        string name, IStorage storage, IGuaraClient client, CancellationToken ct)
    {
        if (await storage.Recurring.GetCalendarAsync(name, ct) is null)
        {
            return TypedResults.NotFound();
        }

        var users = UsersOf(await storage.Recurring.ListAsync(ct), name).Select(r => r.Id).ToList();
        if (users.Count > 0)
        {
            // Remover deixaria recorrentes apontando para um calendário inexistente.
            return TypedResults.Problem(
                $"O calendário '{name}' está em uso por: {string.Join(", ", users)}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await client.ExcluirCalendarioAsync(name, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static int RuleCount(CalendarRecord calendar)
        => calendar.ExcludedDates.Length + calendar.ExcludedRanges.Length
            + calendar.ExcludedDaysOfWeek.Length + calendar.ExcludedCronWindows.Length;

    private static IEnumerable<RecurringJobRecord> UsersOf(
        IReadOnlyList<RecurringJobRecord> recurring, string calendarName)
        => recurring.Where(r => string.Equals(r.CalendarName, calendarName, StringComparison.Ordinal));

    private static CalendarDetailDto ToDetail(
        CalendarRecord calendar, IReadOnlyList<RecurringJobRecord> recurring) => new(
            calendar.Name,
            calendar.ExcludedDates,
            [.. calendar.ExcludedRanges.Select(r => new CalendarRangeDto(r.Start, r.End))],
            [.. calendar.ExcludedDaysOfWeek.Select(d => d.ToString())],
            calendar.ExcludedCronWindows,
            [.. UsersOf(recurring, calendar.Name).Select(r => new CalendarUsageDto(r.Id, r.NextRunAt))]);

    // --- Ações em massa ---

    private static Task<Results<Ok<BulkResultDto>, ProblemHttpResult>> BulkRetryAsync(
        BulkJobsRequest request, IStorage storage, TimeProvider time, CancellationToken ct)
        => ApplyBulkAsync(request, async id =>
        {
            var job = await storage.Jobs.GetAsync(id, ct);
            if (job is null)
            {
                return "Job não encontrado.";
            }

            if (job.State != JobState.Failed)
            {
                return $"Só jobs em Failed podem ser retentados (estado atual: {job.State}).";
            }

            await storage.Jobs.RescheduleAsync(job.Id, time.GetUtcNow(), ct);
            return null;
        });

    private static Task<Results<Ok<BulkResultDto>, ProblemHttpResult>> BulkDeleteAsync(
        BulkJobsRequest request, IGuaraClient client, IStorage storage, CancellationToken ct)
        => ApplyBulkAsync(request, async id =>
        {
            if (await client.ExcluirAsync(id, ct))
            {
                return null;
            }

            return await storage.Jobs.GetAsync(id, ct) is null
                ? "Job não encontrado."
                : "O job está em execução e não pode ser excluído agora.";
        });

    /// <summary>
    /// Aplica a ação item a item e devolve o desfecho de cada um. Um item recusado não
    /// derruba o lote: o operador precisa saber exatamente o que passou e o que não.
    /// </summary>
    private static async Task<Results<Ok<BulkResultDto>, ProblemHttpResult>> ApplyBulkAsync(
        BulkJobsRequest request, Func<JobId, Task<string?>> apply)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Ids is not { Count: > 0 })
        {
            return TypedResults.Problem(
                "Informe ao menos um job.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Ids.Count > MaxBulkItems)
        {
            return TypedResults.Problem(
                $"Máximo de {MaxBulkItems} jobs por ação em massa; recebidos {request.Ids.Count}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var failures = new List<BulkFailureDto>();
        var succeeded = 0;
        foreach (var raw in request.Ids)
        {
            var reason = await apply(new JobId(raw));
            if (reason is null)
            {
                succeeded++;
            }
            else
            {
                failures.Add(new BulkFailureDto(raw, reason));
            }
        }

        return TypedResults.Ok(new BulkResultDto(request.Ids.Count, succeeded, failures));
    }
}
