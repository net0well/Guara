using Guara.Abstractions;
using Guara.Executor;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Engines.Tests;

/// <summary>
/// O dispatcher acorda pelo aviso de trabalho novo, não pelo ciclo periódico. O intervalo
/// é configurado alto de propósito: se o job roda, foi o aviso que trouxe o laço de volta.
/// </summary>
public class DispatchWakeupTests
{
    // Alto o bastante para que qualquer despacho dentro do prazo do teste só possa ter
    // vindo do aviso — nunca de um ciclo de busca que venceu.
    private static readonly TimeSpan IntervaloInalcancavel = TimeSpan.FromMinutes(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddGuara()
            .UseMemoryStorage()
            .AddGuaraScheduler()
            .AddGuaraExecutor(retry => retry.Backoff = static _ => TimeSpan.Zero)
            .AddGuaraWorker(worker => worker.MaxConcurrency = 2)
            .AddGuaraDispatcher(dispatcher =>
            {
                dispatcher.PollingInterval = IntervaloInalcancavel;
                dispatcher.MaxBackoff = IntervaloInalcancavel;
                dispatcher.Queues = ["relatorios"];
            });

        return services.BuildServiceProvider();
    }

    private static async Task<JobRecord> WaitForTerminalStateAsync(IStorage storage, JobId id)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await storage.Jobs.GetAsync(id, Ct) is { State: JobState.Succeeded or JobState.Failed } job)
            {
                return job;
            }

            await Task.Delay(25, Ct);
        }

        throw new TimeoutException(
            $"Job {id} não rodou dentro do prazo com o ciclo de busca em {IntervaloInalcancavel}: " +
            "o aviso de trabalho não acordou o dispatcher.");
    }

    [Fact]
    public async Task EnqueuedJob_RunsWithoutWaitingForThePollingInterval()
    {
        await using var host = BuildHost();
        host.GetRequiredService<JobHandlerRegistry>().Register("Relatorio", "Gerar",
            static (_, _) => ValueTask.CompletedTask);

        var worker = host.GetRequiredService<IWorker>();
        var dispatcher = host.GetRequiredService<IDispatcher>();
        await worker.StartAsync(Ct);
        await dispatcher.StartAsync(Ct);

        try
        {
            var client = host.GetRequiredService<IGuaraClient>();
            var storage = host.GetRequiredService<IStorage>();

            // O laço já rodou o primeiro ciclo com a fila vazia e está aguardando o aviso:
            // é exatamente a janela em que o polling puro deixaria o job parado.
            await Task.Delay(100, Ct);

            var id = await client.EnfileirarAsync(
                new JobDescriptor("Relatorio", "Gerar", default, "relatorios"), Ct);

            var job = await WaitForTerminalStateAsync(storage, id);
            Assert.Equal(JobState.Succeeded, job.State);
        }
        finally
        {
            await dispatcher.StopAsync(Ct);
            await worker.StopAsync(Ct);
        }
    }

    /// <summary>
    /// Parar e voltar a rodar precisa mesmo voltar a rodar. A guarda de idempotência do
    /// <c>StartAsync</c> olha o laço registrado, então um <c>StopAsync</c> que não limpa
    /// esse registro transforma o start seguinte num silêncio — nada falha, nada roda.
    /// </summary>
    [Fact]
    public async Task WorkerAndDispatcher_ProcessAgainAfterStopAndStart()
    {
        await using var host = BuildHost();
        host.GetRequiredService<JobHandlerRegistry>().Register("Relatorio", "Gerar",
            static (_, _) => ValueTask.CompletedTask);

        var worker = host.GetRequiredService<IWorker>();
        var dispatcher = host.GetRequiredService<IDispatcher>();
        var client = host.GetRequiredService<IGuaraClient>();
        var storage = host.GetRequiredService<IStorage>();
        var descriptor = new JobDescriptor("Relatorio", "Gerar", default, "relatorios");

        await worker.StartAsync(Ct);
        await dispatcher.StartAsync(Ct);
        var antes = await client.EnfileirarAsync(descriptor, Ct);
        Assert.Equal(JobState.Succeeded, (await WaitForTerminalStateAsync(storage, antes)).State);

        await dispatcher.StopAsync(Ct);
        await worker.StopAsync(Ct);

        // Enfileirado com tudo parado: só roda se o ciclo seguinte realmente reiniciar.
        var durante = await client.EnfileirarAsync(descriptor, Ct);

        await worker.StartAsync(Ct);
        await dispatcher.StartAsync(Ct);

        try
        {
            Assert.Equal(JobState.Succeeded, (await WaitForTerminalStateAsync(storage, durante)).State);
        }
        finally
        {
            await dispatcher.StopAsync(Ct);
            await worker.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task DispatcherStopsPromptly_WhileWaitingForASignal()
    {
        await using var host = BuildHost();
        var dispatcher = host.GetRequiredService<IDispatcher>();
        await dispatcher.StartAsync(Ct);
        await Task.Delay(100, Ct);

        // A parada não pode depender do teto da espera vencer: o cancelamento tem de
        // atravessar o aguardo do aviso.
        var parada = Task.Run(async () => await dispatcher.StopAsync(Ct), Ct);

        Assert.Same(parada, await Task.WhenAny(parada, Task.Delay(TimeSpan.FromSeconds(10), Ct)));
        await parada;
    }
}
