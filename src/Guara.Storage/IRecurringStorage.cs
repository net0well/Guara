namespace Guara.Storage;

/// <summary>
/// Persistência de definições recorrentes e calendários. Definições são upserts
/// pela chave (<see cref="RecurringJobRecord.Id"/>/<see cref="CalendarRecord.Name"/>);
/// as ocorrências promovidas viram jobs comuns no <see cref="IJobStorage"/>.
/// </summary>
public interface IRecurringStorage
{
    /// <summary>Cria ou substitui uma definição recorrente.</summary>
    /// <param name="record">Definição completa.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a definição está persistida.</returns>
    ValueTask UpsertAsync(RecurringJobRecord record, CancellationToken ct);

    /// <summary>Obtém uma definição pelo id.</summary>
    /// <param name="id">Id da definição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A definição, ou <c>null</c> se não existe.</returns>
    ValueTask<RecurringJobRecord?> GetAsync(string id, CancellationToken ct);

    /// <summary>Remove uma definição (ocorrências já enfileiradas não são afetadas).</summary>
    /// <param name="id">Id da definição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a definição existia e foi removida.</returns>
    ValueTask<bool> DeleteAsync(string id, CancellationToken ct);

    /// <summary>Lista todas as definições.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>As definições registradas.</returns>
    ValueTask<IReadOnlyList<RecurringJobRecord>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Lista definições com disparo vencido (<see cref="RecurringJobRecord.NextRunAt"/>
    /// &lt;= <paramref name="now"/>), ativas e não pausadas, em ordem de vencimento.
    /// </summary>
    /// <param name="now">Relógio do chamador.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Definições prontas para promover uma ocorrência.</returns>
    ValueTask<IReadOnlyList<RecurringJobRecord>> ListDueAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Cria ou substitui um calendário.</summary>
    /// <param name="calendar">Calendário completo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o calendário está persistido.</returns>
    ValueTask UpsertCalendarAsync(CalendarRecord calendar, CancellationToken ct);

    /// <summary>Obtém um calendário pelo nome.</summary>
    /// <param name="name">Nome do calendário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O calendário, ou <c>null</c> se não existe.</returns>
    ValueTask<CalendarRecord?> GetCalendarAsync(string name, CancellationToken ct);

    /// <summary>Remove um calendário (a checagem de uso é do chamador).</summary>
    /// <param name="name">Nome do calendário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o calendário existia e foi removido.</returns>
    ValueTask<bool> DeleteCalendarAsync(string name, CancellationToken ct);

    /// <summary>Lista todos os calendários.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Os calendários registrados.</returns>
    ValueTask<IReadOnlyList<CalendarRecord>> ListCalendarsAsync(CancellationToken ct);
}
