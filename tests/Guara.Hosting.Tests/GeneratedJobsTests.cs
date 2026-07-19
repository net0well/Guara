using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Guara.Hosting.Tests;

public class GeneratedJobsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SaudacaoServico>();
        services.AddGuara(options => options.ApplicationName = "teste-gerado")
            .UseMemoryStorage()
            .AddGuaraJobs()
            .AddGuaraDispatcher(dispatcher =>
            {
                dispatcher.PollingInterval = TimeSpan.FromMilliseconds(50);
                dispatcher.Queues = ["saudacoes", "default"];
            })
            .AddGuaraWorker(worker => worker.MaxConcurrency = 2)
            .AddGuaraExecutor(retry => retry.Backoff = static _ => TimeSpan.Zero)
            .AddGuaraServer();
        return services.BuildServiceProvider();
    }

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
    public async Task TypedFactory_EnqueuesAndExecutes_WithInjectedService()
    {
        await using var provider = BuildHost();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var storage = provider.GetRequiredService<IStorage>();

        await hosted.StartAsync(Ct);
        try
        {
            // Descritor gerado em compilação: assinatura errada nem compila.
            var id = await provider.GetRequiredService<IGuaraClient>()
                .EnfileirarAsync(SaudacaoJobsGuara.SaudarAsync("mundo", 2), Ct);

            var job = await WaitUntilAsync(storage, id, j => j?.State == JobState.Succeeded, TimeSpan.FromSeconds(10));
            Assert.Equal(JobState.Succeeded, job?.State);
            Assert.Equal("saudacoes", job!.Queue); // [GuaraFila] aplicada na criação

            var servico = provider.GetRequiredService<SaudacaoServico>();
            Assert.Equal(["olá, mundo", "olá, mundo"], servico.Mensagens);
        }
        finally
        {
            await hosted.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task GeneratedJob_AttributeRetries_OverrideGlobalPolicy()
    {
        await using var provider = BuildHost();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var storage = provider.GetRequiredService<IStorage>();

        await hosted.StartAsync(Ct);
        try
        {
            var id = await provider.GetRequiredService<IGuaraClient>()
                .EnfileirarAsync(SaudacaoJobsGuara.FalharAsync(), Ct);

            var job = await WaitUntilAsync(storage, id, j => j?.State == JobState.Failed, TimeSpan.FromSeconds(10));
            Assert.Equal(JobState.Failed, job?.State);
            Assert.Equal(0, job!.Attempt); // [GuaraRetentativas(0)] vence o default global (3)
            Assert.Contains("sempre falha", job.Error);
        }
        finally
        {
            await hosted.StopAsync(Ct);
        }
    }
}
