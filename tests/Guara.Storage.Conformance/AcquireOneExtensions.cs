using Guara.Storage;

namespace Guara.Storage.Conformance;

/// <summary>
/// Conveniência do kit: a maior parte dos casos exercita a semântica de <b>um</b> job —
/// posse, lease, exclusividade — e não a do lote. Pedir <c>max: 1</c> em cada chamada
/// afogaria a intenção do teste em ruído.
/// </summary>
internal static class AcquireOneExtensions
{
    /// <summary>Adquire no máximo um job elegível.</summary>
    /// <param name="jobs">Storage sob teste.</param>
    /// <param name="queue">Fila a consumir.</param>
    /// <param name="lease">Duração da posse.</param>
    /// <param name="now">Relógio do chamador.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O job adquirido, ou <c>null</c> quando não há elegível.</returns>
    public static async ValueTask<JobRecord?> AcquireNextDueAsync(
        this IJobStorage jobs, string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
        => (await jobs.AcquireNextDueAsync(queue, 1, lease, now, ct)).FirstOrDefault();
}
