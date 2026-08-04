using Guara.Abstractions;

namespace Guara.Scheduler;

/// <summary>
/// Implementação default de <see cref="IScheduler"/>: imediato dispara já; delay soma o
/// atraso; cron/recorrente delegam ao <see cref="ICronParser"/>. Fusos aceitam ids IANA
/// (<c>America/Sao_Paulo</c>) e Windows nos dois sistemas — resolução nativa do .NET,
/// sem pacotes de terceiros.
/// </summary>
internal sealed class GuaraScheduler(ICronParser cronParser) : IScheduler
{
    /// <inheritdoc />
    public DateTimeOffset? GetNextOccurrence(ScheduleDescriptor schedule, DateTimeOffset after)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return schedule.Kind switch
        {
            ScheduleKind.Immediate => after,
            ScheduleKind.Delay => after + (schedule.Delay
                ?? throw new InvalidOperationException("ScheduleDescriptor de delay sem valor de atraso.")),
            ScheduleKind.Cron or ScheduleKind.Recurring => cronParser.GetNext(
                schedule.CronExpression
                    ?? throw new InvalidOperationException("ScheduleDescriptor de cron sem expressão."),
                TimeZones.Resolve(schedule.TimeZoneId),
                after),
            _ => null,
        };
    }
}
