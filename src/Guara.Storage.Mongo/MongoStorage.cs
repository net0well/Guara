using Guara.Storage;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>
/// Implementação MongoDB de <see cref="IStorage"/>. As coleções do Guará convivem com as
/// da aplicação sob um prefixo, e os índices são criados no primeiro uso quando
/// <c>AutoMigrate</c> está ligado. Locks valem <b>entre nós</b> (documento com validade e
/// dono); a aquisição de jobs é um <c>findAndModify</c> atômico. Comparações temporais
/// usam o relógio injetado do nó chamador — semântica idêntica à dos demais providers
/// (mesmo conformance kit).
/// </summary>
internal sealed class MongoStorage : IStorage
{
    /// <summary>Cria o storage e o cliente a partir das opções.</summary>
    /// <param name="options">Opções validadas (connection string, banco, prefixo, AutoMigrate).</param>
    /// <param name="timeProvider">Relógio para lease/TTL; <see cref="TimeProvider.System"/> quando omitido.</param>
    public MongoStorage(MongoStorageOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var time = timeProvider ?? TimeProvider.System;
        var url = MongoUrl.Create(options.ConnectionString);
        var banco = options.Database is { Length: > 0 } escolhido ? escolhido : url.DatabaseName;
        if (string.IsNullOrWhiteSpace(banco))
        {
            throw new InvalidOperationException(
                "MongoStorageOptions não sabe em que banco gravar: a connection string não declara um e " +
                "MongoStorageOptions.Database está vazio.");
        }

        var collections = new MongoCollections(new MongoClient(url).GetDatabase(banco), options);

        Jobs = new MongoJobStorage(collections, time);
        Queues = new MongoQueueStorage(collections);
        Locks = new MongoLockProvider(collections, time);
        Servers = new MongoServerRegistry(collections);
        Recurring = new MongoRecurringStorage(collections);
        Continuations = new MongoContinuationStorage(collections);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Transações: <c>false</c> — exigiriam replica set, e as operações já são
    /// individualmente atômicas (capabilities honestas). Lock distribuído: <c>true</c>
    /// (documento com validade e dono, válido entre nós). Timers no servidor: <c>false</c>
    /// — quem acorda os jobs é o Dispatcher, não o banco.
    /// </remarks>
    public StorageCapabilities Capabilities { get; } = new(
        SupportsTransactions: false,
        SupportsDistributedLock: true,
        SupportsServerSideTimers: false,
        SupportsServerSideFilter: true);

    /// <inheritdoc />
    public IJobStorage Jobs { get; }

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

    /// <inheritdoc />
    public ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct)
        => throw new NotSupportedException(
            "MongoStorage ainda não expõe transações (Capabilities.SupportsTransactions = false); " +
            "as operações são individualmente atômicas.");
}
