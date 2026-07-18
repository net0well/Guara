using System.Globalization;

namespace Guara.Scheduler;

/// <summary>
/// Expressão cron de 5 campos (<c>minuto hora dia-do-mês mês dia-da-semana</c>) com
/// implementação <b>própria</b> — sem bibliotecas de terceiros.
/// Suporta <c>*</c>, valores, listas (<c>,</c>), intervalos (<c>-</c>), passos (<c>/</c>)
/// e nomes (<c>JAN..DEC</c>, <c>SUN..SAT</c>); <c>7</c> equivale a domingo.
/// Dia-do-mês e dia-da-semana ambos restritos seguem a regra clássica do cron (OU).
/// </summary>
/// <remarks>
/// Fuso horário/DST: ocorrências que caem num horário local <b>inexistente</b>
/// (início do horário de verão) disparam no primeiro instante válido após a transição;
/// horários <b>ambíguos</b> (fim do horário de verão) usam a primeira ocorrência.
/// Um campo é considerado "restrito" quando seu texto não é exatamente <c>*</c>.
/// </remarks>
public sealed class CronExpression
{
    private const int HorizonYears = 5;

    private static readonly string[] MonthNames =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static readonly string[] DayOfWeekNames =
        ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private readonly ulong _minutes;
    private readonly ulong _hours;
    private readonly ulong _daysOfMonth;
    private readonly ulong _months;
    private readonly ulong _daysOfWeek;
    private readonly bool _dayOfMonthRestricted;
    private readonly bool _dayOfWeekRestricted;
    private readonly string _original;

    private CronExpression(
        ulong minutes, ulong hours, ulong daysOfMonth, ulong months, ulong daysOfWeek,
        bool dayOfMonthRestricted, bool dayOfWeekRestricted, string original)
    {
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _dayOfMonthRestricted = dayOfMonthRestricted;
        _dayOfWeekRestricted = dayOfWeekRestricted;
        _original = original;
    }

