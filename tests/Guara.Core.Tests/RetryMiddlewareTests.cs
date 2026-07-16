using Guara.Abstractions;
using Guara.Core;
using Xunit;

namespace Guara.Core.Tests;

public class RetryMiddlewareTests
{
    private static readonly RetryOptions NoBackoff3 = new() { MaxAttempts = 3, Backoff = static _ => TimeSpan.Zero };

    private static JobContext NewContext()
    {
        var ctx = new JobContext();
        ctx.Initialize(new JobId("1"), new JobDescriptor("T", "M", default));
        return ctx;
    }

    private static JobDelegate FailNTimesThenSucceed(int failures, Counter counter) =>
        (_, _) =>
        {
            counter.Calls++;
            if (counter.Calls <= failures)
            {
                throw new InvalidOperationException("boom");
            }

            return ValueTask.CompletedTask;
        };

    private sealed class Counter { public int Calls; }

    [Fact]
    public async Task Succeeds_AfterTransientFailures_WithinMaxAttempts()
    {
        var counter = new Counter();
        var mw = new RetryMiddleware(NoBackoff3);

        await mw.InvokeAsync(NewContext(), FailNTimesThenSucceed(2, counter), CancellationToken.None);

        Assert.Equal(3, counter.Calls); // 2 falhas + 1 sucesso
    }

    [Fact]
    public async Task GivesUp_AfterMaxAttempts_AndRethrows()
    {
        var counter = new Counter();
        var mw = new RetryMiddleware(NoBackoff3);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mw.InvokeAsync(NewContext(), FailNTimesThenSucceed(int.MaxValue, counter), CancellationToken.None));

        Assert.Equal(4, counter.Calls); // 1ª tentativa + 3 retentativas
    }

    [Fact]
    public async Task MaxAttemptsZero_DoesNotRetry()
    {
        var counter = new Counter();
        var mw = new RetryMiddleware(new RetryOptions { MaxAttempts = 0, Backoff = static _ => TimeSpan.Zero });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mw.InvokeAsync(NewContext(), FailNTimesThenSucceed(int.MaxValue, counter), CancellationToken.None));

        Assert.Equal(1, counter.Calls); // sem retentativa
    }
}
