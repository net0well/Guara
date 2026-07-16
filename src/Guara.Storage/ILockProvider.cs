namespace Guara.Storage;

/// <summary>
/// Locks com TTL. Base para deduplicação, manutenção exclusiva e eleição de líder
/// (Cluster). O TTL garante liberação mesmo em crash do dono; verifique
/// <see cref="StorageCapabilities.SupportsDistributedLock"/> para saber se o lock
/// vale entre nós.
/// </summary>
public interface ILockProvider
{
    /// <summary>Tenta adquirir um lock exclusivo.</summary>
    /// <param name="key">Chave do lock.</param>
    /// <param name="ttl">Validade da posse; expira sozinha se não renovada.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O handle da posse, ou <c>null</c> se o lock já pertence a outro dono.</returns>
    ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}

/// <summary>
/// Posse de um lock. Descartar (<see cref="IAsyncDisposable.DisposeAsync"/>) libera o
/// lock; a liberação é segura (só o dono consegue liberar/renovar).
/// </summary>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>Chave do lock.</summary>
    string Key { get; }

    /// <summary>
    /// Estende a posse por mais <paramref name="ttl"/>. Retorna <c>false</c> quando a
    /// posse foi perdida (expirou e outro dono assumiu) — o chamador deve ceder (fail-safe).
    /// </summary>
    /// <param name="ttl">Nova validade a partir de agora.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a posse foi renovada.</returns>
    ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct);
}
