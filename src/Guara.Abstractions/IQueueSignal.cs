namespace Guara.Abstractions;

/// <summary>
/// Aviso de que uma fila tem trabalho elegível <b>agora</b>. Existe para o dispatcher
/// acordar no instante em que o job entra, em vez de esperar o próximo ciclo de busca.
/// <para>
/// O aviso é <b>best-effort</b>: perdê-lo atrasa a busca até o ciclo periódico, nunca
/// perde o job. É essa garantia que permite descartar sinais sob pressão e isolar falha
/// de transporte sem transação entre persistir o job e avisar.
/// </para>
/// <para>
/// Só se sinaliza o que já é elegível. Retentativa, reagendamento e continuação pendente
/// têm data futura — avisar acordaria o dispatcher para não achar nada.
/// </para>
/// </summary>
public interface IQueueSignal
{
    /// <summary>
    /// Avisa que <paramref name="queue"/> tem trabalho elegível agora. Não falha por
    /// indisponibilidade do transporte: quem enfileira não pode quebrar porque o aviso
    /// não saiu.
    /// </summary>
    /// <param name="queue">Fila que recebeu trabalho.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o aviso foi emitido.</returns>
    ValueTask SignalAsync(string queue, CancellationToken ct);

    /// <summary>
    /// Aguarda um aviso para qualquer uma das <paramref name="queues"/>, no máximo por
    /// <paramref name="timeout"/>.
    /// <para>
    /// Um aviso emitido enquanto ninguém aguardava é <b>retido</b> e satisfaz a próxima
    /// espera. Sem essa retenção, o job que entra entre a última busca e o início da
    /// espera ficaria parado até o timeout — a corrida mais provável aqui, já que as duas
    /// coisas acontecem em sequência imediata.
    /// </para>
    /// </summary>
    /// <param name="queues">Filas de interesse; avisos de outras filas não acordam.</param>
    /// <param name="timeout">Teto da espera.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> quando um aviso chegou; <c>false</c> quando o tempo esgotou.</returns>
    /// <exception cref="OperationCanceledException">Quando <paramref name="ct"/> é cancelado.</exception>
    ValueTask<bool> WaitAsync(IReadOnlyList<string> queues, TimeSpan timeout, CancellationToken ct);
}
