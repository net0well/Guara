using Guara.Abstractions;
using Microsoft.Extensions.Logging;

namespace Guara.Scheduler;

/// <summary>Aviso de fila que não derruba quem o emite.</summary>
internal static class QueueSignalExtensions
{
    /// <summary>
    /// Avisa a fila engolindo qualquer falha do transporte. O aviso é uma otimização de
    /// latência emitida <b>depois</b> de o job estar persistido: propagar o erro trocaria
    /// um despacho alguns segundos mais lento por uma operação que falha para o usuário.
    /// Sem o aviso, o dispatcher encontra o job no ciclo periódico.
    /// </summary>
    /// <param name="signal">Sinal de fila configurado.</param>
    /// <param name="queue">Fila que recebeu trabalho elegível agora.</param>
    /// <param name="logger">Registro do aviso perdido — engolir sem rastro tornaria um
    /// transporte quebrado invisível em produção.</param>
    /// <param name="ct">Token de cancelamento; cancelamento continua propagando.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a tentativa termina.</returns>
    public static async ValueTask SignalSafelyAsync(
        this IQueueSignal signal, string queue, ILogger logger, CancellationToken ct)
    {
        try
        {
            await signal.SignalAsync(queue, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Não foi possível avisar a fila {Queue}; o dispatcher encontra o job no próximo ciclo", queue);
        }
    }
}
