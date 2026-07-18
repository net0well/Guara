using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Scheduler;

/// <summary>
/// Implementação default de <see cref="IGuaraClient"/>: persiste o job no storage
/// ("o storage é a fila") e emite os eventos do fluxo (<see cref="JobCreated"/>,
/// <see cref="JobScheduled"/>) — nunca chama outro componente diretamente.
/// </summary>
public sealed class GuaraClient(IStorage storage, IEventPublisher events, TimeProvider time) : IGuaraClient
{
    /// <inheritdoc />
    public async ValueTask<JobId> EnfileirarAsync(JobDescriptor job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var now = time.GetUtcNow();
        var id = NewId();

        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = id,
            Descriptor = job,
            State = JobState.Enqueued,
            Queue = job.Queue,
            CreatedAt = now,
        }, ct);

        await events.PublishAsync(new JobCreated(id, now), ct);
        return id;
    }

    /// <inheritdoc />
    public async ValueTask<JobId> AgendarAsync(JobDescriptor job, TimeSpan atraso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfLessThan(atraso, TimeSpan.Zero);

        var now = time.GetUtcNow();
        var id = NewId();

        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = id,
            Descriptor = job,
            State = JobState.Scheduled,
            Queue = job.Queue,
            CreatedAt = now,
            ScheduledFor = now + atraso,
        }, ct);

        await events.PublishAsync(new JobCreated(id, now), ct);
        await events.PublishAsync(new JobScheduled(id, now), ct);
        return id;
    }

    /// <inheritdoc />
    public ValueTask<bool> ExcluirAsync(JobId id, CancellationToken ct = default)
        => storage.Jobs.DeleteAsync(id, ct);

    private static JobId NewId() => new(Guid.NewGuid().ToString("n"));
}
