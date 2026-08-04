using System.Text.Json;
using Guara.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Persistência de jobs sobre PostgreSQL. A aquisição usa
/// <c>FOR UPDATE SKIP LOCKED</c>: cada nó pega uma linha distinta, sem contenção e
/// sem dupla entrega. Toda comparação temporal usa o relógio <b>injetado</b> do nó
/// chamador (nunca <c>now()</c> do banco) — mesma semântica dos demais providers.
/// </summary>
internal sealed class PostgreSqlJobStorage(
    NpgsqlDataSource dataSource, PostgreSqlSchemaInitializer schema, string s, TimeProvider time) : IJobStorage
{
    private const string Columns =
        "id, descriptor, state, attempt, queue, created_at, scheduled_for, lease_until, finished_at, result, error";

    public async ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO {s}.jobs ({Columns})
            VALUES (@id, @descriptor, @state, @attempt, @queue, @createdAt, @scheduledFor, @leaseUntil, @finishedAt, @result, @error)
            ON CONFLICT (id) DO NOTHING
            """);
        command.Parameters.AddWithValue("id", record.Id.Value);
        command.Parameters.Add(new NpgsqlParameter("descriptor", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(record.Descriptor, PostgreSqlJsonContext.Default.JobDescriptor),
        });
        command.Parameters.AddWithValue("state", (int)record.State);
        command.Parameters.AddWithValue("attempt", record.Attempt);
        command.Parameters.AddWithValue("queue", record.Queue);
        command.Parameters.AddWithValue("createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("scheduledFor", (object?)record.ScheduledFor ?? DBNull.Value);
        command.Parameters.AddWithValue("leaseUntil", (object?)record.LeaseUntil ?? DBNull.Value);
        command.Parameters.AddWithValue("finishedAt", (object?)record.FinishedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("result", (object?)record.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)record.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return record.Id;
    }

    public async ValueTask<JobRecord?> AcquireNextDueAsync(
        string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            WITH candidate AS (
                SELECT id FROM {s}.jobs
                WHERE queue = @queue
                  AND (state = {(int)JobState.Enqueued}
                       OR (state IN ({(int)JobState.Scheduled}, {(int)JobState.Retrying}) AND scheduled_for <= @now)
                       OR (state = {(int)JobState.Processing} AND lease_until < @now))
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {s}.jobs jobs
            SET state = {(int)JobState.Processing}, lease_until = @leaseUntil
            FROM candidate
            WHERE jobs.id = candidate.id
            RETURNING {Prefixed("jobs")}
            """);
        command.Parameters.AddWithValue("queue", queue);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("leaseUntil", now + lease);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            UPDATE {s}.jobs SET lease_until = @leaseUntil
            WHERE id = @id AND state = {(int)JobState.Processing} AND lease_until IS NOT NULL
            """);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("leaseUntil", time.GetUtcNow() + lease);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask ScheduleRetryAsync(JobId id, string error, DateTimeOffset retryAt, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            UPDATE {s}.jobs
            SET state = {(int)JobState.Retrying}, error = @error, attempt = attempt + 1,
                scheduled_for = @retryAt, lease_until = NULL
            WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("retryAt", retryAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask RescheduleAsync(JobId id, DateTimeOffset scheduledFor, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            UPDATE {s}.jobs
            SET state = {(int)JobState.Scheduled}, scheduled_for = @scheduledFor, lease_until = NULL
            WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("scheduledFor", scheduledFor);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            UPDATE {s}.jobs SET
                state = @state,
                result = CASE WHEN @state = {(int)JobState.Succeeded} THEN @value ELSE result END,
                error = CASE WHEN @state IN ({(int)JobState.Failed}, {(int)JobState.Retrying}) THEN @value ELSE error END,
                lease_until = CASE WHEN @state = {(int)JobState.Processing} THEN lease_until ELSE NULL END,
                finished_at = CASE WHEN @state IN ({(int)JobState.Succeeded}, {(int)JobState.Failed})
                                   THEN COALESCE(finished_at, @now) ELSE finished_at END
            WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("state", (int)state);
        command.Parameters.AddWithValue("value", (object?)resultOrError ?? DBNull.Value);
        command.Parameters.AddWithValue("now", time.GetUtcNow());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM {s}.jobs WHERE id = @id");
        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {s}.jobs WHERE id = @id AND state <> {(int)JobState.Processing}");
        command.Parameters.AddWithValue("id", id.Value);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<int> PurgeAsync(JobState state, DateTimeOffset finishedBefore, CancellationToken ct)
    {
        if (state is not (JobState.Succeeded or JobState.Failed))
        {
            throw new ArgumentException(
                $"Apenas estados terminais (Succeeded/Failed) podem ser purgados; recebido: {state}.", nameof(state));
        }

        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {s}.jobs WHERE state = @state AND finished_at IS NOT NULL AND finished_at < @cutoff");
        command.Parameters.AddWithValue("state", (int)state);
        command.Parameters.AddWithValue("cutoff", finishedBefore);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<IReadOnlyDictionary<JobState, long>> CountByStateAsync(string? queue, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand();
        if (queue is null)
        {
            command.CommandText = $"SELECT state, count(*) FROM {s}.jobs GROUP BY state";
        }
        else
        {
            command.CommandText = $"SELECT state, count(*) FROM {s}.jobs WHERE queue = @queue GROUP BY state";
            command.Parameters.AddWithValue("queue", queue);
        }

        var counts = new Dictionary<JobState, long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            counts[(JobState)reader.GetInt32(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    public async ValueTask<IReadOnlyList<JobRecord>> ListAsync(JobQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await schema.EnsureAsync(ct);
        var page = query.EffectivePage;
        var pageSize = query.EffectivePageSize;

        await using var command = dataSource.CreateCommand();
        var where = BuildWhere(query, command);
        command.CommandText = $"""
            SELECT {Columns} FROM {s}.jobs {where}
            ORDER BY created_at DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);

        var results = new List<JobRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadJob(reader));
        }

        return results;
    }

    public async ValueTask<long> CountAsync(JobQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await schema.EnsureAsync(ct);

        await using var command = dataSource.CreateCommand();
        var where = BuildWhere(query, command);
        command.CommandText = $"SELECT count(*) FROM {s}.jobs {where}";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async ValueTask<IReadOnlyList<JobSeriesPoint>> GetSeriesAsync(
        JobSeriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await schema.EnsureAsync(ct);

        // O índice do balde sai de aritmética inteira em microssegundos (a resolução do
        // timestamp no PostgreSQL): dividir o epoch em ponto flutuante colocaria jobs de
        // fronteira no balde errado. percentile_disc devolve uma latência realmente
        // observada — a mesma definição que os demais providers aplicam.
        // O filtro de fila é concatenado, e não um parâmetro sempre presente comparado a
        // NULL: o servidor não consegue inferir o tipo de um parâmetro que só aparece em
        // "IS NULL" e rejeita a consulta.
        var queueFilter = query.Queue is null ? "" : " AND queue = @queue";
        await using var command = dataSource.CreateCommand($"""
            SELECT
                (extract(epoch from (finished_at - @from)) * 1000000)::bigint / @bucket AS bucket,
                count(*) FILTER (WHERE state = {(int)JobState.Succeeded}),
                count(*) FILTER (WHERE state = {(int)JobState.Failed}),
                percentile_disc(0.50) WITHIN GROUP (ORDER BY (finished_at - created_at)),
                percentile_disc(0.95) WITHIN GROUP (ORDER BY (finished_at - created_at))
            FROM {s}.jobs
            WHERE state IN ({(int)JobState.Succeeded}, {(int)JobState.Failed})
              AND finished_at >= @from AND finished_at < @to{queueFilter}
            GROUP BY bucket
            """);
        command.Parameters.AddWithValue("from", query.From);
        command.Parameters.AddWithValue("to", query.To);
        command.Parameters.AddWithValue("bucket", query.Bucket.Ticks / TimeSpan.TicksPerMicrosecond);
        if (query.Queue is { } queue)
        {
            command.Parameters.AddWithValue("queue", queue);
        }

        var buckets = new Dictionary<long, (long Succeeded, long Failed, TimeSpan? P50, TimeSpan? P95)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                buckets[reader.GetInt64(0)] = (
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<TimeSpan>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<TimeSpan>(4));
            }
        }

        // A janela volta contínua: balde sem job finalizado é ponto zerado, não buraco.
        var points = new List<JobSeriesPoint>();
        var index = 0L;
        foreach (var start in query.Buckets())
        {
            points.Add(buckets.TryGetValue(index, out var bucket)
                ? new JobSeriesPoint(start, bucket.Succeeded, bucket.Failed, bucket.P50, bucket.P95)
                : new JobSeriesPoint(start, 0, 0, null, null));
            index++;
        }

        return points;
    }

    private static string BuildWhere(JobQuery query, NpgsqlCommand command)
    {
        var filters = new List<string>();

        if (query.State is { } state)
        {
            filters.Add("state = @state");
            command.Parameters.AddWithValue("state", (int)state);
        }

        if (query.Queue is { } queue)
        {
            filters.Add("queue = @queue");
            command.Parameters.AddWithValue("queue", queue);
        }

        if (query.TypeName is { } typeName)
        {
            filters.Add("descriptor ->> 'typeName' = @typeName");
            command.Parameters.AddWithValue("typeName", typeName);
        }

        if (query.From is { } from)
        {
            filters.Add("created_at >= @from");
            command.Parameters.AddWithValue("from", from);
        }

        if (query.To is { } to)
        {
            filters.Add("created_at < @to");
            command.Parameters.AddWithValue("to", to);
        }

        if (query.Text is { Length: > 0 } text)
        {
            // O mesmo trecho procurado no id e nos nomes do descritor, sem diferenciar caixa.
            filters.Add("""
                (id ILIKE @text
                 OR descriptor ->> 'typeName' ILIKE @text
                 OR descriptor ->> 'methodName' ILIKE @text)
                """);
            command.Parameters.AddWithValue("text", $"%{Escape(text)}%");
        }

        return filters.Count > 0 ? $"WHERE {string.Join(" AND ", filters)}" : "";
    }

    // ILIKE trata _ e % como curingas: o texto digitado pelo operador é literal.
    private static string Escape(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string Prefixed(string alias)
        => string.Join(", ", Columns.Split(", ").Select(column => $"{alias}.{column}"));

    private static JobRecord ReadJob(NpgsqlDataReader reader) => new()
    {
        Id = new JobId(reader.GetString(0)),
        Descriptor = JsonSerializer.Deserialize(reader.GetString(1), PostgreSqlJsonContext.Default.JobDescriptor)!,
        State = (JobState)reader.GetInt32(2),
        Attempt = reader.GetInt32(3),
        Queue = reader.GetString(4),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
        ScheduledFor = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        LeaseUntil = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
        FinishedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        Result = reader.IsDBNull(9) ? null : reader.GetString(9),
        Error = reader.IsDBNull(10) ? null : reader.GetString(10),
    };
}
