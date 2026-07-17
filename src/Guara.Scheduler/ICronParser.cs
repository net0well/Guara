using System.Collections.Concurrent;

namespace Guara.Scheduler;

/// <summary>
/// Fachada de cálculo de cron usada pelo scheduler. A implementação default é
/// <b>própria</b> (<see cref="GuaraCronParser"/>) — sem terceiros (ADR-0009) — e
/// substituível para cenários especiais.
/// </summary>
public interface ICronParser
{
    /// <summary>Calcula a próxima ocorrência estritamente depois de <paramref name="after"/>.</summary>
    /// <param name="expression">Expressão cron de 5 campos.</param>
    /// <param name="timeZone">Fuso horário de avaliação.</param>
    /// <param name="after">Instante de referência (exclusivo).</param>
    /// <returns>A próxima ocorrência, ou <c>null</c> quando não há.</returns>
    /// <exception cref="FormatException">Expressão inválida.</exception>
    DateTimeOffset? GetNext(string expression, TimeZoneInfo timeZone, DateTimeOffset after);
}

/// <summary>
/// Implementação default de <see cref="ICronParser"/> sobre <see cref="CronExpression"/>,
/// com cache de expressões interpretadas (expressões vêm de configuração — conjunto pequeno e estável).
/// </summary>
public sealed class GuaraCronParser : ICronParser
{
    private readonly ConcurrentDictionary<string, CronExpression> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public DateTimeOffset? GetNext(string expression, TimeZoneInfo timeZone, DateTimeOffset after)
        => _cache.GetOrAdd(expression, static e => CronExpression.Parse(e)).GetNextOccurrence(after, timeZone);
}
