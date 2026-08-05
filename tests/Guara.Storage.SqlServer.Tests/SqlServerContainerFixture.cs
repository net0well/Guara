using Testcontainers.MsSql;
using Xunit;

namespace Guara.Storage.SqlServer.Tests;

/// <summary>
/// Um único container SQL Server para toda a coleção; o isolamento entre testes vem
/// de um schema exclusivo por storage criado.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gera opções apontando para um schema exclusivo (isolamento por teste).</summary>
    public SqlServerStorageOptions NewOptions() => new()
    {
        ConnectionString = ConnectionString,
        Schema = $"g{Guid.NewGuid():n}"[..16],
    };

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>;
