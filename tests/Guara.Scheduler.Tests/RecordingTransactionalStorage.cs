using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.Memory;

namespace Guara.Scheduler.Tests;

/// <summary>
/// Storage in-memory que <b>aceita</b> transação do chamador e anota o handle recebido.
/// Existe porque o provider in-memory recusa transações por natureza, e o que está sob
/// teste aqui é o cliente: se ele repassa o handle e o que deixa de emitir por causa dele.
/// </summary>
internal sealed class RecordingTransactionalStorage(TimeProvider time) : IStorage
{
    private readonly MemoryStorage _inner = new(time);

    public RecordingJobStorage RecordingJobs => field ??= new RecordingJobStorage(_inner.Jobs);

    public StorageCapabilities Capabilities => _inner.Capabilities with { SupportsTransactions = true };

    public IJobStorage Jobs => RecordingJobs;

    public IQueueStorage Queues => _inner.Queues;

    public ILockProvider Locks => _inner.Locks;

    public IServerRegistry Servers => _inner.Servers;

    public IRecurringStorage Recurring => _inner.Recurring;

    public IContinuationStorage Continuations => _inner.Continuations;
}

/// <summary>Delega tudo ao storage real; só a criação passa pelo registro.</summary>
internal sealed class RecordingJobStorage(IJobStorage inner) : IJobStorage
{
    /// <summary>Handles recebidos na criação transacional, na ordem.</summary>
    public List<IGuaraTransaction> TransacoesRecebidas { get; } = [];

    public ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct)
        => inner.CreateAsync(record, ct);

    public ValueTask<JobId> CreateAsync(JobRecord record, IGuaraTransaction transaction, CancellationToken ct)
    {
        TransacoesRecebidas.Add(transaction);
        return inner.CreateAsync(record, ct);
    }

    public ValueTask<IReadOnlyList<JobRecord>> AcquireNextDueAsync(
        string queue, int max, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
        => inner.AcquireNextDueAsync(queue, max, lease, now, ct);

    public ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct)
        => inner.RenewLeaseAsync(id, lease, ct);

    public ValueTask ScheduleRetryAsync(JobId id, string error, DateTimeOffset retryAt, CancellationToken ct)
        => inner.ScheduleRetryAsync(id, error, retryAt, ct);

    public ValueTask RescheduleAsync(JobId id, DateTimeOffset scheduledFor, CancellationToken ct)
        => inner.RescheduleAsync(id, scheduledFor, ct);

    public ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct)
        => inner.UpdateStateAsync(id, state, resultOrError, ct);

    public ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct) => inner.GetAsync(id, ct);

    public ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct) => inner.DeleteAsync(id, ct);

    public ValueTask<int> PurgeAsync(JobState state, DateTimeOffset finishedBefore, CancellationToken ct)
        => inner.PurgeAsync(state, finishedBefore, ct);

    public ValueTask<IReadOnlyDictionary<JobState, long>> CountByStateAsync(string? queue, CancellationToken ct)
        => inner.CountByStateAsync(queue, ct);

    public ValueTask<IReadOnlyList<JobRecord>> ListAsync(JobQuery query, CancellationToken ct)
        => inner.ListAsync(query, ct);

    public ValueTask<long> CountAsync(JobQuery query, CancellationToken ct) => inner.CountAsync(query, ct);

    public ValueTask<IReadOnlyList<JobSeriesPoint>> GetSeriesAsync(JobSeriesQuery query, CancellationToken ct)
        => inner.GetSeriesAsync(query, ct);
}

/// <summary>Handle qualquer: o duplo não o interpreta, só registra que chegou.</summary>
internal sealed class FakeCallerTransaction : IGuaraTransaction;
