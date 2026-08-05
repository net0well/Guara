namespace Guara.Throughput.Harness;

/// <summary>
/// Conta os jobs da fase de drenagem e avisa quando o último termina.
/// <para>
/// Nasce desarmado com um total inaccessível de propósito: aquecimento e fase de latência
/// passam pelo mesmo handler, e sem isso os jobs deles derrubariam a contagem antes da
/// medição começar. <see cref="Armar"/> fixa o total quando a fase que interessa começa.
/// </para>
/// </summary>
internal sealed class DrainCounter
{
    private readonly TaskCompletionSource _pronto = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _restantes = int.MaxValue;

    /// <summary>Conclui quando o total armado foi atingido.</summary>
    public Task Concluido => _pronto.Task;

    /// <summary>Fixa quantos jobs a fase de drenagem espera.</summary>
    /// <param name="total">Quantidade de jobs da fase.</param>
    public void Armar(int total) => Interlocked.Exchange(ref _restantes, total);

    /// <summary>Registra a conclusão de um job.</summary>
    public void Registrar()
    {
        if (Interlocked.Decrement(ref _restantes) == 0)
        {
            _pronto.TrySetResult();
        }
    }
}
