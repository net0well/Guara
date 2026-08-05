using System.Text.Json;
using Guara.Storage;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>Registro de nós servidores na tabela <c>servers</c> (upsert por id).</summary>
internal sealed class MySqlServerRegistry(
    MySqlDataSource dataSource, MySqlSchemaInitializer schema, string p) : IServerRegistry
{
    public async ValueTask AnnounceAsync(ServerNode node, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        await schema.EnsureAsync(ct);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {p}servers (id, machine_name, started_at, last_heartbeat, queues, max_concurrency)
            VALUES (@id, @machineName, @startedAt, @lastHeartbeat, @queues, @maxConcurrency)
            ON DUPLICATE KEY UPDATE
                machine_name = VALUES(machine_name),
                started_at = VALUES(started_at),
                last_heartbeat = VALUES(last_heartbeat),
                queues = VALUES(queues),
                max_concurrency = VALUES(max_concurrency)
            """;
        command.Parameters.AddWithValue("@id", node.Id);
        command.Parameters.AddWithValue("@machineName", node.MachineName);
        command.Parameters.AddWithValue("@startedAt", MySqlTime.ToDatabase(node.StartedAt));
        command.Parameters.AddWithValue("@lastHeartbeat", MySqlTime.ToDatabase(node.LastHeartbeat));
        command.Parameters.AddWithValue("@queues", SerializarFilas(node.Queues));
        command.Parameters.AddWithValue("@maxConcurrency", node.MaxConcurrency);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<bool> HeartbeatAsync(string serverId, DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {p}servers SET last_heartbeat = @now WHERE id = @id";
        command.Parameters.AddWithValue("@id", serverId);
        command.Parameters.AddWithValue("@now", MySqlTime.ToDatabase(now));
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask RemoveAsync(string serverId, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {p}servers WHERE id = @id";
        command.Parameters.AddWithValue("@id", serverId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<IReadOnlyList<ServerNode>> ListAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, machine_name, started_at, last_heartbeat, queues, max_concurrency
            FROM {p}servers ORDER BY id
            """;

        var results = new List<ServerNode>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ServerNode
            {
                Id = reader.GetString(0),
                MachineName = reader.GetString(1),
                StartedAt = MySqlTime.FromDatabase(reader.GetDateTime(2)),
                LastHeartbeat = MySqlTime.FromDatabase(reader.GetDateTime(3)),
                Queues = DesserializarFilas(reader.GetString(4)),
                MaxConcurrency = reader.GetInt32(5),
            });
        }

        return results;
    }

    public async ValueTask<int> RemoveExpiredAsync(DateTimeOffset heartbeatBefore, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {p}servers WHERE last_heartbeat < @cutoff";
        command.Parameters.AddWithValue("@cutoff", MySqlTime.ToDatabase(heartbeatBefore));
        return await command.ExecuteNonQueryAsync(ct);
    }

    // O MySQL não tem coluna de array como o text[] do PostgreSQL. JSON preserva o nome
    // exato de cada fila, inclusive vírgula e espaço, que um separador simples perderia.
    private static string SerializarFilas(string[] queues)
        => JsonSerializer.Serialize(queues, MySqlJsonContext.Default.StringArray);

    private static string[] DesserializarFilas(string payload)
        => JsonSerializer.Deserialize(payload, MySqlJsonContext.Default.StringArray) ?? [];
}
