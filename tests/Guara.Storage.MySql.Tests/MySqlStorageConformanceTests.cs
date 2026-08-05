using Guara.Storage.Conformance;
using MySqlConnector;
using Xunit;

namespace Guara.Storage.MySql.Tests;

/// <summary>O provider MySQL passa 100% do kit de conformidade.</summary>
[Collection("mysql")]
public sealed class MySqlStorageConformanceTests(MySqlContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new MySqlStorage(fixture.NewOptions(), timeProvider));

    /// <summary>
    /// Conexão própria, como a de um <c>DbContext</c> da aplicação: mesma base do
    /// provider, transação aberta e controlada por quem chama.
    /// </summary>
    protected override async ValueTask<ConformanceTransaction?> BeginCallerTransactionAsync(
        IStorage storage, CancellationToken ct)
    {
        var connection = new MySqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        return new RelationalConformanceTransaction(connection, await connection.BeginTransactionAsync(ct));
    }
}
