namespace Guara.Abstractions;

/// <summary>Publica eventos de domínio do Guará para os handlers registrados.</summary>
public interface IEventPublisher
{
    /// <summary>Publica um evento.</summary>
    /// <typeparam name="TEvent">Tipo do evento.</typeparam>
    /// <param name="event">Evento a publicar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a publicação termina.</returns>
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IGuaraEvent;
}

/// <summary>Manipula um evento de domínio do Guará.</summary>
/// <typeparam name="TEvent">Tipo do evento tratado.</typeparam>
public interface IEventHandler<in TEvent> where TEvent : IGuaraEvent
{
    /// <summary>Trata o evento.</summary>
    /// <param name="event">Evento recebido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o tratamento termina.</returns>
    ValueTask HandleAsync(TEvent @event, CancellationToken ct);
}
