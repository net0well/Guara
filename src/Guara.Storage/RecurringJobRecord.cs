using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Definição persistida de um job recorrente. A agenda é <b>cron ou intervalo</b>
/// (exatamente um); a janela diária vale apenas para intervalo. O próximo disparo
/// (<see cref="NextRunAt"/>) é recalculado na promoção de cada ocorrência —
/// <c>null</c> significa expirado ou sem ocorrência futura possível.
/// </summary>
public sealed record RecurringJobRecord
{
    /// <summary>Identificador estável da definição (chave do upsert).</summary>
    public required string Id { get; init; }

    /// <summary>O que executar em cada ocorrência.</summary>
    public required JobDescriptor Descriptor { get; init; }

    /// <summary>Expressão cron de 5 campos, quando a agenda é cron.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Intervalo entre ocorrências, quando a agenda é por intervalo.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>Início da janela diária do intervalo (início &gt; fim cruza a meia-noite).</summary>
    public TimeOnly? WindowStart { get; init; }

    /// <summary>Fim da janela diária do intervalo.</summary>
    public TimeOnly? WindowEnd { get; init; }

    /// <summary>Fuso em que a agenda é avaliada (id IANA ou Windows); nulo = UTC.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Nenhuma ocorrência antes deste instante; também ancora a grade do intervalo.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Nenhuma ocorrência depois deste instante (a definição fica expirada, não excluída).</summary>
    public DateTimeOffset? NotAfter { get; init; }

    /// <summary>Descrição exibida no dashboard.</summary>
    public string? Description { get; init; }

    /// <summary>Fila em que as ocorrências entram.</summary>
    public string Queue { get; init; } = "default";

    /// <summary>Calendário de exclusões aplicado ao cálculo das ocorrências.</summary>
    public string? CalendarName { get; init; }

    /// <summary>Pula a ocorrência se a anterior ainda não terminou (registrando o pulo).</summary>
    public bool SkipIfPreviousRunning { get; init; }

    /// <summary>Definição pausada não promove ocorrências até ser retomada.</summary>
    public bool Paused { get; init; }

    /// <summary>Instante de criação da definição (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Última promoção de ocorrência (UTC).</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>Job da última ocorrência promovida (base da checagem de sobreposição).</summary>
    public JobId? LastRunJobId { get; init; }

    /// <summary>Próximo disparo; <c>null</c> = expirado ou sem ocorrência futura.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>Última ocorrência pulada por sobreposição (visível no dashboard).</summary>
    public DateTimeOffset? LastSkippedAt { get; init; }
}
