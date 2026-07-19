using Guara.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Guara.Diagnostics;

/// <summary>
/// Logging estruturado da execução: escopo com <c>JobId</c>/<c>Queue</c>/<c>JobType</c>/<c>Attempt</c>
/// envolvendo o restante do pipeline, início em Debug e desfecho com duração. Falha é
/// logada e <b>relançada</b> — quem decide retentativa/estado é o executor. Argumentos
/// do job nunca são logados (dados sensíveis).
/// </summary>
public sealed class LoggingMiddleware(ILogger<LoggingMiddleware>? logger = null) : IJobMiddleware
{
    private readonly ILogger _logger = logger ?? NullLogger<LoggingMiddleware>.Instance;

    /// <inheritdoc />
    public async ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct)
    {
        // Dictionary<string, object> (sem anulável): é a forma que os formatters
        // estruturados reconhecem como pares chave-valor (KeyValuePair não tem variância).
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = context.Id.Value,
            ["Queue"] = context.Descriptor.Queue,
            ["JobType"] = context.Descriptor.TypeName,
            ["Attempt"] = context.Attempt,
        });

        _logger.LogDebug("Job {JobId} iniciado (tentativa {Attempt})", context.Id.Value, context.Attempt);
        var startedAt = TimeProvider.System.GetTimestamp();
        try
        {
            await next(context, ct);
            _logger.LogInformation(
                "Job {JobId} concluído em {DurationMs:F1} ms",
                context.Id.Value, ElapsedMs(startedAt));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Job {JobId} cancelado após {DurationMs:F1} ms (shutdown ou posse perdida)",
                context.Id.Value, ElapsedMs(startedAt));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Job {JobId} falhou após {DurationMs:F1} ms (tentativa {Attempt})",
                context.Id.Value, ElapsedMs(startedAt), context.Attempt);
            throw;
        }
    }

    private static double ElapsedMs(long startedAt)
        => TimeProvider.System.GetElapsedTime(startedAt).TotalMilliseconds;
}
