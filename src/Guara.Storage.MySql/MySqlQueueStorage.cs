using Guara.Abstractions;
using Guara.Storage;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>Introspecção de filas derivada da tabela de jobs.</summary>
internal sealed class MySqlQueueStorage(
    MySqlDataSource dataSource, MySqlSchemaInitializer schema, string p) : IQueueStorage
{
    public async ValueTask<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT DISTINCT queue FROM {p}jobs ORDER BY queue";

        var queues = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            queues.Add(reader.GetString(0));
        }

        return queues;
    }

    public async ValueTask<long> GetLengthAsync(string queue, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT count(*) FROM {p}jobs WHERE queue = @queue AND state = {(int)JobState.Enqueued}";
        command.Parameters.AddWithValue("@queue", queue);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
