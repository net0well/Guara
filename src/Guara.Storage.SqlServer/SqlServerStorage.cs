using Guara.Storage;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Implementação SQL Server de <see cref="IStorage"/>. O esquema (isolado no schema
/// configurado) é garantido no primeiro uso quando <c>AutoMigrate</c> está ligado.
/// Locks valem <b>entre nós</b> (tabela com TTL e dono); a aquisição de jobs usa
/// <c>READPAST + UPDLOCK</c>. Comparações temporais usam o relógio injetado do nó
/// chamador — semântica idêntica à dos demais providers (mesmo conformance kit).
/// </summary>
internal sealed class SqlServerStorage : IStorage
{
    /// <summary>Cria o storage a partir das opções.</summary>
    /// <param name="options">Opções validadas (connection string, schema, AutoMigrate).</param>
    /// <param name="timeProvider">Relógio para lease/TTL; <see cref="TimeProvider.System"/> quando omitido.</param>
    public SqlServerStorage(SqlServerStorageOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var time = timeProvider ?? TimeProvider.System;
        var connections = new SqlServerConnections(options.ConnectionString);
        var schema = new SqlServerSchemaInitializer(options.ConnectionString, options);

        Jobs = new SqlServerJobStorage(connections, schema, options.Schema, time);
        Queues = new SqlServerQueueStorage(connections, schema, options.Schema);
        Locks = new SqlServerLockProvider(connections, schema, options.Schema, time);
        Servers = new SqlServerServerRegistry(connections, schema, options.Schema);
        Recurring = new SqlServerRecurringStorage(connections, schema, options.Schema);
        Continuations = new SqlServerContinuationStorage(connections, schema, options.Schema);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Transações: <c>false</c> por ora — as operações são individualmente atômicas
    /// (capabilities honestas). Lock distribuído: <c>true</c> (tabela com TTL e dono,
    /// válido entre nós). Timers no servidor: <c>false</c> — quem acorda os jobs é o
    /// Dispatcher, não o banco.
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
            "SqlServerStorage ainda não expõe transações (Capabilities.SupportsTransactions = false); " +
            "as operações são individualmente atômicas.");
}
