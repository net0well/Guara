namespace Guara.Abstractions;

/// <summary>
/// Busca jobs elegíveis no storage e sinaliza que há trabalho (evento
/// <see cref="WorkerRequested"/>). Não executa, não agenda, não serializa (spec 006).
/// </summary>
public interface IDispatcher
{
    /// <summary>Inicia o laço de busca.</summary>
    /// <param name="ct">Token de cancelamento da inicialização.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o laço foi iniciado.</returns>
    ValueTask StartAsync(CancellationToken ct);

    /// <summary>Para o laço de busca (jobs já sinalizados seguem no worker).</summary>
    /// <param name="ct">Token de cancelamento da parada.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o laço parou.</returns>
    ValueTask StopAsync(CancellationToken ct);
}
