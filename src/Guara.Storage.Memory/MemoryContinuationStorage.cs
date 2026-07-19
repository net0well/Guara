using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>
/// Vínculos de continuação em memória. A resolução é atômica sob o lock do
/// dicionário — entre chamadores concorrentes, apenas um resolve cada vínculo.
/// </summary>
internal sealed class MemoryContinuationStorage : IContinuationStorage
{
    private readonly object _sync = new();
    private readonly Dictionary<JobId, ContinuationRecord> _continuations = [];

    public ValueTask AddAsync(ContinuationRecord record, CancellationToken ct)
    {
        lock (_sync)
        {
            _continuations.TryAdd(record.ChildId, record); // idempotente para o mesmo filho
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ContinuationRecord?> GetByChildAsync(JobId childId, CancellationToken ct)
    {
        lock (_sync)
        {
            return ValueTask.FromResult(_continuations.TryGetValue(childId, out var record) ? record : null);
        }
    }

    public ValueTask<IReadOnlyList<ContinuationRecord>> ListByParentAsync(JobId parentId, CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyList<ContinuationRecord> result = _continuations.Values
                .Where(c => c.ParentId == parentId)
                .OrderBy(c => c.CreatedAt)
                .ToList();
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<IReadOnlyList<ContinuationRecord>> ListPendingAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyList<ContinuationRecord> result = _continuations.Values
                .Where(c => c.Status == ContinuationStatus.Pending)
                .OrderBy(c => c.CreatedAt)
                .ToList();
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<bool> TryResolveAsync(
        JobId childId, ContinuationStatus status, string? reason, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_continuations.TryGetValue(childId, out var record) || record.Status != ContinuationStatus.Pending)
            {
                return ValueTask.FromResult(false); // já resolvido por outro chamador (ou não existe)
            }

            _continuations[childId] = record with { Status = status, Reason = reason, ResolvedAt = resolvedAt };
            return ValueTask.FromResult(true);
        }
    }
}
