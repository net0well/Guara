using Testcontainers.PostgreSql;
using Xunit;

namespace Guara.Storage.PostgreSql.Tests;

/// <summary>
/// Um único container PostgreSQL para toda a coleção; o isolamento entre testes vem
/// de um schema exclusivo por storage criado.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gera opções apontando para um schema exclusivo (isolamento por teste).</summary>
    public PostgreSqlStorageOptions NewOptions() => new()
    {
        ConnectionString = ConnectionString,
        Schema = $"g{Guid.NewGuid():n}"[..16],
    };

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
