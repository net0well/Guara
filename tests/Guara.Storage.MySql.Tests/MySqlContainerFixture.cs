using Testcontainers.MySql;
using Xunit;

namespace Guara.Storage.MySql.Tests;

/// <summary>
/// Um único container MySQL para toda a coleção; o isolamento entre testes vem de um
/// prefixo de tabela exclusivo por storage.
/// </summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gera opções apontando para um prefixo exclusivo (isolamento por teste).</summary>
    public MySqlStorageOptions NewOptions() => new()
    {
        ConnectionString = ConnectionString,
        TablePrefix = $"g{Guid.NewGuid():n}"[..16] + "_",
    };

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition("mysql")]
public sealed class MySqlCollection : ICollectionFixture<MySqlContainerFixture>;
