using BenchmarkDotNet.Attributes;
using Guara.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Scheduler.Benchmarks;

/// <summary>
/// Custo de uma chamada de <see cref="IGuaraClient"/> pela composição real — id, registro
/// no storage, evento e aviso de fila. É o que o usuário paga na linha dele; o storage
/// in-memory mantém o número livre de rede e disco, isolando o que é do framework.
/// </summary>
[MemoryDiagnoser]
public class EnqueueBenchmarks
{
    private ServiceProvider _host = null!;
    private IGuaraClient _client = null!;
    private JobDescriptor _job = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseMemoryStorage().AddGuaraScheduler();
        _host = services.BuildServiceProvider();
        _client = _host.GetRequiredService<IGuaraClient>();
        _job = new JobDescriptor("Relatorios.Mensal", "GerarAsync", default, "relatorios");
    }

    [GlobalCleanup]
    public void Cleanup() => _host.Dispose();

    [Benchmark(Description = "EnfileirarAsync — fire-and-forget")]
    public async ValueTask<JobId> Enfileirar() => await _client.EnfileirarAsync(_job, CancellationToken.None);

    /// <summary>
    /// Agendar com atraso não avisa a fila — o job tem data futura e o aviso acordaria o
    /// dispatcher para não achar nada — mas publica um evento a mais (<c>JobScheduled</c>
    /// além de <c>JobCreated</c>). Os dois efeitos andam em sentidos opostos, então a
    /// diferença entre os dois números não isola nenhum deles.
    /// </summary>
    [Benchmark(Description = "AgendarAsync — com atraso, sem aviso de fila")]
    public async ValueTask<JobId> Agendar()
        => await _client.AgendarAsync(_job, TimeSpan.FromHours(1), CancellationToken.None);
}
