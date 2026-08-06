using Guara.Storage;
using Npgsql;

namespace Guara.Storage.PostgreSql;

/// <summary>Registro de nós servidores na tabela <c>servers</c> (upsert por id).</summary>
internal sealed class PostgreSqlServerRegistry(
    NpgsqlDataSource dataSource, PostgreSqlSchemaInitializer schema, string s) : IServerRegistry
{
    public async ValueTask AnnounceAsync(ServerNode node, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO {s}.servers (id, machine_name, started_at, last_heartbeat, queues, max_concurrency, roles)
            VALUES (@id, @machineName, @startedAt, @lastHeartbeat, @queues, @maxConcurrency, @roles)
            ON CONFLICT (id) DO UPDATE SET
                machine_name = EXCLUDED.machine_name,
                started_at = EXCLUDED.started_at,
                last_heartbeat = EXCLUDED.last_heartbeat,
                queues = EXCLUDED.queues,
                max_concurrency = EXCLUDED.max_concurrency,
                roles = EXCLUDED.roles
            """);
        command.Parameters.AddWithValue("id", node.Id);
        command.Parameters.AddWithValue("machineName", node.MachineName);
        command.Parameters.AddWithValue("startedAt", node.StartedAt);
        command.Parameters.AddWithValue("lastHeartbeat", node.LastHeartbeat);
        command.Parameters.AddWithValue("queues", node.Queues);
        command.Parameters.AddWithValue("maxConcurrency", node.MaxConcurrency);
        command.Parameters.AddWithValue("roles", node.Roles);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<bool> HeartbeatAsync(string serverId, DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"UPDATE {s}.servers SET last_heartbeat = @now WHERE id = @id");
        command.Parameters.AddWithValue("id", serverId);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask RemoveAsync(string serverId, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"DELETE FROM {s}.servers WHERE id = @id");
        command.Parameters.AddWithValue("id", serverId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<IReadOnlyList<ServerNode>> ListAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            SELECT id, machine_name, started_at, last_heartbeat, queues, max_concurrency, roles
            FROM {s}.servers ORDER BY id
            """);

        var results = new List<ServerNode>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ServerNode
            {
                Id = reader.GetString(0),
                MachineName = reader.GetString(1),
                StartedAt = reader.GetFieldValue<DateTimeOffset>(2),
                LastHeartbeat = reader.GetFieldValue<DateTimeOffset>(3),
                Queues = reader.GetFieldValue<string[]>(4),
                MaxConcurrency = reader.GetInt32(5),
                Roles = reader.GetFieldValue<string[]>(6),
            });
        }

        return results;
    }

    public async ValueTask<int> RemoveExpiredAsync(DateTimeOffset heartbeatBefore, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {s}.servers WHERE last_heartbeat < @cutoff");
        command.Parameters.AddWithValue("cutoff", heartbeatBefore);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
