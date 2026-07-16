using Guara.Abstractions;
using Guara.Core;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Guara.Core.Tests;

public class JobContextPoolTests
{
    [Fact]
    public void Return_ResetsContext_NoLeakBetweenJobs()
    {
        var pool = new DefaultObjectPool<JobContext>(new JobContextPoolPolicy());

        var first = pool.Get();
        first.Initialize(new JobId("job-1"), new JobDescriptor("T", "M", default));
        first.State = JobState.Processing;
        first.Items["tenant"] = "acme";
        pool.Return(first);

        var reused = pool.Get();
        Assert.Same(first, reused);          // veio do pool
        Assert.True(reused.Id.IsEmpty);       // resetado
        Assert.Equal(JobState.Created, reused.State);
        Assert.Empty(reused.Items);           // sem vazamento do job anterior
    }
}
