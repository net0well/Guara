using Guara.Abstractions;
using Guara.Core;
using Guara.Executor;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Engines.Tests;

/// <summary>
/// Primeiro fluxo completo do Guará: EnfileirarAsync → Dispatcher (aquisição atômica)
/// → WorkerRequested → Worker (slots) → Executor (pipeline) → Succeeded/Failed —
/// tudo sobre o storage in-memory, comunicando-se apenas por eventos e contratos.
/// </summary>
public class EndToEndTests
{
    private sealed class TestGuaraBuilder(IServiceCollection services) : IGuaraBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        var builder = new TestGuaraBuilder(services);

        services.AddSingleton<IEventPublisher, InProcessEventPublisher>(); // wiring do Hosting (spec 009), manual por ora
        builder
            .UseMemoryStorage()
            .AddGuaraScheduler()
            .AddGuaraExecutor(retry => retry.Backoff = static _ => TimeSpan.Zero)
            .AddGuaraWorker(worker => worker.MaxConcurrency = 2)
            .AddGuaraDispatcher(dispatcher => dispatcher.PollingInterval = TimeSpan.FromMilliseconds(50));

        return services.BuildServiceProvider();
    }

    private static async Task<JobRecord> WaitForTerminalStateAsync(IStorage storage, JobId id)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = await storage.Jobs.GetAsync(id, Ct);
            if (job is { State: JobState.Succeeded or JobState.Failed })
            {
                return job;
            }

            await Task.Delay(25, Ct);
        }

        throw new TimeoutException($"Job {id} não atingiu estado terminal a tempo.");
    }

    [Fact]
    public async Task FireAndForget_RunsEndToEnd()
    {
        await using var host = BuildHost();
        var processed = 0;
        host.GetRequiredService<JobHandlerRegistry>().Register("Relatorio", "Gerar", (_, _) =>
        {
            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        });

        var worker = host.GetRequiredService<IWorker>();
        var dispatcher = host.GetRequiredService<IDispatcher>();
        await worker.StartAsync(Ct);
        await dispatcher.StartAsync(Ct);

        try
        {
            var client = host.GetRequiredService<IGuaraClient>();
            var storage = host.GetRequiredService<IStorage>();

            var ids = new List<JobId>();
            for (var i = 0; i < 5; i++)
            {
                ids.Add(await client.EnfileirarAsync(new JobDescriptor("Relatorio", "Gerar", default), Ct));
            }

            foreach (var id in ids)
            {
                var job = await WaitForTerminalStateAsync(storage, id);
                Assert.Equal(JobState.Succeeded, job.State);
            }

            Assert.Equal(5, processed);
        }
        finally
        {
            await dispatcher.StopAsync(Ct);
            await worker.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task DelayedJob_RunsAfterDelay()
    {
        await using var host = BuildHost();
        host.GetRequiredService<JobHandlerRegistry>().Register("Lembrete", "Enviar",
            static (_, _) => ValueTask.CompletedTask);

        var worker = host.GetRequiredService<IWorker>();
        var dispatcher = host.GetRequiredService<IDispatcher>();
        await worker.StartAsync(Ct);
        await dispatcher.StartAsync(Ct);

        try
        {
            var client = host.GetRequiredService<IGuaraClient>();
            var storage = host.GetRequiredService<IStorage>();

            var id = await client.AgendarAsync(
                new JobDescriptor("Lembrete", "Enviar", default), TimeSpan.FromMilliseconds(200), Ct);

            var job = await WaitForTerminalStateAsync(storage, id);
            Assert.Equal(JobState.Succeeded, job.State);
        }
        finally
        {
            await dispatcher.StopAsync(Ct);
            await worker.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task FailingJob_EndsAsFailed_WithoutStoppingTheWorker()
    {
        await using var host = BuildHost();
        var registry = host.GetRequiredService<JobHandlerRegistry>();
        registry.Register("Cobranca", "Executar",
            static (_, _) => throw new InvalidOperationException("cartão recusado"));
        registry.Register("Relatorio", "Gerar", static (_, _) => ValueTask.CompletedTask);

        var worker = host.GetRequiredService<IWorker>();
        var dispatcher = host.GetRequiredService<IDispatcher>();
        await worker.StartAsync(Ct);
        await dispatcher.StartAsync(Ct);

        try
        {
            var client = host.GetRequiredService<IGuaraClient>();
            var storage = host.GetRequiredService<IStorage>();

            var failing = await client.EnfileirarAsync(new JobDescriptor("Cobranca", "Executar", default), Ct);
            var healthy = await client.EnfileirarAsync(new JobDescriptor("Relatorio", "Gerar", default), Ct);

            var failed = await WaitForTerminalStateAsync(storage, failing);
            Assert.Equal(JobState.Failed, failed.State);
            Assert.Contains("cartão recusado", failed.Error);

            var succeeded = await WaitForTerminalStateAsync(storage, healthy); // worker segue vivo
            Assert.Equal(JobState.Succeeded, succeeded.State);
        }
        finally
        {
            await dispatcher.StopAsync(Ct);
            await worker.StopAsync(Ct);
        }
    }
}
