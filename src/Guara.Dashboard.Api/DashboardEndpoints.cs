using System.Text.Json;
using Guara.Abstractions;
using Guara.Dashboard.Api;
using Guara.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder; // extensões de mapeamento aparecem junto das do ASP.NET Core

/// <summary>Mapeamento dos endpoints da API do dashboard (só JSON — a UI é outro pacote).</summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Mapeia a API do dashboard sob <paramref name="prefix"/> e devolve o grupo para
    /// o chamador aplicar autorização — a composição (<c>Guara.Dashboard</c>) faz isso
    /// por padrão. <b>Montar o grupo sem autorização expõe os dados dos jobs.</b>
    /// </summary>
    /// <param name="endpoints">Builder de rotas do host.</param>
    /// <param name="prefix">Prefixo da API (default <c>/api/v1</c>).</param>
    /// <returns>O grupo mapeado, para encadear políticas.</returns>
    public static RouteGroupBuilder MapGuaraDashboardApi(
        this IEndpointRouteBuilder endpoints, string prefix = "/api/v1")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix).WithTags("Guara");

        group.MapGet("/stats", GetStatsAsync).WithName("GuaraStats")
            .WithSummary("Contadores de jobs por estado.");
        group.MapGet("/queues", GetQueuesAsync).WithName("GuaraQueues")
            .WithSummary("Filas conhecidas e seus tamanhos.");
        group.MapGet("/jobs", GetJobsAsync).WithName("GuaraJobs")
            .WithSummary("Lista paginada de jobs, com filtros por estado e fila.");
        group.MapGet("/jobs/{id}", GetJobAsync).WithName("GuaraJobDetail")
            .WithSummary("Detalhe de um job.");
        group.MapGet("/servers", GetServersAsync).WithName("GuaraServers")
            .WithSummary("Nós servidores registrados e seus heartbeats.");
        group.MapGet("/recurring", GetRecurringAsync).WithName("GuaraRecurring")
            .WithSummary("Definições recorrentes.");
        group.MapPost("/jobs/{id}/retry", RetryJobAsync).WithName("GuaraRetryJob")
            .WithSummary("Reenfileira um job que falhou definitivamente.");
        group.MapPost("/jobs/{id}/trigger", TriggerJobAsync).WithName("GuaraTriggerJob")
            .WithSummary("Antecipa um job agendado/em retentativa para agora.");
        group.MapDelete("/jobs/{id}", DeleteJobAsync).WithName("GuaraDeleteJob")
            .WithSummary("Exclui um job que não está em execução.");
        group.MapGet("/stream", StreamAsync).WithName("GuaraStream")
            .WithSummary("Stream SSE de eventos de job em tempo real.");

        return group;
    }

    private static async Task<Ok<StatsDto>> GetStatsAsync(IStorage storage, CancellationToken ct)
    {
        var counts = await storage.Jobs.CountByStateAsync(null, ct);
        var byState = counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
        return TypedResults.Ok(new StatsDto(byState, counts.Values.Sum()));
    }

    private static async Task<Ok<IReadOnlyList<QueueDto>>> GetQueuesAsync(IStorage storage, CancellationToken ct)
    {
        var names = await storage.Queues.GetQueuesAsync(ct);
        var queues = new List<QueueDto>(names.Count);
        foreach (var name in names)
        {
            queues.Add(new QueueDto(name, await storage.Queues.GetLengthAsync(name, ct)));
        }

        return TypedResults.Ok<IReadOnlyList<QueueDto>>(queues);
    }

    private static async Task<Results<Ok<PageDto<JobSummaryDto>>, ProblemHttpResult>> GetJobsAsync(
        IStorage storage, string? state, string? queue, int page = 1, int pageSize = 50,
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

        var effectivePage = Math.Max(1, page);
        var effectivePageSize = Math.Clamp(pageSize, 1, JobQuery.MaxPageSize);
        var jobs = await storage.Jobs.ListAsync(
            new JobQuery(stateFilter, queue, effectivePage, effectivePageSize), ct);
        var items = jobs.Select(ToSummary).ToList();
        return TypedResults.Ok(new PageDto<JobSummaryDto>(items, effectivePage, effectivePageSize));
    }

    private static async Task<Results<Ok<JobDetailDto>, NotFound>> GetJobAsync(
        string id, IStorage storage, CancellationToken ct)
    {
        var job = await storage.Jobs.GetAsync(new JobId(id), ct);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new JobDetailDto(
            job.Id.Value, job.Descriptor.TypeName, job.Descriptor.MethodName, job.Queue,
            job.State.ToString(), job.Attempt, job.CreatedAt, job.ScheduledFor, job.LeaseUntil,
            job.FinishedAt, job.Result, job.Error, job.Descriptor.Metadata));
    }

    private static async Task<Ok<IReadOnlyList<ServerDto>>> GetServersAsync(IStorage storage, CancellationToken ct)
    {
        var servers = await storage.Servers.ListAsync(ct);
        IReadOnlyList<ServerDto> dtos = servers
            .Select(node => new ServerDto(
                node.Id, node.MachineName, node.StartedAt, node.LastHeartbeat, node.Queues, node.MaxConcurrency))
            .ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Ok<IReadOnlyList<RecurringDto>>> GetRecurringAsync(IStorage storage, CancellationToken ct)
    {
        var definitions = await storage.Recurring.ListAsync(ct);
        IReadOnlyList<RecurringDto> dtos = definitions
            .Select(recurring => new RecurringDto(
                recurring.Id, recurring.Description, recurring.Queue, recurring.CronExpression,
                recurring.Interval?.ToString("c"), recurring.TimeZoneId, recurring.Paused,
                recurring.SkipIfPreviousRunning, recurring.NextRunAt, recurring.LastRunAt, recurring.LastSkippedAt))
            .ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RetryJobAsync(
        string id, IStorage storage, TimeProvider time, CancellationToken ct)
    {
        var job = await storage.Jobs.GetAsync(new JobId(id), ct);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.State != JobState.Failed)
        {
            return TypedResults.Problem(
                $"Só jobs em Failed podem ser retentados (estado atual: {job.State}).",
                statusCode: StatusCodes.Status409Conflict);
        }

        // A tentativa é preservada: nova falha volta a Failed imediatamente (histórico honesto).
        await storage.Jobs.RescheduleAsync(job.Id, time.GetUtcNow(), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> TriggerJobAsync(
        string id, IStorage storage, TimeProvider time, CancellationToken ct)
    {
        var job = await storage.Jobs.GetAsync(new JobId(id), ct);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.State is not (JobState.Scheduled or JobState.Retrying))
        {
            return TypedResults.Problem(
                $"Só jobs agendados/em retentativa podem ser antecipados (estado atual: {job.State}).",
                statusCode: StatusCodes.Status409Conflict);
        }

        await storage.Jobs.RescheduleAsync(job.Id, time.GetUtcNow(), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteJobAsync(
        string id, IGuaraClient client, IStorage storage, CancellationToken ct)
    {
        if (await client.ExcluirAsync(new JobId(id), ct))
        {
            return TypedResults.NoContent();
        }

        return await storage.Jobs.GetAsync(new JobId(id), ct) is null
            ? TypedResults.NotFound()
            : TypedResults.Problem(
                "O job está em execução e não pode ser excluído agora.",
                statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task StreamAsync(
        HttpContext context, DashboardEventStream stream, CancellationToken ct)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        // Proxies (nginx) precisam de buffering desligado nesta rota; ver Infra/nginx.
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.Body.FlushAsync(ct);

        // Keep-alive periódico mantém a conexão viva através de proxies.
        using var heartbeat = new CancellationTokenSource();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeat.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), heartbeat.Token);
                await context.Response.WriteAsync(": ping\n\n", heartbeat.Token);
                await context.Response.Body.FlushAsync(heartbeat.Token);
            }
        }, heartbeat.Token);

        try
        {
            await foreach (var (sequence, @event) in stream.SubscribeAsync(ct))
            {
                var payload = JsonSerializer.Serialize(@event, DashboardJsonContext.Default.JobEventDto);
                await context.Response.WriteAsync($"id: {sequence}\nevent: job\ndata: {payload}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cliente desconectou: fim normal do stream.
        }
        finally
        {
            await heartbeat.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static JobSummaryDto ToSummary(JobRecord job) => new(
        job.Id.Value, job.Descriptor.TypeName, job.Descriptor.MethodName, job.Queue,
        job.State.ToString(), job.Attempt, job.CreatedAt, job.ScheduledFor, job.FinishedAt);
}
