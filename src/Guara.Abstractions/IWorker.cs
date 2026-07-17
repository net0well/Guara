namespace Guara.Abstractions;

/// <summary>
/// Gerencia <b>capacidade</b>: consome sinais de trabalho, limita a concorrência,
/// renova a posse (lease) durante a execução e delega ao <see cref="IExecutor"/>.
/// Não agenda, não busca, não sabe como o job roda por dentro (spec 007).
/// </summary>
public interface IWorker
{
    /// <summary>Inicia os slots de processamento.</summary>
    /// <param name="ct">Token de cancelamento da inicialização.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando os slots iniciaram.</returns>
    ValueTask StartAsync(CancellationToken ct);

    /// <summary>
    /// Para com drain gracioso: deixa de aceitar novos jobs, aguarda os em andamento
    /// até o timeout configurado e então cancela cooperativamente os excedentes.
    /// </summary>
    /// <param name="ct">Token de cancelamento da parada.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o drain terminou.</returns>
    ValueTask StopAsync(CancellationToken ct);
}
