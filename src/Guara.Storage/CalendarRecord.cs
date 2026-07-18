namespace Guara.Storage;

/// <summary>
/// Calendário de exclusões reutilizável por recorrentes: uma lista de regras
/// avaliadas em <b>OU</b> — o instante é excluído se qualquer regra o cobrir.
/// Datas e dias da semana valem para o dia inteiro (no fuso do recorrente);
/// janelas cron valem para o minuto exato.
/// </summary>
public sealed record CalendarRecord
{
    /// <summary>Nome único do calendário (chave do upsert).</summary>
    public required string Name { get; init; }

    /// <summary>Versão do formato persistido, para evolução do payload.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Datas excluídas (dia inteiro).</summary>
    public DateOnly[] ExcludedDates { get; init; } = [];

    /// <summary>Intervalos de datas excluídos (dia inteiro, pontas inclusivas).</summary>
    public CalendarDateRange[] ExcludedRanges { get; init; } = [];

    /// <summary>Dias da semana excluídos (dia inteiro).</summary>
    public DayOfWeek[] ExcludedDaysOfWeek { get; init; } = [];

    /// <summary>Janelas excluídas por expressão cron (minuto exato).</summary>
    public string[] ExcludedCronWindows { get; init; } = [];
}

/// <summary>Intervalo de datas com pontas inclusivas.</summary>
/// <param name="Start">Primeira data excluída.</param>
/// <param name="End">Última data excluída.</param>
public sealed record CalendarDateRange(DateOnly Start, DateOnly End);
