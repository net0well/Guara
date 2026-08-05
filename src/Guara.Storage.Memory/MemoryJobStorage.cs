using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>
/// Persistência de jobs em memória. Atomicidade por exclusão mútua sobre o dicionário —
/// nenhum job é adquirido por dois consumidores.
/// </summary>
internal sealed class MemoryJobStorage(TimeProvider time) : IJobStorage
{
    private readonly object _sync = new();
    private readonly Dictionary<JobId, JobRecord> _jobs = [];

    public ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct)
    {
        lock (_sync)
        {
            _jobs.TryAdd(record.Id, record); // idempotente para o mesmo id
        }

        return ValueTask.FromResult(record.Id);
    }

    public ValueTask<JobId> CreateAsync(JobRecord record, IGuaraTransaction transaction, CancellationToken ct)
        => throw new NotSupportedException(
            "MemoryStorage não participa de transação do chamador " +
            "(Capabilities.SupportsTransactions = false): não há banco com que compartilhar " +
            "uma. Para enfileirar junto com a gravação do negócio, use um provider relacional.");

    public ValueTask<JobRecord?> AcquireNextDueAsync(string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            JobRecord? eligible = null;
            var elegivelEm = DateTimeOffset.MaxValue;
            foreach (var job in _jobs.Values)
            {
                if (job.Queue != queue || JobEligibility.For(job) is not { } quando || quando > now)
                {
                    continue;
                }

                if (eligible is null || quando < elegivelEm)
                {
                    eligible = job; // ~FIFO por elegibilidade, igual aos providers persistentes
                    elegivelEm = quando;
                }
            }

            if (eligible is null)
            {
                return ValueTask.FromResult<JobRecord?>(null);
            }

            var acquired = eligible with { State = JobState.Processing, LeaseUntil = now + lease };
            _jobs[acquired.Id] = acquired;
            return ValueTask.FromResult<JobRecord?>(acquired);
        }
    }

    public ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State != JobState.Processing || job.LeaseUntil is null)
            {
                return ValueTask.FromResult(false); // posse perdida — o worker deve abortar
            }

            _jobs[id] = job with { LeaseUntil = time.GetUtcNow() + lease };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask ScheduleRetryAsync(JobId id, string error, DateTimeOffset retryAt, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(id, out var job))
            {
                _jobs[id] = job with
                {
                    State = JobState.Retrying,
                    Error = error,
                    Attempt = job.Attempt + 1,
                    ScheduledFor = retryAt,
                    LeaseUntil = null, // posse liberada: a reexecução será adquirida como qualquer job vencido
                };
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RescheduleAsync(JobId id, DateTimeOffset scheduledFor, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(id, out var job))
            {
                _jobs[id] = job with
                {
                    State = JobState.Scheduled,
                    ScheduledFor = scheduledFor,
                    LeaseUntil = null, // devolução sem consumo de tentativa: posse liberada
                };
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(id, out var job))
            {
                _jobs[id] = job with
                {
                    State = state,
                    Result = state == JobState.Succeeded ? resultOrError : job.Result,
                    Error = state is JobState.Failed or JobState.Retrying ? resultOrError : job.Error,
                    // estados não-Processing liberam a posse
                    LeaseUntil = state == JobState.Processing ? job.LeaseUntil : null,
                    // primeira transição terminal carimba o fim; reaplicações preservam o instante original
                    FinishedAt = state is JobState.Succeeded or JobState.Failed
                        ? job.FinishedAt ?? time.GetUtcNow()
                        : job.FinishedAt,
                };
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<int> PurgeAsync(JobState state, DateTimeOffset finishedBefore, CancellationToken ct)
    {
        if (state is not (JobState.Succeeded or JobState.Failed))
        {
            throw new ArgumentException(
                $"Apenas estados terminais (Succeeded/Failed) podem ser purgados; recebido: {state}.", nameof(state));
        }

        lock (_sync)
        {
            var expired = _jobs.Values
                .Where(j => j.State == state && j.FinishedAt is { } finished && finished < finishedBefore)
                .Select(j => j.Id)
                .ToList();

            foreach (var id in expired)
            {
                _jobs.Remove(id);
            }

            return ValueTask.FromResult(expired.Count);
        }
    }

    public ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct)
    {
        lock (_sync)
        {
            return ValueTask.FromResult(_jobs.TryGetValue(id, out var job) ? job : null);
        }
    }

    public ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State == JobState.Processing)
            {
                return ValueTask.FromResult(false); // inexistente ou em execução
            }

            _jobs.Remove(id);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<IReadOnlyDictionary<JobState, long>> CountByStateAsync(string? queue, CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyDictionary<JobState, long> counts = _jobs.Values
                .Where(j => queue is null || j.Queue == queue)
                .GroupBy(j => j.State)
                .ToDictionary(g => g.Key, g => g.LongCount());
            return ValueTask.FromResult(counts);
        }
    }

    public ValueTask<IReadOnlyList<JobRecord>> ListAsync(JobQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = query.EffectivePage;
        var pageSize = query.EffectivePageSize;

        lock (_sync)
        {
            IReadOnlyList<JobRecord> result = _jobs.Values
                .Where(query.Matches)
                .OrderByDescending(j => j.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<long> CountAsync(JobQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_sync)
        {
            return ValueTask.FromResult(_jobs.Values.LongCount(query.Matches));
        }
    }

    public ValueTask<IReadOnlyList<JobSeriesPoint>> GetSeriesAsync(JobSeriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        List<JobRecord> finished;
        lock (_sync)
        {
            finished = _jobs.Values
                .Where(j => j.State is JobState.Succeeded or JobState.Failed
                         && j.FinishedAt is { } at && at >= query.From && at < query.To
                         && (query.Queue is null || j.Queue == query.Queue))
                .ToList();
        }

        // Um balde por posição fixa na janela: o gráfico precisa da série contínua, então
        // períodos sem job finalizado aparecem zerados em vez de sumirem do eixo.
        var byBucket = finished
            .GroupBy(j => Floor(j.FinishedAt!.Value, query))
            .ToDictionary(g => g.Key, g => g.ToList());

        var points = new List<JobSeriesPoint>();
        foreach (var start in query.Buckets())
        {
            if (!byBucket.TryGetValue(start, out var jobs))
            {
                points.Add(new JobSeriesPoint(start, 0, 0, null, null));
                continue;
            }

            var latencies = jobs
                .Select(j => j.FinishedAt!.Value - j.CreatedAt)
                .Order()
                .ToList();

            points.Add(new JobSeriesPoint(
                start,
                jobs.LongCount(j => j.State == JobState.Succeeded),
                jobs.LongCount(j => j.State == JobState.Failed),
                JobLatency.Percentile(latencies, 0.50),
                JobLatency.Percentile(latencies, 0.95)));
        }

        return ValueTask.FromResult<IReadOnlyList<JobSeriesPoint>>(points);
    }

    private static DateTimeOffset Floor(DateTimeOffset instant, JobSeriesQuery query)
        => query.From + TimeSpan.FromTicks((instant - query.From).Ticks / query.Bucket.Ticks * query.Bucket.Ticks);

    /// <summary>Snapshot para introspecção de filas (usado por <see cref="MemoryQueueStorage"/>).</summary>
    internal List<JobRecord> Snapshot()
    {
        lock (_sync)
        {
            return [.. _jobs.Values];
        }
    }

}
