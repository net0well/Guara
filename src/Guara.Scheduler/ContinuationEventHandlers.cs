using Guara.Abstractions;

namespace Guara.Scheduler;

/// <summary>Dispara a avaliação das continuações quando um pai conclui com sucesso.</summary>
internal sealed class ContinuationOnParentCompleted(ContinuationPromoter promoter) : IEventHandler<JobCompleted>
{
    public ValueTask HandleAsync(JobCompleted @event, CancellationToken ct)
        => promoter.PromoteAsync(@event.Id, JobState.Succeeded, ct);
}

/// <summary>Dispara a avaliação das continuações quando um pai falha definitivamente.</summary>
internal sealed class ContinuationOnParentFailed(ContinuationPromoter promoter) : IEventHandler<JobFailed>
{
    public ValueTask HandleAsync(JobFailed @event, CancellationToken ct)
        => promoter.PromoteAsync(@event.Id, JobState.Failed, ct);
}
