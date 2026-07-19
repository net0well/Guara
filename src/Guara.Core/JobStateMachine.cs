using Guara.Abstractions;

namespace Guara.Core;

/// <summary>Governa as transições válidas de <see cref="JobState"/>.</summary>
public sealed class JobStateMachine
{
    private static readonly Dictionary<JobState, JobState[]> Allowed = new()
    {
        [JobState.Created] = [JobState.Enqueued, JobState.Scheduled],
        [JobState.Scheduled] = [JobState.Enqueued],
        [JobState.Enqueued] = [JobState.Processing],
        // Processing → Scheduled: devolução à fila sem consumir tentativa (ex.: mutex ocupado).
        [JobState.Processing] = [JobState.Succeeded, JobState.Failed, JobState.Retrying, JobState.Scheduled],
        [JobState.Retrying] = [JobState.Enqueued, JobState.Scheduled],
        [JobState.Failed] = [JobState.Enqueued],
        [JobState.Succeeded] = [],
    };

    /// <summary>Indica se a transição <paramref name="from"/> → <paramref name="to"/> é válida.</summary>
    public bool CanTransition(JobState from, JobState to)
        => Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>Aplica a transição, lançando se inválida.</summary>
    /// <param name="from">Estado atual.</param>
    /// <param name="to">Estado desejado.</param>
    /// <returns>O novo estado (<paramref name="to"/>).</returns>
    /// <exception cref="InvalidOperationException">Quando a transição não é permitida.</exception>
    public JobState Transition(JobState from, JobState to)
        => CanTransition(from, to)
            ? to
            : throw new InvalidOperationException($"Transição de estado inválida: {from} → {to}.");
}
