using Testcontainers.Redis;
using Xunit;

namespace Guara.Redis.Tests;

/// <summary>
/// Um único container Redis para toda a coleção; o isolamento entre testes vem de um
/// prefixo de canal exclusivo por sinal.
/// </summary>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.4-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gera opções apontando para um prefixo exclusivo (isolamento por teste).</summary>
    public RedisOptions NewOptions() => new()
    {
        ConnectionString = ConnectionString,
        ChannelPrefix = $"t{Guid.NewGuid():n}"[..16],
    };

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition("redis")]
public sealed class RedisCollection : ICollectionFixture<RedisContainerFixture>;
