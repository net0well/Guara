using Guara.Abstractions;
using Guara.Scheduler;
using Guara.Storage;
using Xunit;

namespace Guara.Scheduler.Tests;

public class RecurrenceCalculatorTests
{
    // 2026-07-16 é quinta-feira; 17 sexta, 18 sábado.
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static readonly RecurrenceCalculator Calculator = new(new GuaraCronParser());

    private static RecurringJobRecord CronJob(string cron, string? timeZoneId = null) => new()
    {
        Id = "r",
        Descriptor = new JobDescriptor("T", "M", default),
        CronExpression = cron,
        TimeZoneId = timeZoneId,
        CreatedAt = T0,
    };

    private static RecurringJobRecord IntervalJob(
        TimeSpan interval, DateTimeOffset? notBefore = null, TimeOnly? windowStart = null, TimeOnly? windowEnd = null) => new()
    {
        Id = "r",
        Descriptor = new JobDescriptor("T", "M", default),
        Interval = interval,
        NotBefore = notBefore,
        WindowStart = windowStart,
        WindowEnd = windowEnd,
        CreatedAt = T0,
    };

    private static DateTimeOffset Utc(int d, int h, int m = 0) => new(2026, 7, d, h, m, 0, TimeSpan.Zero);

    [Fact]
    public void Cron_SemCalendario_ProximoDisparo()
        => Assert.Equal(Utc(17, 3), Calculator.GetNextOccurrence(CronJob("0 3 * * *"), null, T0));

    [Fact]
    public void Calendario_DataExcluida_PulaParaProximaValida()
    {
        var calendar = new CalendarRecord { Name = "c", ExcludedDates = [new DateOnly(2026, 7, 17)] };
        Assert.Equal(Utc(18, 3), Calculator.GetNextOccurrence(CronJob("0 3 * * *"), calendar, T0));
    }

    [Fact]
    public void Calendario_DiaDaSemanaExcluido_Pula()
    {
        var calendar = new CalendarRecord { Name = "c", ExcludedDaysOfWeek = [DayOfWeek.Friday] };
        Assert.Equal(Utc(18, 3), Calculator.GetNextOccurrence(CronJob("0 3 * * *"), calendar, T0));
    }

    [Fact]
    public void Calendario_IntervaloDeDatas_PulaTodoOIntervalo()
    {
        var calendar = new CalendarRecord
        {
            Name = "c",
            ExcludedRanges = [new CalendarDateRange(new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 20))],
        };
        Assert.Equal(Utc(21, 3), Calculator.GetNextOccurrence(CronJob("0 3 * * *"), calendar, T0));
    }

    [Fact]
    public void Calendario_JanelaCron_ExcluiMinutoExato()
    {
        // Exclui as 03:00 das sextas: a ocorrência de sexta 17/07 é pulada.
        var calendar = new CalendarRecord { Name = "c", ExcludedCronWindows = ["0 3 * * 5"] };
        Assert.Equal(Utc(18, 3), Calculator.GetNextOccurrence(CronJob("0 3 * * *"), calendar, T0));
    }

    [Fact]
    public void Calendario_QueExcluiTodasAsOcorrencias_RetornaNull()
    {
        var calendar = new CalendarRecord { Name = "c", ExcludedCronWindows = ["0 3 * * *"] };
        Assert.Null(Calculator.GetNextOccurrence(CronJob("0 3 * * *"), calendar, T0));
    }

    [Fact]
    public void Calendario_DataAvaliadaNoFusoDoRecorrente()
    {
        // 22:00 em UTC-3 = 01:00Z do dia seguinte: a exclusão de 25/12 vale pela data LOCAL.
        var job = CronJob("0 22 * * *", "America/Sao_Paulo");
        var calendar = new CalendarRecord { Name = "c", ExcludedDates = [new DateOnly(2026, 12, 25)] };
        var after = new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.FromHours(-3));

        var next = Calculator.GetNextOccurrence(job, calendar, after);

        Assert.Equal(new DateTimeOffset(2026, 12, 26, 22, 0, 0, TimeSpan.FromHours(-3)), next);
    }

    [Fact]
    public void Intervalo_GradeAncoradaNoInicio()
    {
        var job = IntervalJob(TimeSpan.FromMinutes(10), notBefore: T0);

        // Antes do início, a primeira ocorrência é o próprio início.
        Assert.Equal(T0, Calculator.GetNextOccurrence(job, null, T0 - TimeSpan.FromHours(1)));

        // Estritamente depois do início, vale o próximo ponto da grade.
        Assert.Equal(T0 + TimeSpan.FromMinutes(10), Calculator.GetNextOccurrence(job, null, T0));
    }

    [Fact]
    public void Intervalo_JanelaDiaria_ForaDaJanelaSaltaParaProxima()
    {
        var job = IntervalJob(
            TimeSpan.FromHours(1), notBefore: Utc(16, 0),
            windowStart: new TimeOnly(8, 0), windowEnd: new TimeOnly(18, 0));

        Assert.Equal(Utc(16, 11), Calculator.GetNextOccurrence(job, null, T0));            // dentro da janela
        Assert.Equal(Utc(17, 8), Calculator.GetNextOccurrence(job, null, Utc(16, 18, 30))); // depois do fim → amanhã às 08:00
    }

    [Fact]
    public void Intervalo_JanelaQueCruzaAMeiaNoite()
    {
        var job = IntervalJob(
            TimeSpan.FromHours(1), notBefore: Utc(16, 0),
            windowStart: new TimeOnly(22, 0), windowEnd: new TimeOnly(6, 0));

        Assert.Equal(Utc(16, 22), Calculator.GetNextOccurrence(job, null, T0));         // 10h está fora → hoje às 22:00
        Assert.Equal(Utc(17, 0), Calculator.GetNextOccurrence(job, null, Utc(16, 23, 30))); // madrugada continua dentro
    }

    [Fact]
    public void Vigencia_TerminaEm_RetornaNull()
    {
        var job = CronJob("0 3 * * *") with { NotAfter = Utc(17, 2) };
        Assert.Null(Calculator.GetNextOccurrence(job, null, T0));
    }

    [Fact]
    public void Intervalo_SubMinuto_Funciona()
    {
        var job = IntervalJob(TimeSpan.FromMilliseconds(100), notBefore: T0);
        Assert.Equal(T0 + TimeSpan.FromMilliseconds(100), Calculator.GetNextOccurrence(job, null, T0));
    }
}
