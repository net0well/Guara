using System.Collections.Concurrent;
using System.Diagnostics;
using Guara.Abstractions;
using Guara.Executor;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Throughput.Harness;

/// <summary>
/// Mede vazão e latência do Guará com worker e dispatcher rodando de verdade.
/// <para>
/// A rodada tem fases separadas de propósito. Enfileirar e drenar ao mesmo tempo
/// misturaria os dois custos num número só, e latência medida sob backlog seria apenas a
/// posição do job na fila — nenhum dos dois responde o que se quer saber.
/// </para>
/// </summary>
internal sealed class ThroughputHarness(HarnessOptions options, string? connectionString)
{
    private const string TypeName = "Bench";
    private const string MethodName = "Nada";
    private const string Queue = "default";

    /// <summary>Amostra da fase de latência: pequena, porque cada job espera a fila esvaziar.</summary>
    private const int AmostraDeLatencia = 200;

    public async Task<RunResult> RunAsync(int concurrency, CancellationToken ct)
    {
        var enfileiradoEm = new ConcurrentDictionary<JobId, long>();
        var latencias = new ConcurrentBag<double>();
        var drenagem = new DrainCounter();

        await using var host = BuildHost(concurrency);

        host.GetRequiredService<JobHandlerRegistry>().Register(TypeName, MethodName, (contexto, _) =>
        {
            // O handler não faz trabalho nenhum: mede-se o custo do framework em volta
            // dele. Qualquer coisa aqui dentro entraria no número como se fosse do Guará.
            if (enfileiradoEm.TryRemove(contexto.Id, out var carimbo))
            {
                latencias.Add(Stopwatch.GetElapsedTime(carimbo).TotalMilliseconds);
            }

            drenagem.Registrar();
            return ValueTask.CompletedTask;
        });

        var client = host.GetRequiredService<IGuaraClient>();
        var storage = host.GetRequiredService<IStorage>();
        var worker = host.GetRequiredService<IWorker>();
        var dispatcher = host.GetRequiredService<IDispatcher>();

        await AquecerAsync(client, storage, worker, dispatcher, ct);
        var latencia = await MedirLatenciaAsync(client, enfileiradoEm, latencias, worker, dispatcher, ct);

        // Fila enche com worker e dispatcher parados: o tempo é só o do enfileiramento,
        // sem disputa com quem drena.
        var descriptor = new JobDescriptor(TypeName, MethodName, default, Queue);
        var relogioEnfileiramento = Stopwatch.StartNew();
        for (var i = 0; i < options.Jobs; i++)
        {
            await client.EnfileirarAsync(descriptor, ct);
        }

        relogioEnfileiramento.Stop();

        // Agora o número que interessa: quantos jobs por segundo saem da fila e chegam ao
        // fim com esta concorrência.
        drenagem.Armar(options.Jobs);
        var alocadoAntes = GC.GetTotalAllocatedBytes(precise: true);
        var relogioDrenagem = Stopwatch.StartNew();

        await worker.StartAsync(ct);
        await dispatcher.StartAsync(ct);
        await drenagem.Concluido.WaitAsync(TimeSpan.FromMinutes(10), ct);

        relogioDrenagem.Stop();
        var alocado = GC.GetTotalAllocatedBytes(precise: true) - alocadoAntes;

        await dispatcher.StopAsync(ct);
        await worker.StopAsync(ct);

        return new RunResult(
            concurrency,
            options.Jobs,
            relogioEnfileiramento.Elapsed,
            relogioDrenagem.Elapsed,
            latencia.P50,
            latencia.P95,
            latencia.P99,
            alocado);
    }

    /// <summary>
    /// A primeira rodada paga JIT, abertura de conexão e criação do esquema. Sem
    /// descartar isso, o custo de subir apareceria como se fosse custo por job.
    /// </summary>
    private static async Task AquecerAsync(
        IGuaraClient client, IStorage storage, IWorker worker, IDispatcher dispatcher, CancellationToken ct)
    {
        await worker.StartAsync(ct);
        await dispatcher.StartAsync(ct);

        var ids = new List<JobId>();
        for (var i = 0; i < 50; i++)
        {
            ids.Add(await client.EnfileirarAsync(new JobDescriptor(TypeName, MethodName, default, Queue), ct));
        }

        await AguardarTerminaisAsync(storage, ids, ct);

        await dispatcher.StopAsync(ct);
        await worker.StopAsync(ct);
    }

