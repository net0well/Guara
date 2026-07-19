using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>Situação de uma continuação registrada.</summary>
public enum ContinuationStatus
{
    /// <summary>Aguardando o estado final do pai.</summary>
    Pending,

    /// <summary>Gatilho atendido: o filho foi enfileirado.</summary>
    Enqueued,

    /// <summary>Nunca vai disparar (pai falhou com gatilho de sucesso, foi excluído ou sumiu).</summary>
    Discarded,
}

/// <summary>
/// Vínculo persistido pai→filho de uma continuação. O filho existe como job comum
/// aguardando (<c>Scheduled</c> sem <c>ScheduledFor</c>) até a resolução; a chave do
/// vínculo é o próprio filho — cada filho tem exatamente um pai.
/// </summary>
public sealed record ContinuationRecord
{
    /// <summary>Job filho que aguarda o gatilho (chave do vínculo).</summary>
    public required JobId ChildId { get; init; }

    /// <summary>Job pai observado.</summary>
    public required JobId ParentId { get; init; }

    /// <summary>Estado final do pai que dispara o filho.</summary>
    public ContinuationTrigger Trigger { get; init; } = ContinuationTrigger.OnSucceeded;

    /// <summary>Situação atual do vínculo.</summary>
    public ContinuationStatus Status { get; init; } = ContinuationStatus.Pending;

    /// <summary>Motivo do descarte, quando <see cref="Status"/> é <see cref="ContinuationStatus.Discarded"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>Posição do filho na cadeia de continuações (0 = filho de um job raiz).</summary>
    public int Depth { get; init; }

    /// <summary>Instante do registro (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Instante da resolução (disparo ou descarte), quando já resolvida.</summary>
    public DateTimeOffset? ResolvedAt { get; init; }
}
