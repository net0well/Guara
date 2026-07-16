namespace Guara.Abstractions;

/// <summary>Tipo de agendamento de um job.</summary>
public enum ScheduleKind
{
    /// <summary>Executar assim que possível (fire-and-forget).</summary>
    Immediate,

    /// <summary>Executar após um atraso.</summary>
    Delay,

    /// <summary>Executar conforme uma expressão cron.</summary>
    Cron,

    /// <summary>Job recorrente identificado por um id estável.</summary>
    Recurring,
}

/// <summary>
/// Descreve <b>quando</b> um job deve rodar. Imutável — construa pelas fábricas estáticas.
/// O cálculo do próximo disparo é responsabilidade do <c>Guara.Scheduler</c>.
/// </summary>
public sealed record ScheduleDescriptor
{
    private ScheduleDescriptor(
        ScheduleKind kind,
        TimeSpan? delay,
        string? cronExpression,
        string? timeZoneId,
        string? recurringId)
    {
        Kind = kind;
        Delay = delay;
        CronExpression = cronExpression;
        TimeZoneId = timeZoneId;
        RecurringId = recurringId;
    }

    /// <summary>Tipo do agendamento.</summary>
    public ScheduleKind Kind { get; }

    /// <summary>Atraso, quando <see cref="Kind"/> é <see cref="ScheduleKind.Delay"/>.</summary>
    public TimeSpan? Delay { get; }

    /// <summary>Expressão cron, quando aplicável.</summary>
    public string? CronExpression { get; }

    /// <summary>Fuso horário (id de <c>TimeZoneInfo</c>); nulo significa UTC.</summary>
    public string? TimeZoneId { get; }

    /// <summary>Id estável do job recorrente, quando <see cref="Kind"/> é <see cref="ScheduleKind.Recurring"/>.</summary>
    public string? RecurringId { get; }

    /// <summary>Agendamento imediato (fire-and-forget).</summary>
    public static ScheduleDescriptor Immediate() => new(ScheduleKind.Immediate, null, null, null, null);

    /// <summary>Agendamento com atraso.</summary>
    public static ScheduleDescriptor After(TimeSpan delay) => new(ScheduleKind.Delay, delay, null, null, null);

    /// <summary>Agendamento por expressão cron.</summary>
    public static ScheduleDescriptor Cron(string expression, string? timeZoneId = null)
        => new(ScheduleKind.Cron, null, expression, timeZoneId, null);

    /// <summary>Agendamento recorrente identificado por <paramref name="id"/>.</summary>
    public static ScheduleDescriptor Recurring(string id, string cronExpression, string? timeZoneId = null)
        => new(ScheduleKind.Recurring, null, cronExpression, timeZoneId, id);
}