    /// <summary>
    /// Latência de fire-and-forget: com a fila vazia e o worker ocioso, mede-se do
    /// enfileiramento ao início da execução. É aqui que o aviso de fila aparece — sem
    /// ele, cada job esperaria o próximo ciclo de busca.
    /// </summary>
    private async Task<(double P50, double P95, double P99)> MedirLatenciaAsync(
        IGuaraClient client,
        ConcurrentDictionary<JobId, long> enfileiradoEm,
        ConcurrentBag<double> latencias,
        IWorker worker,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        await worker.StartAsync(ct);
        await dispatcher.StartAsync(ct);

        var amostra = Math.Min(AmostraDeLatencia, options.Jobs);
        var descriptor = new JobDescriptor(TypeName, MethodName, default, Queue);

        for (var i = 0; i < amostra; i++)
        {
            var carimbo = Stopwatch.GetTimestamp();
            var id = await client.EnfileirarAsync(descriptor, ct);
            enfileiradoEm[id] = carimbo;

            // Espaço entre um job e o outro para a fila voltar a ficar vazia: medir com
            // backlog daria a posição na fila, não a latência de acordar e despachar.
            await Task.Delay(5, ct);
        }

        // Parar o worker drena o que está em voo, então nenhum job desta fase sobrevive
        // para atrapalhar a contagem da drenagem.
        await Task.Delay(500, ct);
        await dispatcher.StopAsync(ct);
        await worker.StopAsync(ct);

        var ordenado = latencias.ToList();
        ordenado.Sort();
        latencias.Clear();
        enfileiradoEm.Clear();

        return (
            RunResult.Percentil(ordenado, 0.50),
            RunResult.Percentil(ordenado, 0.95),
            RunResult.Percentil(ordenado, 0.99));
    }

    private static async Task AguardarTerminaisAsync(IStorage storage, List<JobId> ids, CancellationToken ct)
    {
        var limite = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (DateTimeOffset.UtcNow < limite)
        {
            var pendentes = 0;
            foreach (var id in ids)
            {
                if (await storage.Jobs.GetAsync(id, ct) is not { State: JobState.Succeeded or JobState.Failed })
                {
                    pendentes++;
                }
            }

            if (pendentes == 0)
            {
                return;
            }

            await Task.Delay(25, ct);
        }

        throw new TimeoutException("O aquecimento não concluiu a tempo.");
    }

    private ServiceProvider BuildHost(int concurrency)
    {
        var services = new ServiceCollection();
        var builder = services.AddGuara();

        // Schema exclusivo por rodada: uma concorrência não herda a fila da anterior.
        _ = options.Storage switch
        {
            StorageKind.Memory => builder.UseMemoryStorage(),
            StorageKind.PostgreSql => builder.UsePostgreSqlStorage(
                connectionString!, o => o.Schema = $"g{Guid.NewGuid():n}"[..16]),
            StorageKind.SqlServer => builder.UseSqlServerStorage(
                connectionString!, o => o.Schema = $"g{Guid.NewGuid():n}"[..16]),
            // No MySQL schema e banco são a mesma coisa: o isolamento é por prefixo.
            StorageKind.MySql => builder.UseMySqlStorage(
                connectionString!, o => o.TablePrefix = $"g{Guid.NewGuid():n}"[..12] + "_"),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Storage, "Storage desconhecido."),
        };

        builder
            .AddGuaraScheduler()
            .AddGuaraExecutor(retry => retry.Backoff = static _ => TimeSpan.Zero)
            .AddGuaraWorker(w => w.MaxConcurrency = concurrency)
            .AddGuaraDispatcher(d =>
            {
                d.Queues = [Queue];
                // Alto de propósito: com o aviso de fila ligado, o dispatcher acorda por
                // sinal. Intervalo curto mascararia um aviso quebrado, e o número medido
                // passaria a ser o do polling.
                d.PollingInterval = options.PollingInterval;
                d.MaxBackoff = options.PollingInterval;
            });

        return services.BuildServiceProvider();
    }
}
