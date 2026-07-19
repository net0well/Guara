using Guara.Abstractions;
using Guara.Storage;
using Npgsql;

namespace Guara.Storage.PostgreSql;

/// <summary>Introspecção de filas derivada da tabela de jobs.</summary>
internal sealed class PostgreSqlQueueStorage(
    NpgsqlDataSource dataSource, PostgreSqlSchemaInitializer schema, string s) : IQueueStorage
{
    public async ValueTask<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT DISTINCT queue FROM {s}.jobs ORDER BY queue");

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
        await using var command = dataSource.CreateCommand(
            $"SELECT count(*) FROM {s}.jobs WHERE queue = @queue AND state = {(int)JobState.Enqueued}");
        command.Parameters.AddWithValue("queue", queue);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
