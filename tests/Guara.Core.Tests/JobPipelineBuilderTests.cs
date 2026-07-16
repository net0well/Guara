using Guara.Abstractions;
using Guara.Core;
using Xunit;

namespace Guara.Core.Tests;

public class JobPipelineBuilderTests
{
    private sealed class RecordingMiddleware(string tag, List<string> log, bool shortCircuit = false) : IJobMiddleware
    {
        public async ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct)
        {
            log.Add($"{tag}:before");
            if (!shortCircuit)
            {
                await next(context, ct);
            }

            log.Add($"{tag}:after");
        }
    }

    private static JobContext NewContext()
    {
        var ctx = new JobContext();
        ctx.Initialize(new JobId("1"), new JobDescriptor("T", "M", default));
        return ctx;
    }

    [Fact]
    public async Task Middlewares_RunInRegistrationOrder()
    {
        var log = new List<string>();
        var pipeline = new JobPipelineBuilder()
            .Use(new RecordingMiddleware("a", log))
            .Use(new RecordingMiddleware("b", log))
            .Build();

        await pipeline(NewContext(), CancellationToken.None);

        Assert.Equal(new[] { "a:before", "b:before", "b:after", "a:after" }, log);
    }

    [Fact]
    public async Task ShortCircuit_StopsChain()
    {
        var log = new List<string>();
        var pipeline = new JobPipelineBuilder()
            .Use(new RecordingMiddleware("a", log, shortCircuit: true))
            .Use(new RecordingMiddleware("b", log))
            .Build();

        await pipeline(NewContext(), CancellationToken.None);

        Assert.Equal(new[] { "a:before", "a:after" }, log);
    }
}
