using System.Text;
using System.Text.Json;
using Guara.Abstractions;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>
/// Persistência de jobs sobre MySQL 8+. A aquisição usa <c>FOR UPDATE SKIP LOCKED</c>:
/// cada nó pega uma linha distinta e os demais pulam as bloqueadas, sem contenção e sem
/// dupla entrega. Toda comparação temporal usa o relógio <b>injetado</b> do nó chamador
/// (nunca o do banco) — mesma semântica dos demais providers.
/// </summary>
internal sealed class MySqlJobStorage(
    MySqlDataSource dataSource, MySqlSchemaInitializer schema, string p, TimeProvider time) : IJobStorage
{
    private const string Columns =
        "id, descriptor, state, attempt, queue, created_at, scheduled_for, lease_until, finished_at, result, error";

    public ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct)
        => CreateCoreAsync(record, null, ct);

    public ValueTask<JobId> CreateAsync(JobRecord record, IGuaraTransaction transaction, CancellationToken ct)
        => CreateCoreAsync(record, RelationalTransaction.Require(transaction, "MySQL"), ct);

    private async ValueTask<JobId> CreateCoreAsync(
        JobRecord record, RelationalTransaction? transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        // O esquema roda na conexão própria do provider, fora da transação do chamador:
        // DDL lá dentro estenderia o alcance de um rollback dele até as tabelas do Guará.
        await schema.EnsureAsync(ct);

        // Sem transação a conexão é nossa e fecha aqui; com ela é emprestada de quem a
        // abriu e precisa continuar viva depois — por isso só a própria entra no descarte.
        var propria = transaction is null ? await dataSource.OpenConnectionAsync(ct) : null;
        await using var _ = propria;

        var connection = propria ?? transaction!.RequireConnection<MySqlConnection>("MySQL");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction?.RequireTransaction<MySqlTransaction>("MySQL");
        // Idempotente pelo id: recriar o mesmo job não duplica nem sobrescreve.
        command.CommandText = $"""
            INSERT IGNORE INTO {p}jobs ({Columns}, eligible_at)
            VALUES (@id, @descriptor, @state, @attempt, @queue, @createdAt, @scheduledFor, @leaseUntil,
                    @finishedAt, @result, @error, @eligibleAt)
            """;
        command.Parameters.AddWithValue("@eligibleAt", MySqlTime.ToDatabaseOrNull(JobEligibility.For(record)));
        command.Parameters.AddWithValue("@id", record.Id.Value);
        command.Parameters.AddWithValue(
            "@descriptor", JsonSerializer.Serialize(record.Descriptor, MySqlJsonContext.Default.JobDescriptor));
        command.Parameters.AddWithValue("@state", (int)record.State);
        command.Parameters.AddWithValue("@attempt", record.Attempt);
        command.Parameters.AddWithValue("@queue", record.Queue);
        command.Parameters.AddWithValue("@createdAt", MySqlTime.ToDatabase(record.CreatedAt));
        command.Parameters.AddWithValue("@scheduledFor", MySqlTime.ToDatabaseOrNull(record.ScheduledFor));
        command.Parameters.AddWithValue("@leaseUntil", MySqlTime.ToDatabaseOrNull(record.LeaseUntil));
        command.Parameters.AddWithValue("@finishedAt", MySqlTime.ToDatabaseOrNull(record.FinishedAt));
        command.Parameters.AddWithValue("@result", (object?)record.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("@error", (object?)record.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return record.Id;
    }

    public async ValueTask<IReadOnlyList<JobRecord>> AcquireNextDueAsync(
        string queue, int max, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        await schema.EnsureAsync(ct);
        var leaseUntil = now + lease;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // O MySQL não tem RETURNING: as linhas são travadas num SELECT e marcadas num
        // UPDATE, e a transação é o que impede outro nó de vê-las livres entre os dois
        // passos. É também o que o lote amortiza — uma transação passa a cobrir N jobs.
        await using var transaction = await connection.BeginTransactionAsync(ct);

        List<JobRecord> candidatos;
        // O reader vive no escopo do SELECT: a conexão só aceita o próximo comando depois
        // que ele fecha, então nada pode acontecer com a transação enquanto ele está aberto.
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            // SKIP LOCKED faz o nó pular linha já travada por outro em vez de esperar —
            // é o que troca contenção por vazão.
            select.CommandText = $"""
                SELECT {Columns} FROM {p}jobs
                WHERE queue = @queue AND eligible_at <= @now
                ORDER BY eligible_at
                LIMIT @max
                FOR UPDATE SKIP LOCKED
                """;
            select.Parameters.AddWithValue("@queue", queue);
            select.Parameters.AddWithValue("@now", MySqlTime.ToDatabase(now));
            select.Parameters.AddWithValue("@max", max);

            candidatos = new List<JobRecord>(max);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                candidatos.Add(ReadJob(reader));
            }
        }

        if (candidatos.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return [];
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            // Um UPDATE para o lote inteiro: os ids entram como parâmetros nomeados, e não
            // interpolados, para que a lista continue vindo do driver e não do texto.
            var alvos = string.Join(", ", candidatos.Select((_, i) => $"@id{i}"));
            update.CommandText =
                $"UPDATE {p}jobs SET state = @state, lease_until = @leaseUntil, eligible_at = @leaseUntil " +
                $"WHERE id IN ({alvos})";
            update.Parameters.AddWithValue("@state", (int)JobState.Processing);
            update.Parameters.AddWithValue("@leaseUntil", MySqlTime.ToDatabase(leaseUntil));
            for (var i = 0; i < candidatos.Count; i++)
            {
                update.Parameters.AddWithValue($"@id{i}", candidatos[i].Id.Value);
            }

            await update.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return [.. candidatos.Select(c => c with { State = JobState.Processing, LeaseUntil = leaseUntil })];
    }

    public async ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {p}jobs SET lease_until = @leaseUntil, eligible_at = @leaseUntil
            WHERE id = @id AND state = {(int)JobState.Processing} AND lease_until IS NOT NULL
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@leaseUntil", MySqlTime.ToDatabase(time.GetUtcNow() + lease));
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask ScheduleRetryAsync(JobId id, string error, DateTimeOffset retryAt, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {p}jobs
            SET state = {(int)JobState.Retrying}, error = @error, attempt = attempt + 1,
                scheduled_for = @retryAt, lease_until = NULL, eligible_at = @retryAt
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@error", error);
        command.Parameters.AddWithValue("@retryAt", MySqlTime.ToDatabase(retryAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask RescheduleAsync(JobId id, DateTimeOffset scheduledFor, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {p}jobs
            SET state = {(int)JobState.Scheduled}, scheduled_for = @scheduledFor, lease_until = NULL,
                eligible_at = @scheduledFor
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@scheduledFor", MySqlTime.ToDatabase(scheduledFor));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        // O MySQL aplica as atribuições do UPDATE da esquerda para a direita, e as seguintes
        // já enxergam o valor novo das anteriores: a coluna state é a última a mudar para
        // que nenhuma das demais possa ler o estado novo no lugar do antigo.
        command.CommandText = $"""
            UPDATE {p}jobs SET
                result = CASE WHEN @state = {(int)JobState.Succeeded} THEN @value ELSE result END,
                error = CASE WHEN @state IN ({(int)JobState.Failed}, {(int)JobState.Retrying})
                             THEN @value ELSE error END,
                lease_until = CASE WHEN @state = {(int)JobState.Processing} THEN lease_until ELSE NULL END,
                finished_at = CASE WHEN @state IN ({(int)JobState.Succeeded}, {(int)JobState.Failed})
                                   THEN COALESCE(finished_at, @now) ELSE finished_at END,
                eligible_at = CASE @state
                    WHEN {(int)JobState.Enqueued} THEN created_at
                    WHEN {(int)JobState.Scheduled} THEN scheduled_for
                    WHEN {(int)JobState.Retrying} THEN scheduled_for
                    WHEN {(int)JobState.Processing} THEN lease_until
                    ELSE NULL END,
                state = @state
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id.Value);
        command.Parameters.AddWithValue("@state", (int)state);
        command.Parameters.AddWithValue("@value", (object?)resultOrError ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", MySqlTime.ToDatabase(time.GetUtcNow()));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {p}jobs WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.Value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {p}jobs WHERE id = @id AND state <> {(int)JobState.Processing}";
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
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DELETE FROM {p}jobs WHERE state = @state AND finished_at IS NOT NULL AND finished_at < @cutoff";
        command.Parameters.AddWithValue("@state", (int)state);
        command.Parameters.AddWithValue("@cutoff", MySqlTime.ToDatabase(finishedBefore));
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<IReadOnlyDictionary<JobState, long>> CountByStateAsync(string? queue, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        if (queue is null)
        {
            command.CommandText = $"SELECT state, count(*) FROM {p}jobs GROUP BY state";
        }
        else
        {
            command.CommandText = $"SELECT state, count(*) FROM {p}jobs WHERE queue = @queue GROUP BY state";
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

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        var where = BuildWhere(query, command);
        command.CommandText = $"""
            SELECT {Columns} FROM {p}jobs {where}
            ORDER BY created_at DESC
            LIMIT @limit OFFSET @offset
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

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        var where = BuildWhere(query, command);
        command.CommandText = $"SELECT count(*) FROM {p}jobs {where}";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async ValueTask<IReadOnlyList<JobSeriesPoint>> GetSeriesAsync(
        JobSeriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await schema.EnsureAsync(ct);

        var queueFilter = query.Queue is null ? "" : " AND queue = @queue";

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();

        // O MySQL não tem percentile_disc nem WITHIN GROUP. O percentil discreto sai da
        // definição: ordena as durações do balde, e escolhe a de posição CEIL(p * n) — a
        // primeira cuja distribuição acumulada alcança p. A conta é decimal, não em ponto
        // flutuante, para o limite cair sempre na posição exata.
        //
        // O índice do balde sai de divisão inteira (DIV) em microssegundos pelo mesmo
        // motivo: job de fronteira não pode cair no balde vizinho.
        command.CommandText = $"""
            WITH finalizados AS (
                SELECT TIMESTAMPDIFF(MICROSECOND, @from, finished_at) DIV @bucket AS balde,
                       state,
                       TIMESTAMPDIFF(MICROSECOND, created_at, finished_at) AS duracao
                FROM {p}jobs
                WHERE state IN ({(int)JobState.Succeeded}, {(int)JobState.Failed})
                  AND finished_at >= @from AND finished_at < @to{queueFilter}
            ),
            ranqueados AS (
                SELECT balde, state, duracao,
                       ROW_NUMBER() OVER (PARTITION BY balde ORDER BY duracao) AS posicao,
                       COUNT(*) OVER (PARTITION BY balde) AS total
                FROM finalizados
            )
            SELECT balde,
                   count(CASE WHEN state = {(int)JobState.Succeeded} THEN 1 END),
                   count(CASE WHEN state = {(int)JobState.Failed} THEN 1 END),
                   max(CASE WHEN posicao = CEIL(0.50 * total) THEN duracao END),
                   max(CASE WHEN posicao = CEIL(0.95 * total) THEN duracao END)
            FROM ranqueados
            GROUP BY balde
            """;
        command.Parameters.AddWithValue("@from", MySqlTime.ToDatabase(query.From));
        command.Parameters.AddWithValue("@to", MySqlTime.ToDatabase(query.To));
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

    private static string BuildWhere(JobQuery query, MySqlCommand command)
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
            filters.Add("JSON_UNQUOTE(JSON_EXTRACT(descriptor, '$.typeName')) = @typeName");
            command.Parameters.AddWithValue("@typeName", typeName);
        }

        if (query.From is { } from)
        {
            filters.Add("created_at >= @from");
            command.Parameters.AddWithValue("@from", MySqlTime.ToDatabase(from));
        }

        if (query.To is { } to)
        {
            filters.Add("created_at < @to");
            command.Parameters.AddWithValue("@to", MySqlTime.ToDatabase(to));
        }

        if (query.Text is { Length: > 0 } text)
        {
            // O mesmo trecho procurado no id e nos nomes do descritor. As funções JSON
            // devolvem texto com collation binária, que compararia diferenciando maiúsculas:
            // LOWER dos dois lados deixa a busca insensível sem depender de collation.
            filters.Add("""
                (LOWER(id) LIKE @text ESCAPE '!'
                 OR LOWER(JSON_UNQUOTE(JSON_EXTRACT(descriptor, '$.typeName'))) LIKE @text ESCAPE '!'
                 OR LOWER(JSON_UNQUOTE(JSON_EXTRACT(descriptor, '$.methodName'))) LIKE @text ESCAPE '!')
                """);
            command.Parameters.AddWithValue("@text", $"%{Escape(text).ToLowerInvariant()}%");
        }

        return filters.Count > 0 ? $"WHERE {string.Join(" AND ", filters)}" : "";
    }

    // LIKE trata % e _ como curinga: o texto digitado pelo operador é literal. O escape é
    // '!' e não a contrabarra para o filtro não depender do modo NO_BACKSLASH_ESCAPES.
    private static string Escape(string text)
    {
        var escapado = new StringBuilder(text.Length);
        foreach (var caractere in text)
        {
            if (caractere is '%' or '_' or '!')
            {
                escapado.Append('!');
            }

            escapado.Append(caractere);
        }

        return escapado.ToString();
    }

    private static JobRecord ReadJob(MySqlDataReader reader) => new()
    {
        Id = new JobId(reader.GetString(0)),
        Descriptor = JsonSerializer.Deserialize(reader.GetString(1), MySqlJsonContext.Default.JobDescriptor)!,
        State = (JobState)reader.GetInt32(2),
        Attempt = reader.GetInt32(3),
        Queue = reader.GetString(4),
        CreatedAt = MySqlTime.FromDatabase(reader.GetDateTime(5)),
        ScheduledFor = reader.IsDBNull(6) ? null : MySqlTime.FromDatabase(reader.GetDateTime(6)),
        LeaseUntil = reader.IsDBNull(7) ? null : MySqlTime.FromDatabase(reader.GetDateTime(7)),
        FinishedAt = reader.IsDBNull(8) ? null : MySqlTime.FromDatabase(reader.GetDateTime(8)),
        Result = reader.IsDBNull(9) ? null : reader.GetString(9),
        Error = reader.IsDBNull(10) ? null : reader.GetString(10),
    };
}
