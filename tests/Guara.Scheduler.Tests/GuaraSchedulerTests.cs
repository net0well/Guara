using Guara.Abstractions;
using Guara.Scheduler;
using Xunit;

namespace Guara.Scheduler.Tests;

public class GuaraSchedulerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
    private readonly GuaraScheduler _scheduler = new(new GuaraCronParser());

    [Fact]
    public void Immediate_FiresNow()
        => Assert.Equal(T0, _scheduler.GetNextOccurrence(ScheduleDescriptor.Immediate(), T0));

    [Fact]
    public void Delay_AddsInterval()
        => Assert.Equal(
            T0 + TimeSpan.FromHours(2),
            _scheduler.GetNextOccurrence(ScheduleDescriptor.After(TimeSpan.FromHours(2)), T0));

    [Fact]
    public void Cron_DefaultTimeZoneIsUtc()
        => Assert.Equal(
            new DateTimeOffset(2026, 7, 17, 3, 0, 0, TimeSpan.Zero),
            _scheduler.GetNextOccurrence(ScheduleDescriptor.Cron("0 3 * * *"), T0));

    [Theory]
    [InlineData("America/Sao_Paulo")]                 // id IANA
    [InlineData("E. South America Standard Time")]    // id Windows — mesmo fuso
    public void Cron_AcceptsIanaAndWindowsTimeZoneIds(string tzId)
    {
        var next = _scheduler.GetNextOccurrence(ScheduleDescriptor.Cron("0 3 * * *", tzId), T0);

        Assert.NotNull(next);
        // 03:00 em UTC-3 = 06:00Z (após T0=10:00Z do dia 16 → dia 17)
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 6, 0, 0, TimeSpan.Zero), next.Value.ToUniversalTime());
    }

    [Fact]
    public void Recurring_UsesCron()
        => Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero),
            _scheduler.GetNextOccurrence(ScheduleDescriptor.Recurring("id", "0 12 * * *"), T0));

    [Fact]
    public void UnknownTimeZone_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<TimeZoneNotFoundException>(
            () => _scheduler.GetNextOccurrence(ScheduleDescriptor.Cron("0 3 * * *", "Fuso/Inexistente"), T0));
        Assert.Contains("Fuso/Inexistente", ex.Message);
    }
}
