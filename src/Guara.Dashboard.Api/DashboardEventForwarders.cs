using Guara.Abstractions;

namespace Guara.Dashboard.Api;

// Encaminhadores tipados para o stream singleton: TryAddEnumerable exige tipos de
// implementação distinguíveis (factories anônimas não deduplicam).
internal sealed class JobCreatedStreamForwarder(DashboardEventStream stream) : IEventHandler<JobCreated>
{
    public ValueTask HandleAsync(JobCreated @event, CancellationToken ct) => stream.HandleAsync(@event, ct);
}

internal sealed class JobScheduledStreamForwarder(DashboardEventStream stream) : IEventHandler<JobScheduled>
{
    public ValueTask HandleAsync(JobScheduled @event, CancellationToken ct) => stream.HandleAsync(@event, ct);
}

internal sealed class JobCompletedStreamForwarder(DashboardEventStream stream) : IEventHandler<JobCompleted>
{
    public ValueTask HandleAsync(JobCompleted @event, CancellationToken ct) => stream.HandleAsync(@event, ct);
}

internal sealed class JobFailedStreamForwarder(DashboardEventStream stream) : IEventHandler<JobFailed>
{
    public ValueTask HandleAsync(JobFailed @event, CancellationToken ct) => stream.HandleAsync(@event, ct);
}

internal sealed class JobRetryScheduledStreamForwarder(DashboardEventStream stream) : IEventHandler<JobRetryScheduled>
{
    public ValueTask HandleAsync(JobRetryScheduled @event, CancellationToken ct) => stream.HandleAsync(@event, ct);
}
