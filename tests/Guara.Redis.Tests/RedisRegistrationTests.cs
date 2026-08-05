using Guara.Abstractions;
using Guara.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Redis.Tests;

/// <summary>
/// Escolher o Redis é substituir o sinal em processo que o <c>AddGuara()</c> registra —
/// não somar um segundo. Nada aqui toca o Redis: a conexão só sobe no primeiro uso.
/// </summary>
public class RedisRegistrationTests
{
    [Fact]
    public void UseRedis_ReplacesTheInProcessSignal()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseRedis("localhost:6379");

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RedisQueueSignal>(provider.GetRequiredService<IQueueSignal>());
        Assert.Single(provider.GetServices<IQueueSignal>());
    }

    [Fact]
    public void AddGuara_AloneKeepsTheInProcessSignal()
    {
        var services = new ServiceCollection();
        services.AddGuara();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InProcessQueueSignal>(provider.GetRequiredService<IQueueSignal>());
    }

    [Fact]
    public void UseRedis_WithoutConnectionStringNorMultiplexer_FailsWithAnActionableMessage()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseRedis();

        using var provider = services.BuildServiceProvider();

        var erro = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IQueueSignal>());
        Assert.Contains("RedisOptions.ConnectionString", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseRedis_RejectsAChannelPrefixThatCouldMatchOtherChannels()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseRedis("localhost:6379", options => options.ChannelPrefix = "gua*ra");

        using var provider = services.BuildServiceProvider();

        var erro = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IQueueSignal>());
        Assert.Contains("ChannelPrefix", erro.Message, StringComparison.Ordinal);
    }
}
