using Guara.Storage;

namespace Guara.Scheduler;

/// <summary>
/// Calcula a próxima ocorrência de uma definição recorrente combinando a agenda
/// (cron ou intervalo com janela diária), a vigência e o calendário de exclusões.
/// O laço de interseção recomputa sempre <b>pela agenda</b>: um candidato excluído
/// pelo calendário avança para o próximo candidato que a agenda gera — nunca para
/// um instante que a agenda não geraria.
/// </summary>
public sealed class RecurrenceCalculator(ICronParser cronParser)
{
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(5 * 366);
    private const int MaxIterations = 100_000;

    /// <summary>
    /// Próxima ocorrência estritamente depois de <paramref name="after"/>, ou <c>null</c>
    /// quando a definição expirou (<c>NotAfter</c>) ou não há ocorrência possível no horizonte.
    /// </summary>
    /// <param name="recurring">Definição recorrente.</param>
    /// <param name="calendar">Calendário de exclusões, quando a definição usa um.</param>
    /// <param name="after">Instante de referência (exclusivo).</param>
    /// <returns>A próxima ocorrência válida.</returns>
    public DateTimeOffset? GetNextOccurrence(
        RecurringJobRecord recurring, CalendarRecord? calendar, DateTimeOffset after)
    {
        ArgumentNullException.ThrowIfNull(recurring);

        var tz = TimeZones.Resolve(recurring.TimeZoneId);
        var horizon = after + Horizon;

        // Vigência primeiro: cursor recua até logo antes do início para que a
        // primeira ocorrência possa ser exatamente o próprio início.
        var cursor = after;
        if (recurring.NotBefore is { } notBefore && cursor < notBefore)
        {
            cursor = notBefore.AddTicks(-1);
        }

        for (var i = 0; i < MaxIterations; i++)
        {
            var candidate = recurring.CronExpression is not null
                ? cronParser.GetNext(recurring.CronExpression, tz, cursor)
                : NextIntervalOccurrence(recurring, tz, cursor);

            if (candidate is null || candidate > horizon)
            {
                return null; // sem ocorrência possível no horizonte
            }

            if (recurring.NotAfter is { } notAfter && candidate > notAfter)
            {
                return null; // vigência encerrada
            }

            if (calendar is not null && IsExcluded(calendar, candidate.Value, tz))
            {
                cursor = candidate.Value;
                continue;
            }

            return candidate;
        }

        return null;
    }

    private DateTimeOffset? NextIntervalOccurrence(
        RecurringJobRecord recurring, TimeZoneInfo tz, DateTimeOffset cursor)
    {
        var interval = recurring.Interval!.Value;
        var anchor = recurring.NotBefore ?? recurring.CreatedAt;

        // Primeiro ponto da grade (anchor + k*intervalo) estritamente depois do cursor.
        var k = cursor < anchor
            ? 0
            : (long)Math.Floor((cursor - anchor) / interval) + 1;
        var candidate = anchor + interval * k;

        if (recurring.WindowStart is not { } start || recurring.WindowEnd is not { } end)
        {
            return candidate;
        }

        // Janela diária: fora dela, salta direto para o primeiro ponto da grade
        // dentro da próxima janela (sem varrer ponto a ponto).
        for (var i = 0; i < MaxIterations; i++)
        {
            var local = TimeZoneInfo.ConvertTime(candidate, tz);
            var timeOfDay = TimeOnly.FromTimeSpan(local.TimeOfDay);
            var insideWindow = start <= end
                ? timeOfDay >= start && timeOfDay <= end
                : timeOfDay >= start || timeOfDay <= end; // janela que cruza a meia-noite

            if (insideWindow)
            {
                return candidate;
            }

            var windowStartLocal = NextWindowStart(local.DateTime, timeOfDay, start);
            var windowStartAbsolute = new DateTimeOffset(
                windowStartLocal, tz.GetUtcOffset(windowStartLocal));

            k = windowStartAbsolute <= anchor
                ? 0
                : (long)Math.Ceiling((windowStartAbsolute - anchor) / interval);
            var next = anchor + interval * k;
            if (next <= candidate)
            {
                next = candidate + interval; // garante progresso mesmo com grades degeneradas
            }

            candidate = next;
        }

        return null;
    }

    private static DateTime NextWindowStart(DateTime local, TimeOnly timeOfDay, TimeOnly start)
    {
        var startToday = local.Date.Add(start.ToTimeSpan());
        return timeOfDay < start ? startToday : startToday.AddDays(1);
    }

    private bool IsExcluded(CalendarRecord calendar, DateTimeOffset occurrence, TimeZoneInfo tz)
    {
        // O fuso decide a data: converte antes de extrair dia/dia-da-semana.
        var local = TimeZoneInfo.ConvertTime(occurrence, tz);
        var date = DateOnly.FromDateTime(local.DateTime);

        if (calendar.ExcludedDates.Contains(date))
        {
            return true;
        }

        foreach (var range in calendar.ExcludedRanges)
        {
            if (date >= range.Start && date <= range.End)
            {
                return true;
            }
        }

        if (calendar.ExcludedDaysOfWeek.Contains(local.DayOfWeek))
        {
            return true;
        }

        foreach (var expression in calendar.ExcludedCronWindows)
        {
            // O minuto é excluído se a expressão o gera: o próximo disparo calculado
            // a partir do instante imediatamente anterior deve ser o próprio minuto.
            var minute = GuaraDatas.MinutoExato(occurrence);
            var generated = cronParser.GetNext(expression, tz, minute.AddTicks(-1));
            if (generated is { } g && g.UtcTicks == minute.UtcTicks)
            {
                return true;
            }
        }

        return false;
    }
}
