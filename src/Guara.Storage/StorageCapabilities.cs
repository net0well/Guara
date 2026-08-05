namespace Guara.Storage;

/// <summary>
/// Declara o que um provider de storage suporta de verdade. Consumidores (Dispatcher,
/// Dashboard, Cluster) consultam estas flags antes de usar recursos opcionais e degradam
/// de forma transparente — o provider declara honestamente o que suporta.
/// </summary>
/// <param name="SupportsTransactions">O provider aceita enfileirar dentro de uma transação aberta pelo chamador (<see cref="IJobStorage.CreateAsync(JobRecord, Guara.Abstractions.IGuaraTransaction, CancellationToken)"/>).</param>
/// <param name="SupportsDistributedLock">Os locks de <see cref="IStorage.Locks"/> valem entre processos/nós (não apenas no processo local).</param>
/// <param name="SupportsServerSideTimers">O backend notifica jobs vencidos sem polling (push).</param>
/// <param name="SupportsServerSideFilter">O backend filtra consultas ricas no servidor (busca do dashboard).</param>
public sealed record StorageCapabilities(
    bool SupportsTransactions,
    bool SupportsDistributedLock,
    bool SupportsServerSideTimers,
    bool SupportsServerSideFilter);
