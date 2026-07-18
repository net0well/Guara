namespace Guara.Scheduler;

/// <summary>Resolução de fusos aceitando ids IANA e Windows nos dois sistemas.</summary>
internal static class TimeZones
{
    /// <summary>Resolve um id de fuso; nulo/vazio = UTC.</summary>
    /// <param name="timeZoneId">Id IANA (<c>America/Sao_Paulo</c>) ou Windows.</param>
    /// <returns>O fuso resolvido.</returns>
    /// <exception cref="TimeZoneNotFoundException">Fuso desconhecido, com mensagem orientando o formato.</exception>
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
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
