using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>
/// Implementação in-memory de <see cref="IStorage"/> — desenvolvimento, testes e demos.
/// <b>Não durável</b>: o estado se perde ao reiniciar o processo.
/// Aquisição atômica e lease/visibility idênticos aos providers persistentes.
/// </summary>
public sealed class MemoryStorage : IStorage
{
    private readonly MemoryJobStorage _jobs;

    /// <summary>Cria o storage in-memory.</summary>
    /// <param name="timeProvider">Relógio para lease/locks; <see cref="TimeProvider.System"/> quando omitido.</param>
    public MemoryStorage(TimeProvider? timeProvider = null)
    {
        var time = timeProvider ?? TimeProvider.System;
        _jobs = new MemoryJobStorage(time);
        Queues = new MemoryQueueStorage(_jobs);
        Locks = new MemoryLockProvider(time);
        Servers = new MemoryServerRegistry();
        Recurring = new MemoryRecurringStorage();
        Continuations = new MemoryContinuationStorage();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Transações: <c>false</c> — não há banco com que compartilhar a transação do chamador.
    /// Lock distribuído: <c>false</c> — locks valem apenas no processo local.
    /// </remarks>
    public StorageCapabilities Capabilities { get; } = new(
        SupportsTransactions: false,
        SupportsDistributedLock: false,
        SupportsServerSideTimers: false,
        SupportsServerSideFilter: true);

    /// <inheritdoc />
    public IJobStorage Jobs => _jobs;

    /// <inheritdoc />
    public IQueueStorage Queues { get; }

    /// <inheritdoc />
    public ILockProvider Locks { get; }

    /// <inheritdoc />
    public IServerRegistry Servers { get; }

    /// <inheritdoc />
    public IRecurringStorage Recurring { get; }

    /// <inheritdoc />
    public IContinuationStorage Continuations { get; }
}
