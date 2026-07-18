using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>Registro de nós em memória, com exclusão mútua sobre o dicionário.</summary>
internal sealed class MemoryServerRegistry : IServerRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ServerNode> _servers = new(StringComparer.Ordinal);

    public ValueTask AnnounceAsync(ServerNode node, CancellationToken ct)
    {
        lock (_sync)
        {
            _servers[node.Id] = node;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> HeartbeatAsync(string serverId, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_servers.TryGetValue(serverId, out var node))
            {
                return ValueTask.FromResult(false); // nó removido: o chamador deve reanunciar
            }

            _servers[serverId] = node with { LastHeartbeat = now };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask RemoveAsync(string serverId, CancellationToken ct)
    {
        lock (_sync)
        {
            _servers.Remove(serverId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ServerNode>> ListAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyList<ServerNode> snapshot = [.. _servers.Values.OrderBy(s => s.Id, StringComparer.Ordinal)];
            return ValueTask.FromResult(snapshot);
        }
    }

    public ValueTask<int> RemoveExpiredAsync(DateTimeOffset heartbeatBefore, CancellationToken ct)
    {
        lock (_sync)
        {
            var expired = _servers.Values
                .Where(s => s.LastHeartbeat < heartbeatBefore)
                .Select(s => s.Id)
                .ToList();

            foreach (var id in expired)
            {
                _servers.Remove(id);
            }

            return ValueTask.FromResult(expired.Count);
        }
    }
}
