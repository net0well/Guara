using BenchmarkDotNet.Attributes;
using Guara.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Scheduler.Benchmarks;

/// <summary>
/// Custo de calcular o próximo disparo. O cron é implementação própria — sem Cronos — e
/// roda uma vez por definição recorrente a cada ciclo de promoção, então o custo se
/// multiplica pela quantidade de recorrentes cadastrados.
/// <para>
/// As expressões vão da mais barata (todo minuto, acerta na primeira tentativa) à mais
/// cara (29 de fevereiro, que precisa atravessar anos até achar bissexto). A distância
/// entre as duas é o que revela se a busca é inteligente ou força bruta minuto a minuto.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class CronBenchmarks
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 5, 12, 34, 56, TimeSpan.Zero);

    private IScheduler _scheduler = null!;
    private ServiceProvider _host = null!;

    private ScheduleDescriptor _cadaMinuto = null!;
    private ScheduleDescriptor _diario = null!;
    private ScheduleDescriptor _diasUteis = null!;
    private ScheduleDescriptor _bissexto = null!;
    private ScheduleDescriptor _comFuso = null!;
    private ScheduleDescriptor _atraso = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseMemoryStorage().AddGuaraScheduler();
        _host = services.BuildServiceProvider();
        _scheduler = _host.GetRequiredService<IScheduler>();

        _cadaMinuto = ScheduleDescriptor.Cron("* * * * *");
        _diario = ScheduleDescriptor.Cron("0 3 * * *");
        _diasUteis = ScheduleDescriptor.Cron("*/15 9-18 * * 1-5");
        _bissexto = ScheduleDescriptor.Cron("0 0 29 2 *");
        _comFuso = ScheduleDescriptor.Cron("0 3 * * *", "America/Sao_Paulo");
        _atraso = ScheduleDescriptor.After(TimeSpan.FromHours(6));
    }

    [GlobalCleanup]
    public void Cleanup() => _host.Dispose();

    [Benchmark(Description = "Cron '* * * * *' — acerta no minuto seguinte")]
    public DateTimeOffset? CadaMinuto() => _scheduler.GetNextOccurrence(_cadaMinuto, T0);

    [Benchmark(Description = "Cron '0 3 * * *' — diário")]
    public DateTimeOffset? Diario() => _scheduler.GetNextOccurrence(_diario, T0);

    [Benchmark(Description = "Cron '*/15 9-18 * * 1-5' — passo, faixa e dia da semana")]
    public DateTimeOffset? DiasUteis() => _scheduler.GetNextOccurrence(_diasUteis, T0);

    [Benchmark(Description = "Cron '0 0 29 2 *' — atravessa anos até o bissexto")]
    public DateTimeOffset? Bissexto() => _scheduler.GetNextOccurrence(_bissexto, T0);

    [Benchmark(Description = "Cron diário com fuso (conversão + DST)")]
    public DateTimeOffset? ComFuso() => _scheduler.GetNextOccurrence(_comFuso, T0);

    [Benchmark(Description = "Delay — piso de comparação, sem cron")]
    public DateTimeOffset? Atraso() => _scheduler.GetNextOccurrence(_atraso, T0);
}
