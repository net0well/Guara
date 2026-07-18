using Guara.Abstractions;
using Guara.Executor;
using Guara.Server;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Guara.Hosting.Tests;

public class GuaraServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildHost(Action<ServerOptions>? configureServer = null)
    {
        var services = new ServiceCollection();
        services.AddGuara(options => options.ApplicationName = "teste")
            .UseMemoryStorage()
            .AddGuaraDispatcher(d => d.PollingInterval = TimeSpan.FromMilliseconds(50))
            .AddGuaraWorker(w => w.MaxConcurrency = 2)
            .AddGuaraExecutor(retry => retry.Backoff = static _ => TimeSpan.Zero)
            .AddGuaraServer(configureServer);
        return services.BuildServiceProvider();
    }

    private static IHostedService HostedService(ServiceProvider provider)
        => Assert.Single(provider.GetServices<IHostedService>());

    private static async Task<JobRecord?> WaitUntilAsync(
        IStorage storage, JobId id, Func<JobRecord?, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        JobRecord? job = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            job = await storage.Jobs.GetAsync(id, Ct);
            if (condition(job))
            {
                return job;
            }

            await Task.Delay(25, Ct);
        }

        return job;
    }

    [Fact]
    public async Task Start_WithoutStorage_FailsWithActionableMessage()
    {
        var services = new ServiceCollection();
        services.AddGuara().AddGuaraServer();
        await using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => HostedService(provider).StartAsync(Ct));
        Assert.Contains("UseMemoryStorage", ex.Message);
    }

    [Fact]
    public async Task FullBoot_AnnouncesNode_ProcessesJob_AndUnregistersOnStop()
    {
        await using var provider = BuildHost();
        var processed = 0;
        provider.GetRequiredService<JobHandlerRegistry>().Register("Demo", "Executar", (_, _) =>
        {
            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        });

        var hosted = HostedService(provider);
        var storage = provider.GetRequiredService<IStorage>();

        await hosted.StartAsync(Ct);
        try
        {
            var node = Assert.Single(await storage.Servers.ListAsync(Ct));
            Assert.Equal(Environment.MachineName, node.MachineName);
            Assert.Equal(2, node.MaxConcurrency);

            var id = await provider.GetRequiredService<IGuaraClient>()
                .EnfileirarAsync(new JobDescriptor("Demo", "Executar", default), Ct);
            var job = await WaitUntilAsync(storage, id, j => j?.State == JobState.Succeeded, TimeSpan.FromSeconds(10));
            Assert.Equal(JobState.Succeeded, job?.State);
            Assert.Equal(1, processed);
        }
        finally
        {
            await hosted.StopAsync(Ct);
        }

        Assert.Empty(await storage.Servers.ListAsync(Ct));
    }

    [Fact]
    public async Task Heartbeat_ReannouncesWhenRegistrationDisappears()
    {
        await using var provider = BuildHost(server => server.HeartbeatInterval = TimeSpan.FromMilliseconds(50));
        var hosted = HostedService(provider);
        var storage = provider.GetRequiredService<IStorage>();

        await hosted.StartAsync(Ct);
        try
        {
            var node = Assert.Single(await storage.Servers.ListAsync(Ct));
            await storage.Servers.RemoveAsync(node.Id, Ct);

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            var reannounced = false;
            while (DateTimeOffset.UtcNow < deadline && !reannounced)
            {
                reannounced = (await storage.Servers.ListAsync(Ct)).Count == 1;
                await Task.Delay(25, Ct);
            }

            Assert.True(reannounced, "O servidor deveria reanunciar-se após o registro sumir.");
        }
        finally
        {
            await hosted.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Recurring_PromotesAndExecutesRepeatedOccurrences()
    {
        await using var provider = BuildHost(server => server.RecurringPollInterval = TimeSpan.FromMilliseconds(50));
        var processed = 0;
        provider.GetRequiredService<JobHandlerRegistry>().Register("Demo", "Tick", (_, _) =>
        {
            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        });

        var hosted = HostedService(provider);
        var storage = provider.GetRequiredService<IStorage>();
        await provider.GetRequiredService<IGuaraClient>().AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("tick")
                .Executa(new JobDescriptor("Demo", "Tick", default))
                .ACada(TimeSpan.FromMilliseconds(100)),
            Ct);

        await hosted.StartAsync(Ct);
        try
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref processed) < 3)
            {
                await Task.Delay(25, Ct);
            }

            Assert.True(processed >= 3, $"O recorrente deveria ter executado ao menos 3 vezes (executou {processed}).");

            var definition = await storage.Recurring.GetAsync("tick", Ct);
            Assert.NotNull(definition);
            Assert.NotNull(definition.LastRunAt);
            Assert.NotNull(definition.LastRunJobId);
            Assert.NotNull(definition.NextRunAt);
        }
        finally
        {
            await hosted.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Recurring_SkipIfPreviousRunning_RecordsSkip()
    {
        await using var provider = BuildHost(server => server.RecurringPollInterval = TimeSpan.FromMilliseconds(50));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetRequiredService<JobHandlerRegistry>().Register("Demo", "Lento", async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
        });

        var hosted = HostedService(provider);
        var storage = provider.GetRequiredService<IStorage>();
        await provider.GetRequiredService<IGuaraClient>().AdicionarOuAtualizarRecorrenteAsync(
            job => job.ComId("lento")
                .Executa(new JobDescriptor("Demo", "Lento", default))
                .ACada(TimeSpan.FromMilliseconds(100))
                .PularSeAnteriorEmExecucao(),
            Ct);

        await hosted.StartAsync(Ct);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

            // Com a primeira ocorrência presa no handler, o próximo ciclo deve pular e registrar.
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            RecurringJobRecord? definition = null;
            while (DateTimeOffset.UtcNow < deadline && definition?.LastSkippedAt is null)
            {
                definition = await storage.Recurring.GetAsync("lento", Ct);
                await Task.Delay(25, Ct);
            }

            Assert.NotNull(definition?.LastSkippedAt);
        }
        finally
        {
            release.TrySetResult();
            await hosted.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Maintenance_PurgesSucceededJobsPastRetention()
    {
        await using var provider = BuildHost(server =>
        {
            server.MaintenanceInterval = TimeSpan.FromMilliseconds(75);
            server.Retention = new RetentionPolicy(Succeeded: TimeSpan.Zero, Failed: TimeSpan.FromDays(7));
        });
        provider.GetRequiredService<JobHandlerRegistry>()
            .Register("Demo", "Executar", static (_, _) => ValueTask.CompletedTask);

        var hosted = HostedService(provider);
        var storage = provider.GetRequiredService<IStorage>();

        await hosted.StartAsync(Ct);
        try
        {
            var id = await provider.GetRequiredService<IGuaraClient>()
                .EnfileirarAsync(new JobDescriptor("Demo", "Executar", default), Ct);

            var job = await WaitUntilAsync(storage, id, j => j is null, TimeSpan.FromSeconds(10));
            Assert.Null(job); // executado com sucesso e purgado pela retenção zero
        }
        finally
        {
            await hosted.StopAsync(Ct);
        }
    }
}
