using Guara.Abstractions;

namespace Guara.Scheduler;

/// <summary>
/// Implementação default de <see cref="IScheduler"/>: imediato dispara já; delay soma o
/// atraso; cron/recorrente delegam ao <see cref="ICronParser"/>. Fusos aceitam ids IANA
/// (<c>America/Sao_Paulo</c>) e Windows nos dois sistemas — resolução nativa do .NET,
/// sem pacotes de terceiros (ADR-0009/spec 038).
/// </summary>
public sealed class GuaraScheduler(ICronParser cronParser) : IScheduler
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
                ResolveTimeZone(schedule.TimeZoneId),
                after),
            _ => null,
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId))
        {
            return TimeZoneInfo.Utc; // default do Guará (spec 005, DD-3)
        }

        try
        {
            // Aceita IANA e Windows: o .NET converte nativamente quando necessário.
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new TimeZoneNotFoundException(
                $"Fuso horário '{timeZoneId}' não encontrado. Use um id IANA " +
                "(ex.: 'America/Sao_Paulo') ou Windows (ex.: 'E. South America Standard Time').", ex);
        }
    }
}
