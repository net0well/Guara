namespace Guara.Scheduler;

/// <summary>
/// Construtor de datas de disparo — açúcar para compor com <c>IniciaEm</c>/<c>AgendarAsync</c>.
/// Métodos relativos ("hoje", "amanhã") usam o relógio do sistema no fuso informado (UTC quando omitido).
/// </summary>
public static class GuaraDatas
{
    /// <summary>Trunca o instante no segundo exato (remove milissegundos).</summary>
    /// <param name="instante">Instante de referência.</param>
    /// <returns>O instante sem a fração de segundo.</returns>
    public static DateTimeOffset SegundoExato(DateTimeOffset instante)
        => instante.AddTicks(-(instante.Ticks % TimeSpan.TicksPerSecond));

    /// <summary>Trunca o instante no minuto exato.</summary>
    /// <param name="instante">Instante de referência.</param>
    /// <returns>O instante sem segundos nem frações.</returns>
    public static DateTimeOffset MinutoExato(DateTimeOffset instante)
        => instante.AddTicks(-(instante.Ticks % TimeSpan.TicksPerMinute));

    /// <summary>Trunca o instante na hora exata.</summary>
    /// <param name="instante">Instante de referência.</param>
    /// <returns>O instante no início da hora.</returns>
    public static DateTimeOffset HoraExata(DateTimeOffset instante)
        => instante.AddTicks(-(instante.Ticks % TimeSpan.TicksPerHour));

    /// <summary>Hoje no horário indicado (pode já ter passado — combine com a semântica de vigência).</summary>
    /// <param name="hora">Hora local (0–23).</param>
    /// <param name="minuto">Minuto (0–59).</param>
    /// <param name="fuso">Fuso de referência; UTC quando omitido.</param>
    /// <returns>O instante de hoje às <paramref name="hora"/>:<paramref name="minuto"/> no fuso.</returns>
    public static DateTimeOffset HojeAs(int hora, int minuto = 0, TimeZoneInfo? fuso = null)
        => DiaAs(0, hora, minuto, fuso);

    /// <summary>Amanhã no horário indicado.</summary>
    /// <param name="hora">Hora local (0–23).</param>
    /// <param name="minuto">Minuto (0–59).</param>
    /// <param name="fuso">Fuso de referência; UTC quando omitido.</param>
    /// <returns>O instante de amanhã às <paramref name="hora"/>:<paramref name="minuto"/> no fuso.</returns>
    public static DateTimeOffset AmanhaAs(int hora, int minuto = 0, TimeZoneInfo? fuso = null)
        => DiaAs(1, hora, minuto, fuso);

    /// <summary>Próximo dia útil (segunda a sexta, a partir de amanhã), à meia-noite local.</summary>
    /// <param name="fuso">Fuso de referência; UTC quando omitido.</param>
    /// <returns>A meia-noite do próximo dia útil no fuso.</returns>
    public static DateTimeOffset ProximoDiaUtil(TimeZoneInfo? fuso = null)
    {
        var tz = fuso ?? TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), tz).Date.AddDays(1);
        while (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            local = local.AddDays(1);
        }

        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static DateTimeOffset DiaAs(int diasAFrente, int hora, int minuto, TimeZoneInfo? fuso)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hora);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hora, 23);
        ArgumentOutOfRangeException.ThrowIfNegative(minuto);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minuto, 59);

        var tz = fuso ?? TimeZoneInfo.Utc;
        var localDate = TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), tz).Date.AddDays(diasAFrente);
        var local = localDate.AddHours(hora).AddMinutes(minuto);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }
}
