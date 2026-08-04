using System.Collections.Concurrent;
using System.Threading.Channels;
using Guara.Abstractions;

namespace Guara.Dashboard.Api;

/// <summary>
/// Ponte entre o event bus do Guará e os clientes SSE conectados: cada assinante tem
/// um canal limitado (cliente lento perde eventos antigos, nunca trava o publicador —
/// o estado verdadeiro está sempre na API de consulta). Os eventos carregam um número
/// de sequência para reconexão via <c>Last-Event-ID</c>.
/// </summary>
internal sealed class DashboardEventStream :
    IEventHandler<JobCreated>,
    IEventHandler<JobScheduled>,
    IEventHandler<JobCompleted>,
    IEventHandler<JobFailed>,
    IEventHandler<JobRetryScheduled>
{
    private readonly ConcurrentDictionary<Guid, Channel<(long Sequence, JobEventDto Event)>> _subscribers = new();
    private long _sequence;

    /// <summary>Assina o stream; o cancelamento encerra a assinatura.</summary>
    /// <param name="ct">Token do cliente conectado.</param>
    /// <returns>Sequência de eventos com o número para <c>Last-Event-ID</c>.</returns>
    public async IAsyncEnumerable<(long Sequence, JobEventDto Event)> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<(long, JobEventDto)>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _subscribers[id] = channel;

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct))
            {
                yield return item;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(JobCreated @event, CancellationToken ct)
        => Broadcast(new JobEventDto("created", @event.Id.Value, @event.OccurredAt));

    /// <inheritdoc />
    public ValueTask HandleAsync(JobScheduled @event, CancellationToken ct)
        => Broadcast(new JobEventDto("scheduled", @event.Id.Value, @event.OccurredAt));

    /// <inheritdoc />
    public ValueTask HandleAsync(JobCompleted @event, CancellationToken ct)
        => Broadcast(new JobEventDto("completed", @event.Id.Value, @event.OccurredAt));

    /// <inheritdoc />
    public ValueTask HandleAsync(JobFailed @event, CancellationToken ct)
        => Broadcast(new JobEventDto("failed", @event.Id.Value, @event.OccurredAt, Reason: @event.Reason));

    /// <inheritdoc />
    public ValueTask HandleAsync(JobRetryScheduled @event, CancellationToken ct)
        => Broadcast(new JobEventDto("retry-scheduled", @event.Id.Value, @event.OccurredAt, Attempt: @event.Attempt));

    private ValueTask Broadcast(JobEventDto @event)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite((sequence, @event)); // DropOldest: nunca bloqueia
        }

        return ValueTask.CompletedTask;
    }
}
