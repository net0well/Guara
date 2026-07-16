using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>
/// Locks com TTL <b>locais ao processo</b> (<c>SupportsDistributedLock = false</c>).
/// Posse identificada por token: só o dono renova/libera; TTL expira sozinho (fail-safe).
/// </summary>
internal sealed class MemoryLockProvider(TimeProvider time) : ILockProvider
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (Guid Token, DateTimeOffset Expires)> _locks = new(StringComparer.Ordinal);

    public ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        lock (_sync)
        {
            var now = time.GetUtcNow();
            if (_locks.TryGetValue(key, out var existing) && existing.Expires > now)
            {
                return ValueTask.FromResult<ILockHandle?>(null); // pertence a outro dono
            }

            var token = Guid.NewGuid();
            _locks[key] = (token, now + ttl);
            return ValueTask.FromResult<ILockHandle?>(new MemoryLockHandle(this, key, token));
        }
    }

    private bool Renew(string key, Guid token, TimeSpan ttl)
    {
        lock (_sync)
        {
            if (!_locks.TryGetValue(key, out var entry) || entry.Token != token)
            {
                return false; // posse perdida — o chamador deve ceder
            }

            _locks[key] = (token, time.GetUtcNow() + ttl);
            return true;
        }
    }

    private void Release(string key, Guid token)
    {
        lock (_sync)
        {
            if (_locks.TryGetValue(key, out var entry) && entry.Token == token)
            {
                _locks.Remove(key); // release seguro: só o dono libera
            }
        }
    }

    private sealed class MemoryLockHandle(MemoryLockProvider provider, string key, Guid token) : ILockHandle
    {
        public string Key => key;

        public ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
            => ValueTask.FromResult(provider.Renew(key, token, ttl));

        public ValueTask DisposeAsync()
        {
            provider.Release(key, token);
            return ValueTask.CompletedTask;
        }
    }
}
