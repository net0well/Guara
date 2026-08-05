using Guara.Storage.Conformance;
using Npgsql;
using Xunit;

namespace Guara.Storage.PostgreSql.Tests;

/// <summary>O provider PostgreSQL passa 100% do kit de conformidade.</summary>
[Collection("postgres")]
public sealed class PostgreSqlStorageConformanceTests(PostgresContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new PostgreSqlStorage(fixture.NewOptions(), timeProvider));

    /// <summary>
    /// Conexão própria, como a de um <c>DbContext</c> da aplicação: mesma base do
    /// provider, transação aberta e controlada por quem chama.
    /// </summary>
    protected override async ValueTask<ConformanceTransaction?> BeginCallerTransactionAsync(
        IStorage storage, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        return new RelationalConformanceTransaction(connection, await connection.BeginTransactionAsync(ct));
    }
}
