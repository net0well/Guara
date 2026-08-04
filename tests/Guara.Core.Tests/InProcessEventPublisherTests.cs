using Guara.Abstractions;
using Guara.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    private sealed class CancellingHandler : IEventHandler<JobCompleted>
    {
        public ValueTask HandleAsync(JobCompleted @event, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Captura as entradas emitidas para verificar o registro das falhas.</summary>
    private sealed class CapturingLogger : ILogger<InProcessEventPublisher>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));
    }

    [Fact]
    public async Task PublishAsync_FansOutToAllHandlers()
    {
        var received = new List<JobId>();
        var provider = new ServiceCollection()
            .AddSingleton<IEventHandler<JobCompleted>>(new RecordingHandler(received))
            .AddSingleton<IEventHandler<JobCompleted>>(new RecordingHandler(received))
            .BuildServiceProvider();

        var publisher = new InProcessEventPublisher(provider, NullLogger<InProcessEventPublisher>.Instance);
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

        var publisher = new InProcessEventPublisher(provider, NullLogger<InProcessEventPublisher>.Instance);
        await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), CancellationToken.None);

        Assert.Single(received); // o handler que lançou não impediu o outro
    }

    [Fact]
    public async Task PublishAsync_HandlerException_IsLogged()
    {
        var logger = new CapturingLogger();
        var provider = new ServiceCollection()
            .AddSingleton<IEventHandler<JobCompleted>>(new ThrowingHandler())
            .BuildServiceProvider();

        var publisher = new InProcessEventPublisher(provider, logger);
        await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains(nameof(ThrowingHandler), entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(JobCompleted), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_Cancellation_PropagatesWithoutLogging()
    {
        var logger = new CapturingLogger();
        var provider = new ServiceCollection()
            .AddSingleton<IEventHandler<JobCompleted>>(new CancellingHandler())
            .BuildServiceProvider();

        var publisher = new InProcessEventPublisher(provider, logger);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Cancelamento não é falha de handler: sobe para o chamador e não vira log de erro.
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), cts.Token));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task PublishAsync_NoHandlers_IsNoOp()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var publisher = new InProcessEventPublisher(provider, NullLogger<InProcessEventPublisher>.Instance);

        // Não deve lançar.
        await publisher.PublishAsync(new JobCompleted(new JobId("j1"), DateTimeOffset.UnixEpoch), CancellationToken.None);
    }
}
