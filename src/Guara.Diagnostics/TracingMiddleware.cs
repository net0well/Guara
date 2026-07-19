using System.Diagnostics;
using Guara.Abstractions;

namespace Guara.Diagnostics;

/// <summary>
/// Um span por execução de job na fonte <c>Guara</c>, com as tags
/// <c>job.id</c>/<c>job.queue</c>/<c>job.type</c>/<c>job.attempt</c>. Sem listener
/// registrado, o custo é ~zero (nenhuma atividade é criada).
/// </summary>
public sealed class TracingMiddleware : IJobMiddleware
{
    /// <inheritdoc />
    public async ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct)
    {
        using var activity = GuaraDiagnostics.ActivitySource.StartActivity("guara.job");
        activity?.SetTag("job.id", context.Id.Value);
        activity?.SetTag("job.queue", context.Descriptor.Queue);
        activity?.SetTag("job.type", context.Descriptor.TypeName);
        activity?.SetTag("job.attempt", context.Attempt);

        try
        {
            await next(context, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
