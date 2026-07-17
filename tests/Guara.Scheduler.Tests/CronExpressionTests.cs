using Guara.Scheduler;
using Xunit;

namespace Guara.Scheduler.Tests;

public class CronExpressionTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset? Next(string cron, DateTimeOffset after, TimeZoneInfo? tz = null)
        => CronExpression.Parse(cron).GetNextOccurrence(after, tz ?? Utc);

    private static DateTimeOffset UtcAt(int y, int mo, int d, int h = 0, int mi = 0)
        => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    // --- Sintaxe básica ---

    [Fact]
    public void EveryFiveMinutes_FromMidInterval()
        => Assert.Equal(UtcAt(2026, 7, 16, 10, 5), Next("*/5 * * * *", UtcAt(2026, 7, 16, 10, 2)));

    [Fact]
    public void DailyAtThree_BeforeThree_SameDay()
        => Assert.Equal(UtcAt(2026, 7, 16, 3, 0), Next("0 3 * * *", UtcAt(2026, 7, 16, 2, 0)));

    [Fact]
    public void StrictlyAfter_ExactMatchMovesToNextDay()
        => Assert.Equal(UtcAt(2026, 7, 17, 3, 0), Next("0 3 * * *", UtcAt(2026, 7, 16, 3, 0)));

    [Fact]
    public void List_QuartersOfHour()
        => Assert.Equal(UtcAt(2026, 7, 16, 10, 30), Next("0,15,30,45 * * * *", UtcAt(2026, 7, 16, 10, 16)));

    [Fact]
    public void Range_BusinessHours()
        => Assert.Equal(UtcAt(2026, 7, 17, 9, 0), Next("0 9-17 * * *", UtcAt(2026, 7, 16, 18, 30)));

    [Fact]
    public void RangeWithStep_EveryOtherBusinessHour()
    {
        // horas {9, 11, 13, 15, 17}
        Assert.Equal(UtcAt(2026, 7, 16, 11, 0), Next("0 9-17/2 * * *", UtcAt(2026, 7, 16, 9, 30)));
    }

    [Fact]
    public void StartWithStep_FromValueToMax()
    {
        // minutos {5, 15, 25, 35, 45, 55}
        Assert.Equal(UtcAt(2026, 7, 16, 10, 5), Next("5/10 * * * *", UtcAt(2026, 7, 16, 10, 0)));
        Assert.Equal(UtcAt(2026, 7, 16, 10, 55), Next("5/10 * * * *", UtcAt(2026, 7, 16, 10, 46)));
    }

    [Fact]
    public void FirstDayOfMonth()
        => Assert.Equal(UtcAt(2026, 8, 1, 14, 30), Next("30 14 1 * *", UtcAt(2026, 7, 16, 0, 0)));

    // --- Nomes (meses e dias) ---

    [Fact]
    public void MonthName_January()
        => Assert.Equal(UtcAt(2027, 1, 1, 0, 0), Next("0 0 1 JAN *", UtcAt(2026, 7, 16, 0, 0)));

    [Fact]
    public void DayName_Monday_CaseInsensitive()
    {
        // 2026-07-17 é sexta; próxima segunda = 2026-07-20
        Assert.Equal(UtcAt(2026, 7, 20, 8, 0), Next("0 8 * * mon", UtcAt(2026, 7, 17, 9, 0)));
    }

    [Fact]
    public void Sunday_SevenEqualsZero()
    {
        var after = UtcAt(2026, 7, 16, 0, 0); // quinta
        Assert.Equal(Next("0 0 * * 0", after), Next("0 0 * * 7", after)); // domingo 2026-07-19
        Assert.Equal(UtcAt(2026, 7, 19, 0, 0), Next("0 0 * * 7", after));
    }

    // --- Regra OU (dia-do-mês E dia-da-semana restritos) ---

    [Fact]
    public void DomAndDow_BothRestricted_MatchesEither()
    {
        // "meio-dia no dia 13 OU às sextas"
        // De qui 2026-07-16: a sexta 17/07 vem antes do dia 13/08
        Assert.Equal(UtcAt(2026, 7, 17, 12, 0), Next("0 12 13 * 5", UtcAt(2026, 7, 16, 13, 0)));

        // De sáb 2026-08-08: o dia 13 (qui) vem antes da sexta 14
        Assert.Equal(UtcAt(2026, 8, 13, 12, 0), Next("0 12 13 * 5", UtcAt(2026, 8, 8, 0, 0)));
    }

    [Fact]
    public void OnlyDowRestricted_DomWildcardIsAnd()
    {
        // dow restrito, dom livre → só sexta
        Assert.Equal(UtcAt(2026, 7, 17, 12, 0), Next("0 12 * * FRI", UtcAt(2026, 7, 16, 13, 0)));
    }

    // --- Ano bissexto e horizonte ---

    [Fact]
    public void LeapDay_JumpsToLeapYear()
        => Assert.Equal(UtcAt(2028, 2, 29, 0, 0), Next("0 0 29 2 *", UtcAt(2026, 1, 1, 0, 0)));

    [Fact]
    public void ImpossibleDate_ReturnsNull()
        => Assert.Null(Next("0 0 30 2 *", UtcAt(2026, 1, 1, 0, 0)));

    // --- Fuso horário e DST ---

    [Fact]
    public void TimeZone_SaoPaulo_ThreeLocalIsSixUtc()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); // UTC-3, sem DST
        var next = Next("0 3 * * *", UtcAt(2026, 7, 16, 0, 0), tz);

        Assert.NotNull(next);
        Assert.Equal(UtcAt(2026, 7, 16, 6, 0), next.Value.ToUniversalTime());
        Assert.Equal(TimeSpan.FromHours(-3), next.Value.Offset);
    }

    [Fact]
    public void Dst_SpringForwardGap_FiresRightAfterTransition()
    {
        // America/New_York: 2026-03-08 02:00 EST → 03:00 EDT (02:30 local não existe)
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var after = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5));

        var next = Next("30 2 * * *", after, tz);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)), next.Value);
    }

    [Fact]
    public void Dst_FallBackAmbiguous_UsesFirstOccurrence()
    {
        // America/New_York: 2026-11-01 02:00 EDT → 01:00 EST (01:30 local acontece duas vezes)
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var after = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

        var next = Next("30 1 * * *", after, tz);

        Assert.NotNull(next);
        Assert.Equal(TimeSpan.FromHours(-4), next.Value.Offset); // primeira ocorrência (ainda EDT)
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), next.Value.ToUniversalTime());
    }

    // --- Expressões inválidas ---

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * 32 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("a * * * *")]
    [InlineData("10-5 * * * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("1,,2 * * * *")]
    public void InvalidExpressions_Throw(string cron)
    {
        Assert.ThrowsAny<Exception>(() => CronExpression.Parse(cron));
        Assert.False(CronExpression.TryParse(cron == "" ? " " : cron, out _));
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(CronExpression.TryParse("*/5 * * * *", out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal("*/5 * * * *", parsed.ToString());
    }

    // --- Fachada com cache ---

    [Fact]
    public void GuaraCronParser_DelegatesAndCaches()
    {
        var parser = new GuaraCronParser();
        var first = parser.GetNext("0 3 * * *", Utc, UtcAt(2026, 7, 16, 0, 0));
        var second = parser.GetNext("0 3 * * *", Utc, UtcAt(2026, 7, 16, 4, 0));

        Assert.Equal(UtcAt(2026, 7, 16, 3, 0), first);
        Assert.Equal(UtcAt(2026, 7, 17, 3, 0), second);
    }

    [Fact]
    public void GuaraCronParser_InvalidExpression_Throws()
    {
        var parser = new GuaraCronParser();
        Assert.Throws<FormatException>(() => parser.GetNext("99 * * * *", Utc, UtcAt(2026, 1, 1)));
    }
}
