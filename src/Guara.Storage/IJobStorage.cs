using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Persistência e transição de jobs. A operação central é
/// <see cref="AcquireNextDueAsync"/>: aquisição <b>atômica</b> do próximo job elegível
/// com lease (visibility timeout) — no máximo um consumidor obtém cada job, mesmo
/// entre múltiplos nós.
/// </summary>
public interface IJobStorage
{
    /// <summary>Persiste um job novo. Idempotente para o mesmo <see cref="JobRecord.Id"/>.</summary>
    /// <param name="record">Registro a persistir (estado inicial definido pelo chamador).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O id do job.</returns>
    ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct);

    /// <summary>
    /// Adquire atomicamente o próximo job elegível da fila: <c>Enqueued</c>, <c>Scheduled</c>
    /// vencido (<c>ScheduledFor &lt;= now</c>) ou <c>Processing</c> com lease expirado.
    /// O job adquirido passa a <c>Processing</c> com <c>LeaseUntil = now + lease</c>.
    /// </summary>
    /// <param name="queue">Fila a consumir.</param>
    /// <param name="lease">Duração da posse (renovável via <see cref="RenewLeaseAsync"/>).</param>
    /// <param name="now">Relógio do chamador (injetado — testável e consistente no cluster).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O job adquirido, ou <c>null</c> quando não há elegível.</returns>
    ValueTask<JobRecord?> AcquireNextDueAsync(string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Renova a posse de um job em execução. Retorna <c>false</c> quando a posse foi
    /// perdida (lease expirou e outro nó assumiu, ou o job não existe) — o worker deve
    /// abortar a execução local para evitar processamento duplo.
    /// </summary>
    /// <param name="id">Id do job.</param>
    /// <param name="lease">Nova duração da posse a partir de agora.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a posse foi renovada.</returns>
    ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// Atualiza o estado do job. Idempotente: reaplicar a mesma transição não corrompe
    /// o registro. Estados terminais limpam o lease.
    /// </summary>
    /// <param name="id">Id do job.</param>
    /// <param name="state">Novo estado.</param>
    /// <param name="resultOrError">Resultado serializado (sucesso) ou motivo (falha/retry).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o estado é persistido.</returns>
    ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct);

    /// <summary>Obtém um job pelo id.</summary>
    /// <param name="id">Id do job.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O registro, ou <c>null</c> se não existe.</returns>
    ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct);

    /// <summary>
    /// Exclui um job que <b>não</b> está em execução. Jobs <c>Processing</c> não são
    /// excluíveis (evita perder um trabalho em andamento) — cancele/aguarde antes.
    /// </summary>
    /// <param name="id">Id do job.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se excluído; <c>false</c> se não existe ou está <c>Processing</c>.</returns>
    ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct);

    /// <summary>
    /// Lista jobs para o dashboard. Sempre paginada; providers aplicam o teto
    /// <see cref="JobQuery.MaxPageSize"/>.
    /// </summary>
    /// <param name="query">Filtros e paginação.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A página de resultados, do mais recente para o mais antigo.</returns>
    ValueTask<IReadOnlyList<JobRecord>> ListAsync(JobQuery query, CancellationToken ct);
}
