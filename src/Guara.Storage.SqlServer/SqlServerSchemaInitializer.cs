using Guara.Abstractions;
using Microsoft.Data.SqlClient;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Garante o esquema no primeiro uso: DDL idempotente aplicada sob <c>sp_getapplock</c>
/// de sessão — N nós subindo juntos serializam a migração e todos enxergam o esquema
/// consistente. Com <see cref="SqlServerStorageOptions.AutoMigrate"/> desligado, assume
/// que o esquema já foi aplicado pelo pipeline.
/// </summary>
internal sealed class SqlServerSchemaInitializer(string connectionString, SqlServerStorageOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    public ValueTask EnsureAsync(CancellationToken ct)
        => _initialized ? ValueTask.CompletedTask : new ValueTask(InitializeAsync(ct));

    private async Task InitializeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            if (options.AutoMigrate)
            {
                await MigrateAsync(ct);
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MigrateAsync(CancellationToken ct)
    {
        // Recurso determinístico por schema: todos os nós do mesmo schema disputam o mesmo lock.
        var recurso = $"guara:migrate:{options.Schema}";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var acquire = connection.CreateCommand())
        {
            acquire.CommandText = """
                DECLARE @resultado int;
                EXEC @resultado = sp_getapplock
                    @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000;
                IF @resultado < 0
                    THROW 51000, 'Não foi possível obter o lock de migração do esquema do Guará.', 1;
                """;
            acquire.Parameters.AddWithValue("@recurso", recurso);
            await acquire.ExecuteNonQueryAsync(ct);
        }

        try
        {
            // Cada lote roda separado: o SQL Server exige que CREATE SCHEMA seja o primeiro
            // comando do seu lote, e não há GO fora das ferramentas de linha de comando.
            foreach (var lote in BuildDdl(options.Schema))
            {
                await using var ddl = connection.CreateCommand();
                ddl.CommandText = lote;
                await ddl.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            // A liberação não é cancelável: a sessão precisa devolver o lock antes de fechar.
            await using var release = connection.CreateCommand();
            release.CommandText = "EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';";
            release.Parameters.AddWithValue("@recurso", recurso);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// DDL em lotes idempotentes. Colunas de texto são dimensionadas: só o que é payload
    /// por natureza (descritor, resultado, erro, calendário) usa <c>nvarchar(max)</c>.
    /// </summary>
    private static IEnumerable<string> BuildDdl(string s)
    {
        yield return $"IF SCHEMA_ID('{s}') IS NULL EXEC('CREATE SCHEMA [{s}]');";

        yield return $"""
            IF OBJECT_ID('{s}.jobs') IS NULL
            CREATE TABLE {s}.jobs (
                id            nvarchar(200)     NOT NULL CONSTRAINT pk_jobs PRIMARY KEY,
                descriptor    nvarchar(max)     NOT NULL,
                state         int               NOT NULL,
                attempt       int               NOT NULL CONSTRAINT df_jobs_attempt DEFAULT 0,
                queue         nvarchar(200)     NOT NULL,
                created_at    datetimeoffset(7) NOT NULL,
                scheduled_for datetimeoffset(7) NULL,
                lease_until   datetimeoffset(7) NULL,
                finished_at   datetimeoffset(7) NULL,
                result        nvarchar(max)     NULL,
                error         nvarchar(max)     NULL,
                eligible_at   datetimeoffset(7) NULL
            );
            """;

        // Elegibilidade materializada: o instante em que o job passa a poder ser adquirido.
        // Sem ela, a aquisição vira disjunção sobre estados e o servidor precisa reunir três
        // faixas do índice e ordenar tudo para achar o primeiro — custo proporcional à
        // profundidade da fila. Cada passo é um lote à parte porque a coluna precisa existir
        // antes de ser referenciada.
        yield return $"""
            IF COL_LENGTH('{s}.jobs', 'eligible_at') IS NULL
            ALTER TABLE {s}.jobs ADD eligible_at datetimeoffset(7) NULL;
            """;

        yield return $"""
            UPDATE {s}.jobs SET eligible_at = CASE state
                WHEN {(int)JobState.Enqueued} THEN created_at
                WHEN {(int)JobState.Scheduled} THEN scheduled_for
                WHEN {(int)JobState.Retrying} THEN scheduled_for
                WHEN {(int)JobState.Processing} THEN lease_until
                ELSE NULL END
            WHERE eligible_at IS NULL
              AND state <> {(int)JobState.Succeeded} AND state <> {(int)JobState.Failed};
            """;

        yield return $"""
            IF INDEXPROPERTY(OBJECT_ID('{s}.jobs'), 'ix_jobs_due', 'IndexID') IS NULL
            CREATE INDEX ix_jobs_due ON {s}.jobs (queue, eligible_at);
            """;

        // O índice antigo cobria a disjunção que deixou de existir: manter só custaria
        // escrita a cada transição de estado, sem servir a nenhuma consulta.
        yield return $"""
            IF INDEXPROPERTY(OBJECT_ID('{s}.jobs'), 'ix_jobs_eligibility', 'IndexID') IS NOT NULL
            DROP INDEX ix_jobs_eligibility ON {s}.jobs;
            """;

        yield return $"""
            IF INDEXPROPERTY(OBJECT_ID('{s}.jobs'), 'ix_jobs_purge', 'IndexID') IS NULL
            CREATE INDEX ix_jobs_purge ON {s}.jobs (state, finished_at);
            """;

        yield return $"""
            IF OBJECT_ID('{s}.servers') IS NULL
            CREATE TABLE {s}.servers (
                id              nvarchar(200)     NOT NULL CONSTRAINT pk_servers PRIMARY KEY,
                machine_name    nvarchar(200)     NOT NULL,
                started_at      datetimeoffset(7) NOT NULL,
                last_heartbeat  datetimeoffset(7) NOT NULL,
                queues          nvarchar(2000)    NOT NULL CONSTRAINT df_servers_queues DEFAULT N'[]',
                max_concurrency int               NOT NULL CONSTRAINT df_servers_concurrency DEFAULT 0,
                roles           nvarchar(2000)    NOT NULL CONSTRAINT df_servers_roles DEFAULT N'[]'
            );
            """;

        // Papéis coordenados que o nó lidera. O default cobre a tabela pré-existente: nó que
        // ainda não reanunciou aparece sem papel, e o próximo anúncio corrige.
        yield return $"""
            IF COL_LENGTH('{s}.servers', 'roles') IS NULL
            ALTER TABLE {s}.servers ADD roles nvarchar(2000) NOT NULL CONSTRAINT df_servers_roles DEFAULT N'[]';
            """;

        yield return $"""
            IF OBJECT_ID('{s}.locks') IS NULL
            CREATE TABLE {s}.locks (
                [key]      nvarchar(400)     NOT NULL CONSTRAINT pk_locks PRIMARY KEY,
                owner      nvarchar(200)     NOT NULL,
                expires_at datetimeoffset(7) NOT NULL
            );
            """;

        yield return $"""
            IF OBJECT_ID('{s}.recurring') IS NULL
            CREATE TABLE {s}.recurring (
                id                       nvarchar(200)     NOT NULL CONSTRAINT pk_recurring PRIMARY KEY,
                descriptor               nvarchar(max)     NOT NULL,
                cron                     nvarchar(200)     NULL,
                interval_ticks           bigint            NULL,
                window_start_ticks       bigint            NULL,
                window_end_ticks         bigint            NULL,
                time_zone                nvarchar(100)     NULL,
                not_before               datetimeoffset(7) NULL,
                not_after                datetimeoffset(7) NULL,
                description              nvarchar(500)     NULL,
                queue                    nvarchar(200)     NOT NULL,
                calendar_name            nvarchar(200)     NULL,
                skip_if_previous_running bit               NOT NULL CONSTRAINT df_recurring_skip DEFAULT 0,
                paused                   bit               NOT NULL CONSTRAINT df_recurring_paused DEFAULT 0,
                created_at               datetimeoffset(7) NOT NULL,
                last_run_at              datetimeoffset(7) NULL,
                last_run_job_id          nvarchar(200)     NULL,
                next_run_at              datetimeoffset(7) NULL,
                last_skipped_at          datetimeoffset(7) NULL
            );
            """;

        yield return $"""
            IF INDEXPROPERTY(OBJECT_ID('{s}.recurring'), 'ix_recurring_due', 'IndexID') IS NULL
            CREATE INDEX ix_recurring_due ON {s}.recurring (paused, next_run_at);
            """;

        yield return $"""
            IF OBJECT_ID('{s}.calendars') IS NULL
            CREATE TABLE {s}.calendars (
                name    nvarchar(200) NOT NULL CONSTRAINT pk_calendars PRIMARY KEY,
                payload nvarchar(max) NOT NULL
            );
            """;

        yield return $"""
            IF OBJECT_ID('{s}.continuations') IS NULL
            CREATE TABLE {s}.continuations (
                child_id    nvarchar(200)     NOT NULL CONSTRAINT pk_continuations PRIMARY KEY,
                parent_id   nvarchar(200)     NOT NULL,
                fires_on    int               NOT NULL,
                status      int               NOT NULL,
                reason      nvarchar(max)     NULL,
                depth       int               NOT NULL CONSTRAINT df_continuations_depth DEFAULT 0,
                created_at  datetimeoffset(7) NOT NULL,
                resolved_at datetimeoffset(7) NULL
            );
            """;

        yield return $"""
            IF INDEXPROPERTY(OBJECT_ID('{s}.continuations'), 'ix_continuations_parent', 'IndexID') IS NULL
            CREATE INDEX ix_continuations_parent ON {s}.continuations (parent_id);
            """;

        // Índice filtrado: só as pendentes entram, que é o que a varredura de recuperação lê.
        yield return $"""
            IF INDEXPROPERTY(OBJECT_ID('{s}.continuations'), 'ix_continuations_pending', 'IndexID') IS NULL
            CREATE INDEX ix_continuations_pending ON {s}.continuations (status) WHERE status = 0;
            """;

        yield return $"""
            IF OBJECT_ID('{s}.schema_version') IS NULL
            CREATE TABLE {s}.schema_version (
                version    int               NOT NULL CONSTRAINT pk_schema_version PRIMARY KEY,
                applied_at datetimeoffset(7) NOT NULL
            );
            """;

        yield return $"""
            IF NOT EXISTS (SELECT 1 FROM {s}.schema_version WHERE version = 1)
            INSERT INTO {s}.schema_version (version, applied_at) VALUES (1, SYSDATETIMEOFFSET());
            """;
    }
}
