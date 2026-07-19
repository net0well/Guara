using Guara.Abstractions;
using Guara.Storage;
using Npgsql;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Vínculos de continuação na tabela <c>continuations</c>. A resolução é um UPDATE
/// condicionado a <c>status = Pending</c>: entre nós concorrentes, exatamente um vence.
/// </summary>
internal sealed class PostgreSqlContinuationStorage(
    NpgsqlDataSource dataSource, PostgreSqlSchemaInitializer schema, string s) : IContinuationStorage
{
    private const string Columns = "child_id, parent_id, fires_on, status, reason, depth, created_at, resolved_at";

    public async ValueTask AddAsync(ContinuationRecord record, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO {s}.continuations ({Columns})
            VALUES (@childId, @parentId, @firesOn, @status, @reason, @depth, @createdAt, @resolvedAt)
            ON CONFLICT (child_id) DO NOTHING
            """);
        command.Parameters.AddWithValue("childId", record.ChildId.Value);
        command.Parameters.AddWithValue("parentId", record.ParentId.Value);
        command.Parameters.AddWithValue("firesOn", (int)record.Trigger);
        command.Parameters.AddWithValue("status", (int)record.Status);
        command.Parameters.AddWithValue("reason", (object?)record.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("depth", record.Depth);
        command.Parameters.AddWithValue("createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("resolvedAt", (object?)record.ResolvedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<ContinuationRecord?> GetByChildAsync(JobId childId, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM {s}.continuations WHERE child_id = @childId");
        command.Parameters.AddWithValue("childId", childId.Value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadContinuation(reader) : null;
    }

    public async ValueTask<IReadOnlyList<ContinuationRecord>> ListByParentAsync(JobId parentId, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM {s}.continuations WHERE parent_id = @parentId ORDER BY created_at");
        command.Parameters.AddWithValue("parentId", parentId.Value);
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask<IReadOnlyList<ContinuationRecord>> ListPendingAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns} FROM {s}.continuations
            WHERE status = {(int)ContinuationStatus.Pending}
            ORDER BY created_at
            """);
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask<bool> TryResolveAsync(
        JobId childId, ContinuationStatus status, string? reason, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            UPDATE {s}.continuations
            SET status = @status, reason = @reason, resolved_at = @resolvedAt
            WHERE child_id = @childId AND status = {(int)ContinuationStatus.Pending}
            """);
        command.Parameters.AddWithValue("childId", childId.Value);
        command.Parameters.AddWithValue("status", (int)status);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("resolvedAt", resolvedAt);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private static async ValueTask<IReadOnlyList<ContinuationRecord>> ReadAllAsync(
        NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<ContinuationRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadContinuation(reader));
        }

        return results;
    }

    private static ContinuationRecord ReadContinuation(NpgsqlDataReader reader) => new()
    {
        ChildId = new JobId(reader.GetString(0)),
        ParentId = new JobId(reader.GetString(1)),
        Trigger = (ContinuationTrigger)reader.GetInt32(2),
        Status = (ContinuationStatus)reader.GetInt32(3),
        Reason = reader.IsDBNull(4) ? null : reader.GetString(4),
        Depth = reader.GetInt32(5),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(6),
        ResolvedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
    };
}
