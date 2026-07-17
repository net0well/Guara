namespace Guara.Abstractions;

/// <summary>
/// A API pública de operação de jobs do Guará — métodos em <b>português</b> (ADR-0010).
/// Injete em qualquer serviço para enfileirar, agendar e excluir jobs.
/// Métodos de recorrentes/calendários (builder fluente) entram como adições
/// extend-only junto da implementação da spec 038.
/// </summary>
public interface IGuaraClient
{
    /// <summary>Enfileira um job para execução imediata (fire-and-forget).</summary>
    /// <param name="job">Descrição do job.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O id do job criado.</returns>
    ValueTask<JobId> EnfileirarAsync(JobDescriptor job, CancellationToken ct = default);

    /// <summary>Agenda um job para rodar uma vez, após um atraso.</summary>
    /// <param name="job">Descrição do job.</param>
    /// <param name="atraso">Quanto tempo esperar antes de executar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O id do job criado.</returns>
    ValueTask<JobId> AgendarAsync(JobDescriptor job, TimeSpan atraso, CancellationToken ct = default);

    /// <summary>
    /// Exclui um job que ainda não está em execução.
    /// </summary>
    /// <param name="id">Id do job.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o job foi excluído; <c>false</c> se não existe ou está em execução.</returns>
    ValueTask<bool> ExcluirAsync(JobId id, CancellationToken ct = default);
}
