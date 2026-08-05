namespace Guara.Storage;

/// <summary>
/// Fachada do storage do Guará — "o storage é a fila". Providers
/// (<c>Guara.Storage.*</c>) implementam esta família de contratos; os motores só
/// conhecem as interfaces. Consulte <see cref="Capabilities"/> antes de usar
/// recursos opcionais (enfileiramento transacional, locks distribuídos, push).
/// </summary>
public interface IStorage
{
    /// <summary>O que este provider suporta de verdade.</summary>
    StorageCapabilities Capabilities { get; }

    /// <summary>Persistência e aquisição de jobs.</summary>
    IJobStorage Jobs { get; }

    /// <summary>Introspecção de filas.</summary>
    IQueueStorage Queues { get; }

    /// <summary>Locks com TTL (distribuídos quando <see cref="StorageCapabilities.SupportsDistributedLock"/>).</summary>
    ILockProvider Locks { get; }

    /// <summary>Registro de nós servidores (heartbeat/descoberta).</summary>
    IServerRegistry Servers { get; }

    /// <summary>Definições recorrentes e calendários.</summary>
    IRecurringStorage Recurring { get; }

    /// <summary>Vínculos de continuação (pai→filho).</summary>
    IContinuationStorage Continuations { get; }
}
