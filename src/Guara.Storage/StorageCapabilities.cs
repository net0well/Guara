namespace Guara.Storage;

/// <summary>
/// Declara o que um provider de storage suporta de verdade. Consumidores (Dispatcher,
/// Dashboard, Cluster) consultam estas flags antes de usar recursos opcionais e degradam
/// de forma transparente — "capabilities honestas" (spec 004).
/// </summary>
/// <param name="SupportsTransactions">O provider oferece <see cref="IStorage.BeginTransactionAsync"/>.</param>
/// <param name="SupportsDistributedLock">Os locks de <see cref="IStorage.Locks"/> valem entre processos/nós (não apenas no processo local).</param>
/// <param name="SupportsServerSideTimers">O backend notifica jobs vencidos sem polling (push).</param>
/// <param name="SupportsServerSideFilter">O backend filtra consultas ricas no servidor (busca do dashboard).</param>
public sealed record StorageCapabilities(
    bool SupportsTransactions,
    bool SupportsDistributedLock,
    bool SupportsServerSideTimers,
    bool SupportsServerSideFilter);
