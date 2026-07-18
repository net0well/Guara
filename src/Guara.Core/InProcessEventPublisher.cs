using Guara.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Core;

/// <summary>
/// Publicador de eventos <b>em processo</b>: faz fan-out síncrono para todos os
/// <see cref="IEventHandler{TEvent}"/> registrados no contêiner. Entrega best-effort —
/// a falha de um handler não impede os demais. A resolução usa o
/// tipo estático <c>TEvent</c> (genérico fechado), sem reflection → AOT-safe.
/// </summary>
/// <remarks>
/// A variante com buffer assíncrono via <c>Channel&lt;T&gt;</c> (desacoplando publicação de
/// entrega) é fornecida pelo <c>Guara.Server</c>, que roda o laço de fundo — este publicador
/// é o padrão em processo, consumindo o mesmo contrato <see cref="IEventHandler{TEvent}"/>.
/// </remarks>
public sealed class InProcessEventPublisher(IServiceProvider services) : IEventPublisher
{
    /// <inheritdoc />
    public async ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
        where TEvent : IGuaraEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(@event, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Best-effort: isola a falha de um handler para não afetar os demais.
                // O logging estruturado da falha é adicionado quando o Guara.Diagnostics envolve o pipeline.
            }
        }
    }
}
