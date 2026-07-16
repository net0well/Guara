using Guara.Abstractions;
using Guara.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Core.Tests;

public class InProcessEventPublisherTests
{
    private sealed class RecordingHandler(List<JobId> received) : IEventHandler<JobCompleted>
    {
        public ValueTask HandleAsync(JobCompleted @event, CancellationToken ct)
        {
            received.Add(@event.Id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IEventHandler<JobCompleted>
    {
        public ValueTask HandleAsync(JobCompleted @event, CancellationToken ct)
            => throw new InvalidOperationException("handler falhou");
    }

    [Fact]
    public async Task PublishAsync_FansOutToAllHandlers()
    {
        var received = new List<JobId>();
        var provider = new ServiceCollection()
            .AddSingleton<IEventHandler<JobCompleted>>(new RecordingHandler(received))
            .AddSingleton<IEventHandler<JobCompleted>>(new RecordingHandler(received))
            .BuildServiceProvider();

        var publisher = new InProcessEventPublisher(provider);
        await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), CancellationToken.None);

        Assert.Equal(2, received.Count);
    }

    [Fact]
    public async Task PublishAsync_HandlerException_DoesNotStopOthers()
    {
        var received = new List<JobId>();
        var provider = new ServiceCollection()
            .AddSingleton<IEventHandler<JobCompleted>>(new ThrowingHandler())
            .AddSingleton<IEventHandler<JobCompleted>>(new RecordingHandler(received))
            .BuildServiceProvider();

        var publisher = new InProcessEventPublisher(provider);
        await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), CancellationToken.None);

        Assert.Single(received); // o handler que lançou não impediu o outro
    }

    [Fact]
    public async Task PublishAsync_NoHandlers_IsNoOp()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var publisher = new InProcessEventPublisher(provider);

        // Não deve lançar.
        await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), CancellationToken.None);
    }
}
