using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Persistência dos vínculos de continuação (pai→filho). A resolução é o ponto de
/// idempotência do disparo: <see cref="TryResolveAsync"/> só transiciona uma vez a
/// partir de <see cref="ContinuationStatus.Pending"/> — entre nós concorrentes,
/// apenas um vence.
/// </summary>
public interface IContinuationStorage
{
    /// <summary>Registra um vínculo. Idempotente para o mesmo <see cref="ContinuationRecord.ChildId"/>.</summary>
    /// <param name="record">Vínculo completo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o vínculo está persistido.</returns>
    ValueTask AddAsync(ContinuationRecord record, CancellationToken ct);

    /// <summary>Obtém o vínculo em que o job é o filho.</summary>
    /// <param name="childId">Id do job filho.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O vínculo, ou <c>null</c> se o job não é uma continuação.</returns>
    ValueTask<ContinuationRecord?> GetByChildAsync(JobId childId, CancellationToken ct);

    /// <summary>Lista os vínculos de um pai (fan-out em uma consulta, sem N+1).</summary>
    /// <param name="parentId">Id do job pai.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Os vínculos registrados para o pai.</returns>
    ValueTask<IReadOnlyList<ContinuationRecord>> ListByParentAsync(JobId parentId, CancellationToken ct);

    /// <summary>
    /// Lista os vínculos pendentes que a varredura de recuperação consegue resolver: os que
    /// já têm um pai finalizado, e os cujo pai não existe mais.
    /// <para>
    /// Traz o desfecho do pai junto de propósito. Listar só os pendentes obrigaria a
    /// varredura a ler o pai de cada um, e pendente é o estado <b>normal</b> de uma
    /// continuação enquanto o pai não termina — o custo cresceria com a fila inteira, a cada
    /// ciclo de manutenção, para descobrir que quase nada mudou.
    /// </para>
    /// </summary>
    /// <param name="max">Teto de vínculos por chamada; o restante fica para o próximo ciclo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Os vínculos resolvíveis, com o desfecho do pai.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Quando <paramref name="max"/> não é positivo.</exception>
    ValueTask<IReadOnlyList<ResolvableContinuation>> ListResolvablePendingAsync(
        int max, CancellationToken ct);

    /// <summary>
    /// Resolve um vínculo pendente (disparo ou descarte), atomicamente e uma única vez.
    /// </summary>
    /// <param name="childId">Id do job filho.</param>
    /// <param name="status">Desfecho (<see cref="ContinuationStatus.Enqueued"/> ou <see cref="ContinuationStatus.Discarded"/>).</param>
    /// <param name="reason">Motivo, quando descarte.</param>
    /// <param name="resolvedAt">Instante da resolução.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se este chamador resolveu; <c>false</c> se já estava resolvido ou não existe.</returns>
    ValueTask<bool> TryResolveAsync(
        JobId childId, ContinuationStatus status, string? reason, DateTimeOffset resolvedAt, CancellationToken ct);
}
