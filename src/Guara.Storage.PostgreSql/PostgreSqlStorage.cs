using Guara.Storage;
using Npgsql;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Implementação PostgreSQL de <see cref="IStorage"/>. O esquema (isolado no schema
/// configurado) é garantido no primeiro uso quando <c>AutoMigrate</c> está ligado.
/// Locks valem <b>entre nós</b> (tabela com TTL e dono); a aquisição de jobs usa
/// <c>FOR UPDATE SKIP LOCKED</c>. Comparações temporais usam o relógio injetado do
/// nó chamador — semântica idêntica à dos demais providers (mesmo conformance kit).
/// </summary>
internal sealed class PostgreSqlStorage : IStorage, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Cria o storage e o pool de conexões a partir das opções.</summary>
    /// <param name="options">Opções validadas (connection string, schema, AutoMigrate).</param>
    /// <param name="timeProvider">Relógio para lease/TTL; <see cref="TimeProvider.System"/> quando omitido.</param>
    public PostgreSqlStorage(PostgreSqlStorageOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var time = timeProvider ?? TimeProvider.System;
        _dataSource = NpgsqlDataSource.Create(ComPreparacaoAutomatica(options.ConnectionString));
        var schema = new PostgreSqlSchemaInitializer(_dataSource, options);

        Jobs = new PostgreSqlJobStorage(_dataSource, schema, options.Schema, time);
        Queues = new PostgreSqlQueueStorage(_dataSource, schema, options.Schema);
        Locks = new PostgreSqlLockProvider(_dataSource, schema, options.Schema, time);
        Servers = new PostgreSqlServerRegistry(_dataSource, schema, options.Schema);
        Recurring = new PostgreSqlRecurringStorage(_dataSource, schema, options.Schema);
        Continuations = new PostgreSqlContinuationStorage(_dataSource, schema, options.Schema);
    }

    /// <summary>
    /// Liga a preparação automática de comandos, salvo se a connection string já disser
    /// o contrário.
    /// <para>
    /// O Guará repete um punhado pequeno de consultas — aquisição, transição de estado,
    /// renovação de posse — milhares de vezes por minuto. Sem preparação, o servidor
    /// analisa e planeja cada uma de novo a cada chamada, e o plano da aquisição sozinho
    /// custa alguns milissegundos de planejamento contra pouco mais de execução.
    /// </para>
    /// </summary>
    private static string ComPreparacaoAutomatica(string connectionString)
    {
        var construtor = new NpgsqlConnectionStringBuilder(connectionString);

        // Zero é o default do driver, e é o único valor que distingue "não configurado"
        // de escolha do usuário: quem quiser desligar de propósito pede outro caminho.
        if (construtor.MaxAutoPrepare == 0)
        {
            // Folga sobre o punhado de consultas distintas do provider, sem inflar o
            // cache de planos por conexão.
            construtor.MaxAutoPrepare = 32;
        }

        return construtor.ConnectionString;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Transações: <c>true</c> — o enfileiramento participa da transação do chamador,
    /// escrevendo na conexão dela. Lock distribuído: <c>true</c> (tabela com TTL e dono,
    /// válido entre nós). Push (LISTEN/NOTIFY): entra com a estratégia push do
    /// Dispatcher; até lá, <c>false</c>.
    /// </remarks>
    public StorageCapabilities Capabilities { get; } = new(
        SupportsTransactions: true,
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

    /// <summary>Libera o pool de conexões.</summary>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o pool foi liberado.</returns>
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
