using System.Diagnostics.Metrics;
using Guara.Abstractions;

namespace Guara.Diagnostics;

/// <summary>
/// Métricas de execução no medidor <c>Guara</c>: <c>guara.jobs.processed</c>
/// (contador com <c>queue</c>/<c>outcome</c>) e <c>guara.job.duration</c>
/// (histograma em ms, por fila). Tags de baixa cardinalidade — nada de id de job
/// em métrica agregada (id vive nos traces).
/// </summary>
public sealed class MetricsMiddleware : IJobMiddleware
{
    private static readonly Counter<long> Processed = GuaraDiagnostics.Meter.CreateCounter<long>(
        "guara.jobs.processed", unit: "{job}", description: "Execuções de job concluídas, por fila e desfecho.");

    private static readonly Histogram<double> Duration = GuaraDiagnostics.Meter.CreateHistogram<double>(
        "guara.job.duration", unit: "ms", description: "Duração da execução de job, por fila.");

    /// <inheritdoc />
    public async ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct)
    {
        var startedAt = TimeProvider.System.GetTimestamp();
        var outcome = "success";
        try
        {
            await next(context, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "canceled";
            throw;
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            var queue = new KeyValuePair<string, object?>("queue", context.Descriptor.Queue);
            Processed.Add(1, queue, new KeyValuePair<string, object?>("outcome", outcome));
            Duration.Record(TimeProvider.System.GetElapsedTime(startedAt).TotalMilliseconds, queue);
        }
    }
}
