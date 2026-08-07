using System.Text.Json.Serialization;

namespace Guara.Host.Models;

/// <summary>
/// Situação de um pedido ao longo do fluxo de exemplo. Serializa pelo nome, e não pelo
/// número: o valor numérico mudaria em silêncio se alguém inserisse um membro no meio.
/// O conversor genérico é resolvido em compilação — a versão não genérica usa reflection.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SituacaoPedido>))]
public enum SituacaoPedido
{
    /// <summary>Criado, aguardando cobrança.</summary>
    Recebido,

    /// <summary>Cobrança aprovada.</summary>
    Pago,

    /// <summary>Cobrança recusada em definitivo.</summary>
    Recusado,
}

/// <summary>Pedido do catálogo de exemplo.</summary>
public sealed record Pedido
{
    /// <summary>Identificador do pedido.</summary>
    public required int Id { get; init; }

    /// <summary>E-mail de quem comprou; destino da confirmação.</summary>
    public required string EmailCliente { get; init; }

    /// <summary>Valor total em reais.</summary>
    public required decimal Total { get; init; }

    /// <summary>Instante de criação (UTC).</summary>
    public required DateTimeOffset CriadoEm { get; init; }

    /// <summary>Situação atual.</summary>
    public SituacaoPedido Situacao { get; init; } = SituacaoPedido.Recebido;
}
