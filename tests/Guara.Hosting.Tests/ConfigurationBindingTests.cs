using Guara.Core;
using Guara.Dispatcher;
using Guara.Hosting;
using Guara.Server;
using Guara.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Hosting.Tests;

public class ConfigurationBindingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddGuara()
            .UseConfiguration(configuration)
            .UseMemoryStorage()
            .AddGuaraServer();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Binding_ReadsEveryComponentSection()
    {
        using var provider = BuildProvider(Config(
            ("Guara:ApplicationName", "minha-app"),
            ("Guara:Worker:MaxConcurrency", "3"),
            ("Guara:Worker:ShutdownDrainTimeout", "00:00:10"),
            ("Guara:Dispatcher:PollingInterval", "00:00:01"),
            ("Guara:Dispatcher:Queues:0", "alta"),
            ("Guara:Dispatcher:Queues:1", "default"),
            ("Guara:Server:RecurringPollInterval", "00:00:02"),
            ("Guara:Server:Retention:Succeeded", "02:00:00"),
            ("Guara:Retry:MaxAttempts", "7")));

        Assert.Equal("minha-app", provider.GetRequiredService<GuaraOptions>().ApplicationName);
        var worker = provider.GetRequiredService<WorkerOptions>();
        Assert.Equal(3, worker.MaxConcurrency);
        Assert.Equal(TimeSpan.FromSeconds(10), worker.ShutdownDrainTimeout);
        var dispatcher = provider.GetRequiredService<DispatcherOptions>();
        Assert.Equal(TimeSpan.FromSeconds(1), dispatcher.PollingInterval);
        Assert.Equal(["alta", "default"], dispatcher.Queues);
        var server = provider.GetRequiredService<ServerOptions>();
        Assert.Equal(TimeSpan.FromSeconds(2), server.RecurringPollInterval);
        Assert.Equal(TimeSpan.FromHours(2), server.Retention.Succeeded);
        Assert.Equal(TimeSpan.FromDays(7), server.Retention.Failed); // não configurado → default
        Assert.Equal(7, provider.GetRequiredService<RetryOptions>().MaxAttempts);
    }

    [Fact]
    public void CodeDelegate_WinsOverConfiguration()
    {
        var services = new ServiceCollection();
        services.AddGuara()
            .UseConfiguration(Config(("Guara:Worker:MaxConcurrency", "3")))
            .UseMemoryStorage()
            .AddGuaraWorker(worker => worker.MaxConcurrency = 9)
            .AddGuaraServer();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(9, provider.GetRequiredService<WorkerOptions>().MaxConcurrency);
    }

    [Fact]
    public void MissingSections_KeepDefaults()
    {
        using var provider = BuildProvider(Config());

        Assert.Equal(Environment.ProcessorCount, provider.GetRequiredService<WorkerOptions>().MaxConcurrency);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<DispatcherOptions>().PollingInterval);
        Assert.Equal(3, provider.GetRequiredService<RetryOptions>().MaxAttempts);
    }

    [Fact]
    public void UnparseableValue_FailsAtStartup_WithFullPath()
    {
        using var provider = BuildProvider(Config(("Guara:Worker:MaxConcurrency", "muitos")));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<WorkerOptions>());
        Assert.Contains("Guara:Worker:MaxConcurrency", ex.Message);
        Assert.Contains("muitos", ex.Message);
    }

    [Fact]
    public void InvalidValue_FailsValidationAtStartup()
    {
        using var provider = BuildProvider(Config(("Guara:Worker:MaxConcurrency", "0")));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<WorkerOptions>());
        Assert.Contains("MaxConcurrency", ex.Message);
    }

    [Fact]
    public void WithoutUseConfiguration_DefaultsStillApply()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseMemoryStorage().AddGuaraServer();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(Environment.ProcessorCount, provider.GetRequiredService<WorkerOptions>().MaxConcurrency);
    }
}
