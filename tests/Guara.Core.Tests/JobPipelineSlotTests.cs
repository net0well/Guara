using Guara.Abstractions;
using Guara.Core;
using Xunit;

namespace Guara.Core.Tests;

public class JobPipelineSlotTests
{
    private sealed class TagMiddleware(string tag, List<string> log) : IJobMiddleware
    {
        public async ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct)
        {
            log.Add(tag);
            await next(context, ct);
        }
    }

    private static JobContext NewContext()
    {
        var ctx = new JobContext();
        ctx.Initialize(new JobId("1"), new JobDescriptor("T", "M", default));
        return ctx;
    }

    [Fact]
    public async Task Build_OrdersBySlot_RegardlessOfRegistrationOrder()
    {
        var log = new List<string>();

        // Registrados fora de ordem de propósito.
        var pipeline = new JobPipelineBuilder()
            .Use(PipelineSlot.Notifications, new TagMiddleware("notifications", log))
            .Use(PipelineSlot.Metrics, new TagMiddleware("metrics", log))
            .Use(PipelineSlot.Validation, new TagMiddleware("validation", log))
            .Use(PipelineSlot.Custom, new TagMiddleware("custom", log))
            .Build();

        await pipeline(NewContext(), CancellationToken.None);

        Assert.Equal(new[] { "validation", "custom", "metrics", "notifications" }, log);
    }

    [Fact]
    public async Task Build_PreservesRegistrationOrderWithinSameSlot()
    {
        var log = new List<string>();

        var pipeline = new JobPipelineBuilder()
            .Use(PipelineSlot.Custom, new TagMiddleware("a", log))
            .Use(PipelineSlot.Custom, new TagMiddleware("b", log))
            .Use(PipelineSlot.Custom, new TagMiddleware("c", log))
            .Build();

        await pipeline(NewContext(), CancellationToken.None);

        Assert.Equal(new[] { "a", "b", "c" }, log);
    }
}
