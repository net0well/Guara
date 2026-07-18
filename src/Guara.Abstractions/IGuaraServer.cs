namespace Guara.Abstractions;

/// <summary>
/// O processo servidor do Guará: inicia e para os motores em ordem, mantém o
/// heartbeat do nó e executa a manutenção periódica (purga por retenção,
/// limpeza de nós mortos).
/// </summary>
public interface IGuaraServer
{
    /// <summary>Anuncia o nó e inicia os motores e os laços de fundo.</summary>
    /// <param name="ct">Token de cancelamento da inicialização.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o servidor está operante.</returns>
    ValueTask StartAsync(CancellationToken ct);

    /// <summary>
    /// Para em ordem: deixa de buscar jobs, drena os em execução, encerra os laços
    /// de fundo e remove o registro do nó.
    /// </summary>
    /// <param name="ct">Token de cancelamento da parada.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o servidor parou.</returns>
    ValueTask StopAsync(CancellationToken ct);
}
