namespace Guara.Abstractions;

/// <summary>
/// A API pública de operação de jobs do Guará — métodos em <b>português</b>.
/// Injete em qualquer serviço para enfileirar, agendar e excluir jobs, além de
/// gerenciar recorrentes e calendários de exclusão.
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
    /// Exclui um job que ainda não está em execução. Continuações pendentes do job
    /// excluído são descartadas e registradas — nunca disparadas.
    /// </summary>
    /// <param name="id">Id do job.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o job foi excluído; <c>false</c> se não existe ou está em execução.</returns>
    ValueTask<bool> ExcluirAsync(JobId id, CancellationToken ct = default);

    /// <summary>
    /// Registra um job filho que dispara automaticamente quando o pai atinge o estado
    /// final do gatilho (sucesso por padrão). O filho aguarda sem consumir workers;
    /// pai já finalizado no registro é avaliado imediatamente.
    /// </summary>
    /// <param name="paiId">Id do job pai.</param>
    /// <param name="filho">Descrição do job filho.</param>
    /// <param name="opcoes">Opções da continuação (gatilho).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O id do job filho criado.</returns>
    ValueTask<JobId> ContinuarComAsync(
        JobId paiId, JobDescriptor filho, ContinuationOptions? opcoes = null, CancellationToken ct = default);

    /// <summary>
    /// Cria ou atualiza um job recorrente (upsert pelo <c>ComId</c>). Atualizar recalcula
    /// o próximo disparo a partir de agora com a nova agenda.
    /// </summary>
    /// <param name="configurar">Configuração fluente do recorrente.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a definição está persistida.</returns>
    ValueTask AdicionarOuAtualizarRecorrenteAsync(Action<IRecurringJobBuilder> configurar, CancellationToken ct = default);

    /// <summary>Forma posicional de conveniência para recorrentes por cron.</summary>
    /// <param name="id">Id estável da definição.</param>
    /// <param name="job">O que executar.</param>
    /// <param name="cron">Expressão cron de 5 campos.</param>
    /// <param name="fuso">Fuso da agenda.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a definição está persistida.</returns>
    ValueTask AdicionarOuAtualizarRecorrenteAsync(
        string id, JobDescriptor job, string cron, TimeZoneInfo fuso, CancellationToken ct = default);

    /// <summary>Remove uma definição recorrente (ocorrências já enfileiradas não são afetadas).</summary>
    /// <param name="id">Id da definição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a definição existia e foi removida.</returns>
    ValueTask<bool> ExcluirRecorrenteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Suspende as ocorrências automáticas de um recorrente. Ocorrências já enfileiradas
    /// seguem seu curso. Idempotente: pausar o que já está pausado não muda nada.
    /// </summary>
    /// <param name="id">Id da definição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a definição existe (e ficou pausada); <c>false</c> se não existe.</returns>
    ValueTask<bool> PausarRecorrenteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Volta a promover ocorrências de um recorrente pausado. O período pausado
    /// <b>não</b> é recuperado: o próximo disparo é recalculado a partir de agora, então
    /// retomar nunca dispara de uma vez tudo o que deixou de rodar.
    /// </summary>
    /// <param name="id">Id da definição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a definição existe (e ficou ativa); <c>false</c> se não existe.</returns>
    ValueTask<bool> RetomarRecorrenteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Enfileira uma ocorrência do recorrente imediatamente, sem mexer na agenda: o
    /// próximo disparo automático continua onde estava. Funciona mesmo pausado — é uma
    /// execução manual, não a retomada da agenda.
    /// </summary>
    /// <param name="id">Id da definição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O id do job criado, ou <c>null</c> se a definição não existe.</returns>
    ValueTask<JobId?> DispararRecorrenteAgoraAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Cria ou atualiza um calendário de exclusões. Ao substituir um calendário existente,
    /// os recorrentes que o usam têm o próximo disparo recalculado automaticamente.
    /// </summary>
    /// <param name="nome">Nome único do calendário.</param>
    /// <param name="configurar">Configuração fluente das exclusões.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o calendário está persistido.</returns>
    ValueTask AdicionarOuAtualizarCalendarioAsync(
        string nome, Action<ICalendarBuilder> configurar, CancellationToken ct = default);

    /// <summary>
    /// Remove um calendário. Falha se algum recorrente o usa (a mensagem lista quais).
    /// </summary>
    /// <param name="nome">Nome do calendário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o calendário existia e foi removido.</returns>
    ValueTask<bool> ExcluirCalendarioAsync(string nome, CancellationToken ct = default);
}
