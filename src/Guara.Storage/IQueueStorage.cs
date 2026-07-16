namespace Guara.Storage;

/// <summary>
/// Introspecção de filas (dashboard/métricas). Enfileirar é criar um <see cref="JobRecord"/>
/// com estado <c>Enqueued</c> via <see cref="IJobStorage.CreateAsync"/>; desenfileirar é
/// <see cref="IJobStorage.AcquireNextDueAsync"/> — este contrato não duplica essas operações.
/// </summary>
public interface IQueueStorage
{
    /// <summary>Lista as filas conhecidas pelo storage.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Nomes das filas.</returns>
    ValueTask<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct);

    /// <summary>Quantidade de jobs aguardando (<c>Enqueued</c>) numa fila.</summary>
    /// <param name="queue">Nome da fila.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O tamanho atual da fila.</returns>
    ValueTask<long> GetLengthAsync(string queue, CancellationToken ct);
}
