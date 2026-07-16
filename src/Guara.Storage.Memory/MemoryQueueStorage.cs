using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>Introspecção de filas derivada dos jobs em memória.</summary>
internal sealed class MemoryQueueStorage(MemoryJobStorage jobs) : IQueueStorage
{
    public ValueTask<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct)
    {
        IReadOnlyList<string> queues = jobs.Snapshot()
            .Select(j => j.Queue)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return ValueTask.FromResult(queues);
    }

    public ValueTask<long> GetLengthAsync(string queue, CancellationToken ct)
    {
        var length = jobs.Snapshot().LongCount(j => j.Queue == queue && j.State == JobState.Enqueued);
        return ValueTask.FromResult(length);
    }
}
