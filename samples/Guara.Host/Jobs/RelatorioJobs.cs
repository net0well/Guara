using Guara.Abstractions;
using Guara.Host.Repositories;

namespace Guara.Host.Jobs;

/// <summary>
/// Relatórios do exemplo. Ficam numa fila própria para não disputar worker com o envio de
/// e-mail: relatório é lento e tolera espera, confirmação de compra não.
/// </summary>
public sealed class RelatorioJobs(IPedidoRepository pedidos, ILogger<RelatorioJobs> logger)
{
    /// <summary>Consolida os pedidos do dia.</summary>
    /// <param name="ct">Token de cancelamento fornecido pelo Guará.</param>
    [GuaraJob]
    [GuaraFila("relatorios")]
    [GuaraTempoLimite(30)]
    public async Task ConsolidarDiarioAsync(CancellationToken ct)
    {
        var todos = await pedidos.ListarAsync(ct);
        var faturado = todos.Sum(p => p.Total);

        // O token é propagado ao trabalho lento: se o tempo limite estourar, o job para aqui
        // em vez de seguir consumindo worker.
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);

        logger.LogInformation(
            "Relatório diário: {Quantidade} pedidos, {Faturado:C} faturados", todos.Count, faturado);
    }

    /// <summary>
    /// Exporta os pedidos de um cliente. Marcado para não rodar concorrente consigo mesmo:
    /// duas exportações do mesmo cliente ao mesmo tempo escreveriam no mesmo arquivo.
    /// </summary>
    /// <param name="emailCliente">Cliente a exportar.</param>
    /// <param name="ct">Token de cancelamento fornecido pelo Guará.</param>
    [GuaraJob]
    [GuaraFila("relatorios")]
    [GuaraDesabilitarConcorrencia]
    public async Task ExportarDoClienteAsync(string emailCliente, CancellationToken ct)
    {
        var todos = await pedidos.ListarAsync(ct);
        var doCliente = todos.Count(p => string.Equals(p.EmailCliente, emailCliente, StringComparison.OrdinalIgnoreCase));

        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        logger.LogInformation("Exportação de {Email}: {Quantidade} pedidos", emailCliente, doCliente);
    }
}
