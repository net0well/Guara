namespace Guara.Storage;

/// <summary>
/// Janela de agregação para os gráficos do dashboard. A agregação é feita no provider —
/// trazer os registros crus para somar em memória não escala.
/// </summary>
/// <param name="From">Início da janela, inclusivo.</param>
/// <param name="To">Fim da janela, exclusivo.</param>
/// <param name="Bucket">Largura de cada ponto da série.</param>
/// <param name="Queue">Restringe a uma fila, ou <c>null</c> para todas.</param>
public sealed record JobSeriesQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    TimeSpan Bucket,
    string? Queue = null)
{
    /// <summary>
    /// Teto de pontos por consulta. Sem ele, uma janela larga com balde estreito faria o
    /// provider varrer e devolver milhares de linhas por requisição de gráfico.
    /// </summary>
    public const int MaxPoints = 500;

    /// <summary>Valida a janela; chamado pelos providers antes de consultar.</summary>
    /// <exception cref="ArgumentException">Janela vazia/invertida, balde não positivo ou pontos demais.</exception>
    public void Validate()
    {
        if (To <= From)
        {
            throw new ArgumentException($"Janela inválida: To ({To:O}) precisa ser maior que From ({From:O}).");
        }

        if (Bucket <= TimeSpan.Zero)
        {
            throw new ArgumentException($"Balde inválido: {Bucket}. Informe um intervalo positivo.");
        }

        var points = (To - From).Ticks / Bucket.Ticks;
        if (points > MaxPoints)
        {
            throw new ArgumentException(
                $"A janela pedida geraria {points} pontos (teto: {MaxPoints}). Aumente o balde ou reduza a janela.");
        }
    }

    /// <summary>Instantes de início de cada balde da janela, em ordem crescente.</summary>
    /// <returns>Os inícios de balde que cobrem a janela.</returns>
    public IEnumerable<DateTimeOffset> Buckets()
    {
        for (var start = From; start < To; start += Bucket)
        {
            yield return start;
        }
    }
}

/// <summary>
/// Um ponto da série. As latências são o tempo decorrido entre a criação e o desfecho dos
/// jobs finalizados naquele balde, e ficam nulas quando nenhum job finalizou nele.
/// </summary>
/// <param name="Timestamp">Início do balde.</param>
/// <param name="Succeeded">Jobs concluídos com sucesso no balde.</param>
/// <param name="Failed">Jobs que falharam definitivamente no balde.</param>
/// <param name="LatencyP50">Mediana do tempo de vida dos jobs finalizados no balde.</param>
/// <param name="LatencyP95">Percentil 95 do mesmo tempo de vida.</param>
public sealed record JobSeriesPoint(
    DateTimeOffset Timestamp,
    long Succeeded,
    long Failed,
    TimeSpan? LatencyP50,
    TimeSpan? LatencyP95)
{
    /// <summary>Total de jobs finalizados no balde — o throughput do período.</summary>
    public long Total => Succeeded + Failed;
}

/// <summary>
/// Definição normativa dos percentis de latência. Fica no pacote de contratos porque todo
/// provider precisa devolver o mesmo número para a mesma amostra: é rank discreto (o valor
/// observado cujo acumulado alcança o percentil), o mesmo critério de
/// <c>percentile_disc</c> no SQL — nunca interpolação, que inventaria uma latência que
/// nenhum job teve.
/// </summary>
public static class JobLatency
{
    /// <summary>Percentil por rank discreto sobre uma amostra já ordenada.</summary>
    /// <param name="ordered">Amostra em ordem crescente.</param>
    /// <param name="percentile">Percentil desejado, entre 0 (exclusivo) e 1 (inclusivo).</param>
    /// <returns>O valor observado no percentil, ou <c>null</c> se a amostra está vazia.</returns>
    public static TimeSpan? Percentile(IReadOnlyList<TimeSpan> ordered, double percentile)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(percentile, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 1);

        if (ordered.Count == 0)
        {
            return null;
        }

        var rank = (int)Math.Ceiling(percentile * ordered.Count);
        return ordered[Math.Clamp(rank - 1, 0, ordered.Count - 1)];
    }
}
