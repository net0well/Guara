namespace Guara.Throughput.Harness;

/// <summary>Resultado de uma rodada, já reduzido aos números que interessam.</summary>
internal sealed record RunResult(
    int Concurrency,
    int Jobs,
    TimeSpan EnqueueElapsed,
    TimeSpan EndToEndElapsed,
    double LatencyP50Ms,
    double LatencyP95Ms,
    double LatencyP99Ms,
    long AllocatedBytes)
{
    public double EnqueuePerSecond => Jobs / EnqueueElapsed.TotalSeconds;

    public double EndToEndPerSecond => Jobs / EndToEndElapsed.TotalSeconds;

    public double AllocatedPerJob => (double)AllocatedBytes / Jobs;

    /// <summary>
    /// Percentil por posição sobre a amostra ordenada. Sem interpolação de propósito: o
    /// valor reportado é uma latência que aconteceu de verdade, não uma média entre duas.
    /// </summary>
    public static double Percentil(List<double> ordenado, double fracao)
    {
        if (ordenado.Count == 0)
        {
            return 0;
        }

        var indice = (int)Math.Ceiling(fracao * ordenado.Count) - 1;
        return ordenado[Math.Clamp(indice, 0, ordenado.Count - 1)];
    }
}
