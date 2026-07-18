namespace Guara.Abstractions;

/// <summary>
/// Calcula <b>quando</b> um job deve rodar a partir do seu <see cref="ScheduleDescriptor"/>.
/// Não executa, não busca e não persiste — apenas calcula.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Calcula a próxima ocorrência estritamente depois de <paramref name="after"/>.
    /// </summary>
    /// <param name="schedule">Descrição do agendamento (imediato, delay, cron ou recorrente).</param>
    /// <param name="after">Instante de referência (exclusivo para cron; base para delay).</param>
    /// <returns>O próximo disparo, ou <c>null</c> quando não há ocorrência futura.</returns>
    DateTimeOffset? GetNextOccurrence(ScheduleDescriptor schedule, DateTimeOffset after);
}
