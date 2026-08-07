using Guara.Abstractions;
using Guara.Host.Models;
using Guara.Host.Repositories;

namespace Guara.Host.Jobs;

/// <summary>
/// Trabalho de fundo do fluxo de pedidos. Cada método marcado com <c>[GuaraJob]</c> ganha
/// uma fábrica de descritor gerada em compilação — <c>PedidoJobsGuara.{Método}</c> —, com
/// a fila e os argumentos já resolvidos.
/// <para>
/// A classe recebe as mesmas dependências que qualquer serviço: o job roda dentro de um
/// escopo de DI, então o repositório aqui é o mesmo que o controller usa.
/// </para>
/// </summary>
public sealed class PedidoJobs(IPedidoRepository pedidos, ILogger<PedidoJobs> logger)
{
    /// <summary>Envia a confirmação da compra.</summary>
    /// <param name="pedidoId">Pedido confirmado.</param>
    /// <param name="ct">Token de cancelamento fornecido pelo Guará.</param>
    [GuaraJob]
    [GuaraFila("emails")]
    public async Task EnviarConfirmacaoAsync(int pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(pedidoId, ct);
        if (pedido is null)
        {
            // O pedido sumiu entre o enfileiramento e a execução. Não é falha: repetir não
            // resolveria, então o job termina com sucesso em vez de gastar retentativas.
            logger.LogWarning("Pedido {PedidoId} não existe mais; confirmação descartada", pedidoId);
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        logger.LogInformation(
            "Confirmação do pedido {PedidoId} enviada para {Email}", pedido.Id, pedido.EmailCliente);
    }

    /// <summary>
    /// Cobra o pedido no gateway. Marcado com retentativas porque a falha esperada aqui é
    /// transitória — gateway fora do ar tende a voltar.
    /// </summary>
    /// <param name="pedidoId">Pedido a cobrar.</param>
    /// <param name="ct">Token de cancelamento fornecido pelo Guará.</param>
    [GuaraJob]
    [GuaraRetentativas(3)]
    public async Task CobrarAsync(int pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(pedidoId, ct);
        if (pedido is null)
        {
            logger.LogWarning("Pedido {PedidoId} não existe mais; cobrança descartada", pedidoId);
            return;
        }

        // Instabilidade simulada: metade das tentativas falha, para o painel mostrar o
        // ciclo Retrying → Succeeded/Failed com dados reais.
        if (Random.Shared.Next(2) == 0)
        {
            throw new InvalidOperationException(
                $"Gateway indisponível ao cobrar o pedido {pedido.Id}");
        }

        await pedidos.AtualizarSituacaoAsync(pedido.Id, SituacaoPedido.Pago, ct);
        logger.LogInformation("Pedido {PedidoId} pago ({Total:C})", pedido.Id, pedido.Total);
    }

    /// <summary>
    /// Avisa o cliente de que a cobrança não passou. Roda como continuação da cobrança,
    /// então não repete: se o aviso falhar, repetir mandaria e-mail duplicado.
    /// </summary>
    /// <param name="pedidoId">Pedido recusado.</param>
    /// <param name="ct">Token de cancelamento fornecido pelo Guará.</param>
    [GuaraJob]
    [GuaraFila("emails")]
    [GuaraRetentativas(0)]
    public async Task AvisarCobrancaRecusadaAsync(int pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(pedidoId, ct);
        if (pedido is null || pedido.Situacao == SituacaoPedido.Pago)
        {
            return; // pago na retentativa: não há o que avisar
        }

        await pedidos.AtualizarSituacaoAsync(pedido.Id, SituacaoPedido.Recusado, ct);
        logger.LogInformation(
            "Aviso de cobrança recusada do pedido {PedidoId} enviado para {Email}",
            pedido.Id, pedido.EmailCliente);
    }
}
