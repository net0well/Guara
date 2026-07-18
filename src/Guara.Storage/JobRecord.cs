using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Um job persistido no storage. Imutável — atualizações produzem um novo registro
/// (<c>with</c>); a fonte da verdade do estado é sempre o storage.
/// </summary>
public sealed record JobRecord
{
    /// <summary>Identificador do job.</summary>
    public required JobId Id { get; init; }

    /// <summary>Descrição do que executar (argumentos já serializados).</summary>
    public required JobDescriptor Descriptor { get; init; }

    /// <summary>Estado atual.</summary>
    public JobState State { get; init; } = JobState.Created;

    /// <summary>Número da tentativa atual (0 = primeira).</summary>
    public int Attempt { get; init; }

    /// <summary>Fila em que o job aguarda/roda.</summary>
    public string Queue { get; init; } = "default";

    /// <summary>Instante de criação (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Quando o job fica elegível, para jobs agendados (delay/cron).</summary>
    public DateTimeOffset? ScheduledFor { get; init; }

    /// <summary>
    /// Posse do job por um worker até este instante (visibility timeout).
    /// Lease expirado torna o job novamente elegível — cobre crash de worker.
    /// </summary>
    public DateTimeOffset? LeaseUntil { get; init; }

    /// <summary>Resultado serializado, quando <see cref="State"/> é <see cref="JobState.Succeeded"/>.</summary>
    public string? Result { get; init; }

    /// <summary>Motivo da falha, quando <see cref="State"/> é <see cref="JobState.Failed"/>/<see cref="JobState.Retrying"/>.</summary>
    public string? Error { get; init; }
}
