using Guara.Abstractions;
using Guara.Core;
using Xunit;

namespace Guara.Core.Tests;

public class RetryMiddlewareTests
{
    // O middleware conta por InProcessAttempts, e não por MaxAttempts: os dois modos se
    // compõem, e o que sobrevive a estas repetições vira uma tentativa persistente. Aqui
    // MaxAttempts é deixado de propósito num valor diferente, para que um retorno ao
    // acoplamento antigo quebre o teste em vez de passar despercebido.
    private static readonly RetryOptions NoBackoff3 =
        new() { InProcessAttempts = 3, MaxAttempts = 7, Backoff = static _ => TimeSpan.Zero };

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
    public async Task Succeeds_AfterTransientFailures_WithinInProcessAttempts()
    {
        var counter = new Counter();
        var mw = new RetryMiddleware(NoBackoff3);

        await mw.InvokeAsync(NewContext(), FailNTimesThenSucceed(2, counter), CancellationToken.None);

        Assert.Equal(3, counter.Calls); // 2 falhas + 1 sucesso
    }

    [Fact]
    public async Task GivesUp_AfterInProcessAttempts_AndRethrows()
    {
        var counter = new Counter();
        var mw = new RetryMiddleware(NoBackoff3);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mw.InvokeAsync(NewContext(), FailNTimesThenSucceed(int.MaxValue, counter), CancellationToken.None));

        Assert.Equal(4, counter.Calls); // 1ª tentativa + 3 retentativas
    }

    /// <summary>É o default: sem opt-in explícito, nada é repetido em processo.</summary>
    [Fact]
    public async Task InProcessAttemptsZero_DoesNotRetry()
    {
        var counter = new Counter();
        var mw = new RetryMiddleware(new RetryOptions { Backoff = static _ => TimeSpan.Zero });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mw.InvokeAsync(NewContext(), FailNTimesThenSucceed(int.MaxValue, counter), CancellationToken.None));

        Assert.Equal(1, counter.Calls); // sem retentativa
    }
}
