using System.Collections.Concurrent;
using Guara.Host.Models;

namespace Guara.Host.Repositories;

/// <summary>Acesso aos pedidos da aplicação de exemplo.</summary>
public interface IPedidoRepository
{
    /// <summary>Grava um pedido novo e devolve o id atribuído.</summary>
    /// <param name="emailCliente">E-mail de quem comprou.</param>
    /// <param name="total">Valor total.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O pedido gravado.</returns>
    ValueTask<Pedido> CriarAsync(string emailCliente, decimal total, CancellationToken ct);

    /// <summary>Busca um pedido pelo id.</summary>
    /// <param name="id">Id do pedido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O pedido, ou <c>null</c> se não existir.</returns>
    ValueTask<Pedido?> ObterAsync(int id, CancellationToken ct);

    /// <summary>Registra o desfecho da cobrança.</summary>
    /// <param name="id">Id do pedido.</param>
    /// <param name="situacao">Situação final.</param>
    /// <param name="ct">Token de cancelamento.</param>
    ValueTask AtualizarSituacaoAsync(int id, SituacaoPedido situacao, CancellationToken ct);

    /// <summary>Lista os pedidos, do mais recente para o mais antigo.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Os pedidos gravados.</returns>
    ValueTask<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct);
}

/// <summary>
/// Repositório em memória. O exemplo existe para demonstrar o Guará, não persistência:
/// num projeto real esta classe seria um <c>DbContext</c>, e a interface acima continuaria
/// igual — é ela que os serviços e os jobs enxergam.
/// </summary>
public sealed class PedidoRepository : IPedidoRepository
{
    private readonly ConcurrentDictionary<int, Pedido> _pedidos = new();
    private int _ultimoId;

    /// <inheritdoc />
    public ValueTask<Pedido> CriarAsync(string emailCliente, decimal total, CancellationToken ct)
    {
        var pedido = new Pedido
        {
            Id = Interlocked.Increment(ref _ultimoId),
            EmailCliente = emailCliente,
            Total = total,
            CriadoEm = DateTimeOffset.UtcNow,
        };

        _pedidos[pedido.Id] = pedido;
        return ValueTask.FromResult(pedido);
    }

    /// <inheritdoc />
    public ValueTask<Pedido?> ObterAsync(int id, CancellationToken ct)
        => ValueTask.FromResult(_pedidos.GetValueOrDefault(id));

    /// <inheritdoc />
    public ValueTask AtualizarSituacaoAsync(int id, SituacaoPedido situacao, CancellationToken ct)
    {
        // O job pode rodar depois de o pedido ter sido removido: atualizar o que não existe
        // mais é caso normal, não erro.
        _pedidos.TryGetValue(id, out var pedido);
        if (pedido is not null)
        {
            _pedidos[id] = pedido with { Situacao = situacao };
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct)
    {
        IReadOnlyList<Pedido> lista = [.. _pedidos.Values.OrderByDescending(p => p.Id)];
        return ValueTask.FromResult(lista);
    }
}
