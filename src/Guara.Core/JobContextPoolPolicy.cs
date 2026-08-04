using Microsoft.Extensions.ObjectPool;

namespace Guara.Core;

/// <summary>
/// Política de pool para <see cref="JobContext"/>: reaproveita instâncias entre jobs,
/// chamando <see cref="JobContext.Reset"/> na devolução (alocação amortizada ~zero).
/// </summary>
internal sealed class JobContextPoolPolicy : IPooledObjectPolicy<JobContext>
{
    /// <inheritdoc />
    public JobContext Create() => new();

    /// <inheritdoc />
    public bool Return(JobContext obj)
    {
        obj.Reset();
        return true;
    }
}
