namespace Guara.Storage;

/// <summary>
/// Fachada do storage do Guará — "o storage é a fila". Providers
/// (<c>Guara.Storage.*</c>) implementam esta família de contratos; os motores só
/// conhecem as interfaces. Consulte <see cref="Capabilities"/> antes de usar
/// recursos opcionais (transações, locks distribuídos, push).
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

    /// <summary>
    /// Inicia uma transação, quando <see cref="StorageCapabilities.SupportsTransactions"/>.
    /// </summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A transação iniciada.</returns>
    /// <exception cref="NotSupportedException">Quando o provider não suporta transações.</exception>
    ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct);
}

/// <summary>Unidade de trabalho transacional de um provider.</summary>
public interface ITransaction : IAsyncDisposable
{
    /// <summary>Confirma a transação.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a transação é confirmada.</returns>
    ValueTask CommitAsync(CancellationToken ct);

    /// <summary>Desfaz a transação.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a transação é desfeita.</returns>
    ValueTask RollbackAsync(CancellationToken ct);
}
