using Guara.Abstractions;
using Guara.Host.Jobs;
using Guara.Host.Models;
using Guara.Host.Repositories;

namespace Guara.Host.Services;

/// <summary>
/// Regra de negócio dos pedidos. É aqui que a resposta ao usuário se separa do trabalho de
/// fundo: grava o pedido, enfileira o que pode esperar, e devolve.
/// </summary>
public sealed class PedidoService(
    IPedidoRepository pedidos,
    IGuaraClient jobs,
    ILogger<PedidoService> logger)
{
    /// <summary>
    /// Registra a compra e agenda o que não precisa acontecer antes da resposta.
    /// </summary>
    /// <param name="emailCliente">E-mail de quem comprou.</param>
    /// <param name="total">Valor total.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O pedido criado.</returns>
    public async Task<Pedido> RegistrarAsync(string emailCliente, decimal total, CancellationToken ct)
    {
        var pedido = await pedidos.CriarAsync(emailCliente, total, ct);

        // A confirmação sai por job: mandar e-mail no caminho da requisição faria o cliente
        // esperar pelo servidor de e-mail, e uma falha dele derrubaria a compra.
        await jobs.EnfileirarAsync(PedidoJobsGuara.EnviarConfirmacaoAsync(pedido.Id), ct);

        // A cobrança também. O aviso de recusa é encadeado: só existe quando a cobrança
        // termina, e a continuação dispensa este código de saber quando isso será.
        var cobranca = await jobs.EnfileirarAsync(PedidoJobsGuara.CobrarAsync(pedido.Id), ct);
        await jobs.ContinuarComAsync(
            cobranca, PedidoJobsGuara.AvisarCobrancaRecusadaAsync(pedido.Id), ct: ct);

        logger.LogInformation("Pedido {PedidoId} registrado para {Email}", pedido.Id, emailCliente);
        return pedido;
    }

    /// <summary>Pede a exportação dos pedidos de um cliente.</summary>
    /// <param name="emailCliente">Cliente a exportar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O id do job, para acompanhar no painel.</returns>
    public async Task<JobId> SolicitarExportacaoAsync(string emailCliente, CancellationToken ct)
        => await jobs.EnfileirarAsync(RelatorioJobsGuara.ExportarDoClienteAsync(emailCliente), ct);

    /// <summary>Busca um pedido.</summary>
    /// <param name="id">Id do pedido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O pedido, ou <c>null</c> se não existir.</returns>
    public ValueTask<Pedido?> ObterAsync(int id, CancellationToken ct) => pedidos.ObterAsync(id, ct);

    /// <summary>Lista os pedidos.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Os pedidos gravados.</returns>
    public ValueTask<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct) => pedidos.ListarAsync(ct);
}
