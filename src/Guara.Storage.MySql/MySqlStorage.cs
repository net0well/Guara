using Guara.Storage;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>
/// Implementação MySQL 8+ de <see cref="IStorage"/>. As tabelas do Guará convivem com as
/// da aplicação sob um prefixo, e o esquema é garantido no primeiro uso quando
/// <c>AutoMigrate</c> está ligado. Locks valem <b>entre nós</b> (tabela com TTL e dono);
/// a aquisição de jobs usa <c>FOR UPDATE SKIP LOCKED</c>. Comparações temporais usam o
/// relógio injetado do nó chamador — semântica idêntica à dos demais providers
/// (mesmo conformance kit).
/// </summary>
internal sealed class MySqlStorage : IStorage, IAsyncDisposable
{
    private readonly MySqlDataSource _dataSource;

    /// <summary>Cria o storage e o pool de conexões a partir das opções.</summary>
    /// <param name="options">Opções validadas (connection string, prefixo, AutoMigrate).</param>
    /// <param name="timeProvider">Relógio para lease/TTL; <see cref="TimeProvider.System"/> quando omitido.</param>
    public MySqlStorage(MySqlStorageOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var time = timeProvider ?? TimeProvider.System;
        _dataSource = new MySqlDataSourceBuilder(options.ConnectionString).Build();
        var schema = new MySqlSchemaInitializer(_dataSource, options);

        Jobs = new MySqlJobStorage(_dataSource, schema, options.TablePrefix, time);
        Queues = new MySqlQueueStorage(_dataSource, schema, options.TablePrefix);
        Locks = new MySqlLockProvider(_dataSource, schema, options.TablePrefix, time);
        Servers = new MySqlServerRegistry(_dataSource, schema, options.TablePrefix);
        Recurring = new MySqlRecurringStorage(_dataSource, schema, options.TablePrefix);
        Continuations = new MySqlContinuationStorage(_dataSource, schema, options.TablePrefix);
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
            "MySqlStorage ainda não expõe transações (Capabilities.SupportsTransactions = false); " +
            "as operações são individualmente atômicas.");

    /// <summary>Libera o pool de conexões.</summary>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o pool foi liberado.</returns>
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
