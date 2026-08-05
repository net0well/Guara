using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Scheduler;

/// <summary>
/// Resolve continuações quando o pai atinge um estado final: gatilho atendido
/// enfileira o filho; não atendido descarta a cadeia inteira, registrando o motivo.
/// A resolução do vínculo é única entre nós (<see cref="IContinuationStorage.TryResolveAsync"/>);
/// a varredura cobre quedas entre persistir o final do pai e promover.
/// </summary>
internal sealed class ContinuationPromoter(
    IStorage storage, IQueueSignal signal, TimeProvider time, ILogger<ContinuationPromoter> logger)
{
    /// <summary>Avalia todas as continuações de um pai que atingiu um estado final.</summary>
    /// <param name="parentId">Id do job pai.</param>
    /// <param name="parentFinalState">Estado final atingido pelo pai.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando todas foram avaliadas.</returns>
    public async ValueTask PromoteAsync(JobId parentId, JobState parentFinalState, CancellationToken ct)
    {
        foreach (var continuation in await storage.Continuations.ListByParentAsync(parentId, ct))
        {
            await EvaluateAsync(continuation, parentFinalState, ct);
        }
    }

    /// <summary>
    /// Descarta as continuações pendentes de um pai, em cascata: descendentes de um
    /// filho descartado nunca vão disparar e são descartados junto.
    /// </summary>
    /// <param name="parentId">Id do pai cuja cadeia pendente deve ser descartada.</param>
    /// <param name="reason">Motivo registrado em cada vínculo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a cadeia foi descartada.</returns>
    public async ValueTask DiscardPendingAsync(JobId parentId, string reason, CancellationToken ct)
    {
        var parents = new Stack<JobId>();
        parents.Push(parentId);

        while (parents.Count > 0)
        {
            var current = parents.Pop();
            foreach (var continuation in await storage.Continuations.ListByParentAsync(current, ct))
            {
                if (await DiscardAsync(continuation, reason, ct))
                {
                    parents.Push(continuation.ChildId);
                }
            }
        }
    }

    /// <summary>
    /// Varredura de recuperação: resolve vínculos pendentes cujo pai já finalizou
    /// (queda entre persistir o final e promover) ou não existe mais.
    /// </summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a varredura termina.</returns>
    public async ValueTask SweepAsync(CancellationToken ct)
    {
        foreach (var pending in await storage.Continuations.ListPendingAsync(ct))
        {
            var parent = await storage.Jobs.GetAsync(pending.ParentId, ct);
            if (parent is null)
            {
                const string reason = "O job pai não existe mais.";
                if (await DiscardAsync(pending, reason, ct))
                {
                    await DiscardPendingAsync(pending.ChildId, reason, ct);
                }
            }
            else if (parent.State is JobState.Succeeded or JobState.Failed)
            {
                await EvaluateAsync(pending, parent.State, ct);
            }
        }
    }

    /// <summary>Avalia um vínculo pendente contra o estado final do pai.</summary>
    /// <param name="continuation">Vínculo a avaliar.</param>
    /// <param name="parentFinalState">Estado final do pai.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o vínculo foi resolvido.</returns>
    internal async ValueTask EvaluateAsync(
        ContinuationRecord continuation, JobState parentFinalState, CancellationToken ct)
    {
        if (continuation.Status != ContinuationStatus.Pending)
        {
            return;
        }

        if (parentFinalState == JobState.Succeeded || continuation.Trigger == ContinuationTrigger.OnAnyFinishedState)
        {
            // Filho primeiro, resolução depois: reaplicar a transição é inócuo (mesmo
            // registro) e uma queda entre os passos é recuperada pela varredura.
            await storage.Jobs.UpdateStateAsync(continuation.ChildId, JobState.Enqueued, null, ct);
            if (await storage.Continuations.TryResolveAsync(
                    continuation.ChildId, ContinuationStatus.Enqueued, null, time.GetUtcNow(), ct))
            {
                logger.LogInformation(
                    "Continuação {ChildId} enfileirada: o pai {ParentId} finalizou como {ParentState}",
                    continuation.ChildId.Value, continuation.ParentId.Value, parentFinalState);

                // A leitura do filho existe só para descobrir a fila do aviso, e acontece
                // apenas aqui: a resolução é única entre nós, então quem não promoveu
                // não paga a consulta nem avisa em duplicidade.
                if (await storage.Jobs.GetAsync(continuation.ChildId, ct) is { } child)
                {
                    await signal.SignalSafelyAsync(child.Queue, logger, ct);
                }
            }
        }
        else
        {
            const string reason = "O job pai falhou e o gatilho dispara apenas em sucesso.";
            if (await DiscardAsync(continuation, reason, ct))
            {
                await DiscardPendingAsync(continuation.ChildId, reason, ct);
            }
        }
    }

    private async ValueTask<bool> DiscardAsync(ContinuationRecord continuation, string reason, CancellationToken ct)
    {
        if (continuation.Status != ContinuationStatus.Pending)
        {
            return false;
        }

        // O job filho some; o vínculo permanece como registro do descarte. A ordem
        // (excluir, depois resolver) deixa qualquer queda no meio para a varredura.
        await storage.Jobs.DeleteAsync(continuation.ChildId, ct);
        if (!await storage.Continuations.TryResolveAsync(
                continuation.ChildId, ContinuationStatus.Discarded, reason, time.GetUtcNow(), ct))
        {
            return false;
        }

        logger.LogWarning(
            "Continuação {ChildId} descartada: {Reason}", continuation.ChildId.Value, reason);
        return true;
    }
}
