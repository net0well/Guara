using System.Text.Json;
using Guara.Storage;

namespace Guara.Storage.SqlServer;

/// <summary>Registro de nós servidores na tabela <c>servers</c> (upsert por id).</summary>
internal sealed class SqlServerServerRegistry(
    SqlServerConnections connections, SqlServerSchemaInitializer schema, string s) : IServerRegistry
{
    public async ValueTask AnnounceAsync(ServerNode node, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        await schema.EnsureAsync(ct);

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // Mesmo padrão do lock: UPDATE e, só se nada mudou, INSERT sob bloqueio de faixa.
        // O upsert numa instrução do PostgreSQL não tem equivalente direto aqui.
        command.CommandText = $"""
            UPDATE {s}.servers WITH (UPDLOCK, SERIALIZABLE)
            SET machine_name = @machineName,
                started_at = @startedAt,
                last_heartbeat = @lastHeartbeat,
                queues = @queues,
                max_concurrency = @maxConcurrency,
                roles = @roles
            WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {s}.servers (id, machine_name, started_at, last_heartbeat, queues, max_concurrency, roles)
            SELECT @id, @machineName, @startedAt, @lastHeartbeat, @queues, @maxConcurrency, @roles
            WHERE NOT EXISTS (SELECT 1 FROM {s}.servers WITH (UPDLOCK, SERIALIZABLE) WHERE id = @id);
            """;
        command.Parameters.AddWithValue("@id", node.Id);
        command.Parameters.AddWithValue("@machineName", node.MachineName);
        command.Parameters.AddWithValue("@startedAt", node.StartedAt);
        command.Parameters.AddWithValue("@lastHeartbeat", node.LastHeartbeat);
        command.Parameters.AddWithValue("@queues", SerializarLista(node.Queues));
        command.Parameters.AddWithValue("@maxConcurrency", node.MaxConcurrency);
        command.Parameters.AddWithValue("@roles", SerializarLista(node.Roles));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<bool> HeartbeatAsync(string serverId, DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {s}.servers SET last_heartbeat = @now WHERE id = @id";
        command.Parameters.AddWithValue("@id", serverId);
        command.Parameters.AddWithValue("@now", now);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask RemoveAsync(string serverId, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {s}.servers WHERE id = @id";
        command.Parameters.AddWithValue("@id", serverId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<IReadOnlyList<ServerNode>> ListAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, machine_name, started_at, last_heartbeat, queues, max_concurrency, roles
            FROM {s}.servers ORDER BY id
            """;

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
                Queues = DesserializarLista(reader.GetString(4)),
                MaxConcurrency = reader.GetInt32(5),
                Roles = DesserializarLista(reader.GetString(6)),
            });
        }

        return results;
    }

    public async ValueTask<int> RemoveExpiredAsync(DateTimeOffset heartbeatBefore, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {s}.servers WHERE last_heartbeat < @cutoff";
        command.Parameters.AddWithValue("@cutoff", heartbeatBefore);
        return await command.ExecuteNonQueryAsync(ct);
    }

    // O SQL Server não tem coluna de array como o text[] do PostgreSQL. JSON preserva o
    // nome exato de cada item, inclusive vírgula e espaço, que um separador simples perderia.
    private static string SerializarLista(string[] itens)
        => JsonSerializer.Serialize(itens, SqlServerJsonContext.Default.StringArray);

    private static string[] DesserializarLista(string payload)
        => JsonSerializer.Deserialize(payload, SqlServerJsonContext.Default.StringArray) ?? [];
}
