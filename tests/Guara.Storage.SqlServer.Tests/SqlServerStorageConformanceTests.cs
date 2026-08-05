using Guara.Storage.Conformance;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Guara.Storage.SqlServer.Tests;

/// <summary>O provider SQL Server passa 100% do kit de conformidade.</summary>
[Collection("sqlserver")]
public sealed class SqlServerStorageConformanceTests(SqlServerContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new SqlServerStorage(fixture.NewOptions(), timeProvider));

    /// <summary>
    /// Conexão própria, como a de um <c>DbContext</c> da aplicação: mesma base do
    /// provider, transação aberta e controlada por quem chama.
    /// </summary>
    protected override async ValueTask<ConformanceTransaction?> BeginCallerTransactionAsync(
        IStorage storage, CancellationToken ct)
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        return new RelationalConformanceTransaction(connection, await connection.BeginTransactionAsync(ct));
    }
}