    /// <summary>Interpreta uma expressão cron de 5 campos.</summary>
    /// <param name="expression">Expressão (ex.: <c>"0 3 * * MON"</c>).</param>
    /// <returns>A expressão interpretada.</returns>
    /// <exception cref="FormatException">Expressão inválida — a mensagem indica o campo e o motivo.</exception>
    public static CronExpression Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var fields = expression.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException(
                $"Expressão cron inválida '{expression}': esperados 5 campos " +
                $"(minuto hora dia-do-mês mês dia-da-semana), encontrados {fields.Length}.");
        }

        var minutes = ParseField(fields[0], 0, 59, null, 0, "minuto", expression);
        var hours = ParseField(fields[1], 0, 23, null, 0, "hora", expression);
        var daysOfMonth = ParseField(fields[2], 1, 31, null, 0, "dia-do-mês", expression);
        var months = ParseField(fields[3], 1, 12, MonthNames, 1, "mês", expression);

        // dia-da-semana aceita 0..7 (7 = domingo); dobra o bit 7 no 0 após o parse
        var dowRaw = ParseField(fields[4], 0, 7, DayOfWeekNames, 0, "dia-da-semana", expression);
        var daysOfWeek = (dowRaw & 0x7FUL) | ((dowRaw >> 7) & 1UL);

        return new CronExpression(
            minutes, hours, daysOfMonth, months, daysOfWeek,
            dayOfMonthRestricted: fields[2] != "*",
            dayOfWeekRestricted: fields[4] != "*",
            expression);
    }

    /// <summary>Tenta interpretar uma expressão cron.</summary>
    /// <param name="expression">Expressão candidata.</param>
    /// <param name="result">A expressão interpretada, quando válida.</param>
    /// <returns><c>true</c> quando a expressão é válida.</returns>
    public static bool TryParse(string expression, out CronExpression? result)
    {
        try
        {
            result = Parse(expression);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Calcula a próxima ocorrência <b>estritamente depois</b> de <paramref name="after"/>,
    /// no fuso <paramref name="timeZone"/>.
    /// </summary>
    /// <param name="after">Instante de referência (exclusivo).</param>
    /// <param name="timeZone">Fuso horário em que a expressão é avaliada.</param>
    /// <returns>A próxima ocorrência, ou <c>null</c> se não houver nos próximos 5 anos.</returns>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localAfter = TimeZoneInfo.ConvertTime(after, timeZone).DateTime;
        var candidate = new DateTime(
            localAfter.Year, localAfter.Month, localAfter.Day,
            localAfter.Hour, localAfter.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        var horizon = candidate.AddYears(HorizonYears);

        while (candidate <= horizon)
        {
            if (!IsSet(_months, candidate.Month))
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1).AddMonths(1);
                continue;
            }

            if (!DayMatches(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!IsSet(_hours, candidate.Hour))
            {
                candidate = new DateTime(
                    candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0).AddHours(1);
                continue;
            }

            if (!IsSet(_minutes, candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return Resolve(candidate, timeZone);
        }

        return null; // sem ocorrência no horizonte (ex.: "0 0 30 2 *")
    }

    /// <inheritdoc />
    public override string ToString() => _original;

    private bool DayMatches(DateTime date)
    {
        var dom = IsSet(_daysOfMonth, date.Day);
        var dow = IsSet(_daysOfWeek, (int)date.DayOfWeek);

        return (_dayOfMonthRestricted, _dayOfWeekRestricted) switch
        {
            (true, true) => dom || dow, // regra clássica do cron: ambos restritos = OU
            (true, false) => dom,
            (false, true) => dow,
            _ => true,
        };
    }

    private static DateTimeOffset Resolve(DateTime local, TimeZoneInfo tz)
    {
        if (tz.IsInvalidTime(local))
        {
            // Horário pulado (início do horário de verão): dispara no primeiro instante válido.
            var probe = local;
            do
            {
                probe = probe.AddMinutes(1);
            }
            while (tz.IsInvalidTime(probe));

            return new DateTimeOffset(probe, tz.GetUtcOffset(probe));
        }

        if (tz.IsAmbiguousTime(local))
        {
            // Horário ambíguo (fim do horário de verão): primeira ocorrência —
            // maior offset = instante UTC mais cedo.
            var offsets = tz.GetAmbiguousTimeOffsets(local);
            var offset = offsets[0];
            for (var i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] > offset)
                {
                    offset = offsets[i];
                }
            }

            return new DateTimeOffset(local, offset);
        }

        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static bool IsSet(ulong mask, int bit) => (mask & (1UL << bit)) != 0;

    private static ulong ParseField(
        string text, int min, int max, string[]? names, int nameOffset, string fieldName, string expression)
    {
        ulong mask = 0;
        foreach (var segment in text.Split(','))
        {
            mask |= ParseSegment(segment, min, max, names, nameOffset, fieldName, expression);
        }

        return mask;
    }

    private static ulong ParseSegment(
        string segment, int min, int max, string[]? names, int nameOffset, string fieldName, string expression)
    {
        if (segment.Length == 0)
        {
            throw Invalid(expression, fieldName, "segmento vazio");
        }

        var step = 1;
        var range = segment;

        var slash = segment.IndexOf('/');
        if (slash >= 0)
        {
            range = segment[..slash];
            var stepText = segment[(slash + 1)..];
            if (!int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
            {
                throw Invalid(expression, fieldName, $"passo inválido '{stepText}'");
            }
        }

        int start, end;
        if (range == "*")
        {
            start = min;
            end = max;
        }
        else
        {
            var dash = range.IndexOf('-');
            if (dash >= 0)
            {
                start = ParseValue(range[..dash], names, nameOffset, fieldName, expression);
                end = ParseValue(range[(dash + 1)..], names, nameOffset, fieldName, expression);
            }
            else
            {
                start = ParseValue(range, names, nameOffset, fieldName, expression);
                end = slash >= 0 ? max : start; // "a/s" = de a até o máximo, de s em s
            }
        }

        if (start < min || end > max)
        {
            throw Invalid(expression, fieldName, $"valor fora do intervalo {min}-{max}");
        }

        if (start > end)
        {
            throw Invalid(expression, fieldName, $"intervalo invertido '{range}'");
        }

        ulong mask = 0;
        for (var value = start; value <= end; value += step)
        {
            mask |= 1UL << value;
        }

        return mask;
    }

    private static int ParseValue(
        string token, string[]? names, int nameOffset, string fieldName, string expression)
    {
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        if (names is not null)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i].Equals(token, StringComparison.OrdinalIgnoreCase))
                {
                    return i + nameOffset;
                }
            }
        }

        throw Invalid(expression, fieldName, $"valor inválido '{token}'");
    }

    private static FormatException Invalid(string expression, string fieldName, string reason)
        => new($"Expressão cron inválida '{expression}': campo {fieldName} — {reason}.");
}
