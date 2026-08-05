using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Storage.SqlServer;

/// <summary>Introspecção de filas derivada da tabela de jobs.</summary>
internal sealed class SqlServerQueueStorage(
    SqlServerConnections connections, SqlServerSchemaInitializer schema, string s) : IQueueStorage
{
    public async ValueTask<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT DISTINCT queue FROM {s}.jobs ORDER BY queue";

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
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // count(*) devolve int no SQL Server; count_big mantém o contrato de long do storage.
        command.CommandText =
            $"SELECT count_big(*) FROM {s}.jobs WHERE queue = @queue AND state = {(int)JobState.Enqueued}";
        command.Parameters.AddWithValue("@queue", queue);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
