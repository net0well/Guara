using System.Text;
using System.Text.Json;
using Guara.Abstractions;
using Microsoft.Data.SqlClient;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Persistência de jobs sobre SQL Server. A aquisição usa <c>READPAST + UPDLOCK</c>:
/// cada nó pega uma linha distinta e os demais pulam as bloqueadas, sem contenção e sem
/// dupla entrega. Toda comparação temporal usa o relógio <b>injetado</b> do nó chamador
/// (nunca o do banco) — mesma semântica dos demais providers.
/// </summary>
internal sealed class SqlServerJobStorage(
    SqlServerConnections connections, SqlServerSchemaInitializer schema, string s, TimeProvider time) : IJobStorage
{
    private const string Columns =
        "id, descriptor, state, attempt, queue, created_at, scheduled_for, lease_until, finished_at, result, error";

    public ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct)
        => CreateCoreAsync(record, null, ct);

    public ValueTask<JobId> CreateAsync(JobRecord record, IGuaraTransaction transaction, CancellationToken ct)
        => CreateCoreAsync(record, RelationalTransaction.Require(transaction, "SQL Server"), ct);

    private async ValueTask<JobId> CreateCoreAsync(
        JobRecord record, RelationalTransaction? transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        // O esquema roda na conexão própria do provider, fora da transação do chamador:
        // DDL lá dentro estenderia o alcance de um rollback dele até as tabelas do Guará.
        await schema.EnsureAsync(ct);

        // Sem transação a conexão é nossa e fecha aqui; com ela é emprestada de quem a
        // abriu e precisa continuar viva depois — por isso só a própria entra no descarte.
        var propria = transaction is null ? await connections.OpenAsync(ct) : null;
        await using var _ = propria;

        var connection = propria ?? transaction!.RequireConnection<SqlConnection>("SQL Server");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction?.RequireTransaction<SqlTransaction>("SQL Server");
        // Idempotente pelo id: recriar o mesmo job não duplica nem sobrescreve.
        command.CommandText = $"""
            INSERT INTO {s}.jobs ({Columns})
            SELECT @id, @descriptor, @state, @attempt, @queue, @createdAt, @scheduledFor, @leaseUntil,
                   @finishedAt, @result, @error
            WHERE NOT EXISTS (SELECT 1 FROM {s}.jobs WITH (UPDLOCK, SERIALIZABLE) WHERE id = @id);
            """;
        command.Parameters.AddWithValue("@id", record.Id.Value);
        command.Parameters.AddWithValue(
            "@descriptor", JsonSerializer.Serialize(record.Descriptor, SqlServerJsonContext.Default.JobDescriptor));
        command.Parameters.AddWithValue("@state", (int)record.State);
        command.Parameters.AddWithValue("@attempt", record.Attempt);
        command.Parameters.AddWithValue("@queue", record.Queue);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("@scheduledFor", (object?)record.ScheduledFor ?? DBNull.Value);
        command.Parameters.AddWithValue("@leaseUntil", (object?)record.LeaseUntil ?? DBNull.Value);
        command.Parameters.AddWithValue("@finishedAt", (object?)record.FinishedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@result", (object?)record.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("@error", (object?)record.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return record.Id;
    }

    public async ValueTask<JobRecord?> AcquireNextDueAsync(
        string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // O candidato sai de uma CTE porque UPDATE TOP(1) não honra ORDER BY, e a ordem
        // importa: a fila é FIFO por criação. READPAST faz o nó pular linhas já travadas
        // por outro em vez de esperar — é o que troca contenção por vazão.
        command.CommandText = $"""
            WITH candidato AS (
                SELECT TOP (1) {Columns}
                FROM {s}.jobs WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE queue = @queue
                  AND (state = {(int)JobState.Enqueued}
                       OR (state IN ({(int)JobState.Scheduled}, {(int)JobState.Retrying}) AND scheduled_for <= @now)
                       OR (state = {(int)JobState.Processing} AND lease_until < @now))
                ORDER BY created_at
            )
            UPDATE candidato
            SET state = {(int)JobState.Processing}, lease_until = @leaseUntil
            OUTPUT INSERTED.id, INSERTED.descriptor, INSERTED.state, INSERTED.attempt, INSERTED.queue,
                   INSERTED.created_at, INSERTED.scheduled_for, INSERTED.lease_until, INSERTED.finished_at,
                   INSERTED.result, INSERTED.error;
            """;
        command.Parameters.AddWithValue("@queue", queue);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@leaseUntil", now + lease);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {s}.jobs SET lease_until = @leaseUntil
            WHERE id = @id AND state = {(int)JobState.Processing} AND lease_until IS NOT NULL
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@leaseUntil", time.GetUtcNow() + lease);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask ScheduleRetryAsync(JobId id, string error, DateTimeOffset retryAt, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {s}.jobs
            SET state = {(int)JobState.Retrying}, error = @error, attempt = attempt + 1,
                scheduled_for = @retryAt, lease_until = NULL
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@error", error);
        command.Parameters.AddWithValue("@retryAt", retryAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask RescheduleAsync(JobId id, DateTimeOffset scheduledFor, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {s}.jobs
            SET state = {(int)JobState.Scheduled}, scheduled_for = @scheduledFor, lease_until = NULL
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@scheduledFor", scheduledFor);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {s}.jobs SET
                state = @state,
                result = CASE WHEN @state = {(int)JobState.Succeeded} THEN @value ELSE result END,
                error = CASE WHEN @state IN ({(int)JobState.Failed}, {(int)JobState.Retrying})
                             THEN @value ELSE error END,
                lease_until = CASE WHEN @state = {(int)JobState.Processing} THEN lease_until ELSE NULL END,
                finished_at = CASE WHEN @state IN ({(int)JobState.Succeeded}, {(int)JobState.Failed})
                                   THEN COALESCE(finished_at, @now) ELSE finished_at END
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@state", (int)state);
        command.Parameters.AddWithValue("@value", (object?)resultOrError ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", time.GetUtcNow());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {s}.jobs WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.Value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {s}.jobs WHERE id = @id AND state <> {(int)JobState.Processing}";
        command.Parameters.AddWithValue("@id", id.Value);
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
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DELETE FROM {s}.jobs WHERE state = @state AND finished_at IS NOT NULL AND finished_at < @cutoff";
        command.Parameters.AddWithValue("@state", (int)state);
        command.Parameters.AddWithValue("@cutoff", finishedBefore);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<IReadOnlyDictionary<JobState, long>> CountByStateAsync(string? queue, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        if (queue is null)
        {
            command.CommandText = $"SELECT state, count_big(*) FROM {s}.jobs GROUP BY state";
        }
        else
        {
            command.CommandText = $"SELECT state, count_big(*) FROM {s}.jobs WHERE queue = @queue GROUP BY state";
            command.Parameters.AddWithValue("@queue", queue);
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

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var where = BuildWhere(query, command);
        // OFFSET/FETCH exige ORDER BY explícito no SQL Server.
        command.CommandText = $"""
            SELECT {Columns} FROM {s}.jobs {where}
            ORDER BY created_at DESC
            OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY
            """;
        command.Parameters.AddWithValue("@limit", pageSize);
        command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

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

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var where = BuildWhere(query, command);
        command.CommandText = $"SELECT count_big(*) FROM {s}.jobs {where}";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async ValueTask<IReadOnlyList<JobSeriesPoint>> GetSeriesAsync(
        JobSeriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await schema.EnsureAsync(ct);

        var queueFilter = query.Queue is null ? "" : " AND queue = @queue";

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // PERCENTILE_DISC no SQL Server é função de janela, não agregação: não combina com
        // GROUP BY. A saída vem de funções de janela particionadas pelo balde, e o DISTINCT
        // colapsa as linhas repetidas de cada partição.
        //
        // O índice do balde sai de aritmética inteira em microssegundos, e não de divisão em
        // ponto flutuante, para não jogar job de fronteira no balde vizinho.
        command.CommandText = $"""
            WITH finalizados AS (
                SELECT DATEDIFF_BIG(microsecond, @from, finished_at) / @bucket AS balde,
                       state,
                       DATEDIFF_BIG(microsecond, created_at, finished_at) AS duracao
                FROM {s}.jobs
                WHERE state IN ({(int)JobState.Succeeded}, {(int)JobState.Failed})
                  AND finished_at >= @from AND finished_at < @to{queueFilter}
            )
            SELECT DISTINCT
                balde,
                count_big(CASE WHEN state = {(int)JobState.Succeeded} THEN 1 END) OVER (PARTITION BY balde),
                count_big(CASE WHEN state = {(int)JobState.Failed} THEN 1 END) OVER (PARTITION BY balde),
                PERCENTILE_DISC(0.50) WITHIN GROUP (ORDER BY duracao) OVER (PARTITION BY balde),
                PERCENTILE_DISC(0.95) WITHIN GROUP (ORDER BY duracao) OVER (PARTITION BY balde)
            FROM finalizados
            """;
        command.Parameters.AddWithValue("@from", query.From);
        command.Parameters.AddWithValue("@to", query.To);
        command.Parameters.AddWithValue("@bucket", query.Bucket.Ticks / TimeSpan.TicksPerMicrosecond);
        if (query.Queue is { } queue)
        {
            command.Parameters.AddWithValue("@queue", queue);
        }

        var baldes = new Dictionary<long, (long Succeeded, long Failed, TimeSpan? P50, TimeSpan? P95)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                baldes[reader.GetInt64(0)] = (
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : Microssegundos(reader.GetInt64(3)),
                    reader.IsDBNull(4) ? null : Microssegundos(reader.GetInt64(4)));
            }
        }

        // A janela volta contínua: balde sem job finalizado é ponto zerado, não buraco.
        var points = new List<JobSeriesPoint>();
        var indice = 0L;
        foreach (var inicio in query.Buckets())
        {
            points.Add(baldes.TryGetValue(indice, out var balde)
                ? new JobSeriesPoint(inicio, balde.Succeeded, balde.Failed, balde.P50, balde.P95)
                : new JobSeriesPoint(inicio, 0, 0, null, null));
            indice++;
        }

        return points;
    }

    private static TimeSpan Microssegundos(long valor) => TimeSpan.FromTicks(valor * TimeSpan.TicksPerMicrosecond);

    private static string BuildWhere(JobQuery query, SqlCommand command)
    {
        var filters = new List<string>();

        if (query.State is { } state)
        {
            filters.Add("state = @state");
            command.Parameters.AddWithValue("@state", (int)state);
        }

        if (query.Queue is { } queue)
        {
            filters.Add("queue = @queue");
            command.Parameters.AddWithValue("@queue", queue);
        }

        if (query.TypeName is { } typeName)
        {
            filters.Add("JSON_VALUE(descriptor, '$.typeName') = @typeName");
            command.Parameters.AddWithValue("@typeName", typeName);
        }

        if (query.From is { } from)
        {
            filters.Add("created_at >= @from");
            command.Parameters.AddWithValue("@from", from);
        }

        if (query.To is { } to)
        {
            filters.Add("created_at < @to");
            command.Parameters.AddWithValue("@to", to);
        }

        if (query.Text is { Length: > 0 } text)
        {
            // O mesmo trecho procurado no id e nos nomes do descritor. A busca não diferencia
            // maiúsculas porque o SQL Server compara pela collation do banco, que é
            // case-insensitive por padrão.
            filters.Add("""
                (id LIKE @text ESCAPE '\'
                 OR JSON_VALUE(descriptor, '$.typeName') LIKE @text ESCAPE '\'
                 OR JSON_VALUE(descriptor, '$.methodName') LIKE @text ESCAPE '\')
                """);
            command.Parameters.AddWithValue("@text", $"%{Escape(text)}%");
        }

        return filters.Count > 0 ? $"WHERE {string.Join(" AND ", filters)}" : "";
    }

    // LIKE trata %, _ e [ como curinga: o texto digitado pelo operador é literal.
    private static string Escape(string text)
    {
        var escapado = new StringBuilder(text.Length);
        foreach (var caractere in text)
        {
            if (caractere is '%' or '_' or '[' or ']' or '\\')
            {
                escapado.Append('\\');
            }

            escapado.Append(caractere);
        }

        return escapado.ToString();
    }

    private static JobRecord ReadJob(SqlDataReader reader) => new()
    {
        Id = new JobId(reader.GetString(0)),
        Descriptor = JsonSerializer.Deserialize(reader.GetString(1), SqlServerJsonContext.Default.JobDescriptor)!,
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
