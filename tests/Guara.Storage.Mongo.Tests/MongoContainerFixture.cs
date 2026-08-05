using Testcontainers.MongoDb;
using Xunit;

namespace Guara.Storage.Mongo.Tests;

/// <summary>
/// Um único container MongoDB para toda a coleção; o isolamento entre testes vem de um
/// prefixo de coleção exclusivo por storage.
/// </summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gera opções apontando para um prefixo exclusivo (isolamento por teste).</summary>
    public MongoStorageOptions NewOptions() => new()
    {
        ConnectionString = ConnectionString,
        Database = "guara_testes",
        CollectionPrefix = $"g{Guid.NewGuid():n}"[..16] + "_",
    };

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition("mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoContainerFixture>;
