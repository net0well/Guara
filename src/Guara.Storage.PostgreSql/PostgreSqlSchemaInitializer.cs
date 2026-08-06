using System.Security.Cryptography;
using System.Text;
using Guara.Abstractions;
using Npgsql;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Garante o esquema no primeiro uso: DDL 100% idempotente (IF NOT EXISTS) aplicada
/// sob advisory lock de sessão — N nós subindo juntos serializam a migração e todos
/// enxergam o esquema consistente. Com <see cref="PostgreSqlStorageOptions.AutoMigrate"/>
/// desligado, assume que o esquema já foi aplicado pelo pipeline.
/// </summary>
internal sealed class PostgreSqlSchemaInitializer(NpgsqlDataSource dataSource, PostgreSqlStorageOptions options)
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
        var lockKey = AdvisoryLockKey(options.Schema);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using (var acquire = connection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(@key)";
            acquire.Parameters.AddWithValue("key", lockKey);
            await acquire.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = BuildDdl(options.Schema);
            await ddl.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            // O unlock não é cancelável: a sessão precisa devolver o advisory lock.
            await using var release = connection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@key)";
            release.Parameters.AddWithValue("key", lockKey);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    // Chave determinística por schema: todos os nós do mesmo schema disputam o mesmo lock.
    private static long AdvisoryLockKey(string schema)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"guara:migrate:{schema}"));
        return BitConverter.ToInt64(hash, 0);
    }

    private static string BuildDdl(string s) => $"""
        CREATE SCHEMA IF NOT EXISTS {s};

        CREATE TABLE IF NOT EXISTS {s}.jobs (
            id            text PRIMARY KEY,
            descriptor    jsonb NOT NULL,
            state         int NOT NULL,
            attempt       int NOT NULL DEFAULT 0,
            queue         text NOT NULL,
            created_at    timestamptz NOT NULL,
            scheduled_for timestamptz NULL,
            lease_until   timestamptz NULL,
            finished_at   timestamptz NULL,
            result        text NULL,
            error         text NULL
        );
        -- Elegibilidade materializada: o instante em que o job passa a poder ser adquirido.
        -- Sem ela, a aquisição vira disjunção sobre estados, e o banco precisa unir três
        -- faixas do índice e ordenar tudo para achar o primeiro — custo proporcional à
        -- profundidade da fila. Com ela, é uma varredura ordenada que para na primeira linha.
        ALTER TABLE {s}.jobs ADD COLUMN IF NOT EXISTS eligible_at timestamptz NULL;

        UPDATE {s}.jobs SET eligible_at = CASE state
            WHEN {(int)JobState.Enqueued} THEN created_at
            WHEN {(int)JobState.Scheduled} THEN scheduled_for
            WHEN {(int)JobState.Retrying} THEN scheduled_for
            WHEN {(int)JobState.Processing} THEN lease_until
            ELSE NULL END
        WHERE eligible_at IS NULL AND state <> {(int)JobState.Succeeded} AND state <> {(int)JobState.Failed};

        CREATE INDEX IF NOT EXISTS ix_jobs_due ON {s}.jobs (queue, eligible_at);
        -- O índice antigo cobria a disjunção que deixou de existir: manter só custaria
        -- escrita a cada transição de estado, sem servir a nenhuma consulta.
        DROP INDEX IF EXISTS {s}.ix_jobs_eligibility;
        CREATE INDEX IF NOT EXISTS ix_jobs_purge ON {s}.jobs (state, finished_at);

        CREATE TABLE IF NOT EXISTS {s}.servers (
            id              text PRIMARY KEY,
            machine_name    text NOT NULL,
            started_at      timestamptz NOT NULL,
            last_heartbeat  timestamptz NOT NULL,
            queues          text[] NOT NULL DEFAULT ARRAY[]::text[],
            max_concurrency int NOT NULL DEFAULT 0,
            roles           text[] NOT NULL DEFAULT ARRAY[]::text[]
        );

        -- Papéis coordenados que o nó lidera. O default cobre a tabela pré-existente: nó que
        -- ainda não reanunciou aparece sem papel, e o próximo anúncio corrige.
        ALTER TABLE {s}.servers ADD COLUMN IF NOT EXISTS roles text[] NOT NULL DEFAULT ARRAY[]::text[];

        CREATE TABLE IF NOT EXISTS {s}.locks (
            key        text PRIMARY KEY,
            owner      text NOT NULL,
            expires_at timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS {s}.recurring (
            id                       text PRIMARY KEY,
            descriptor               jsonb NOT NULL,
            cron                     text NULL,
            interval_ticks           bigint NULL,
            window_start_ticks       bigint NULL,
            window_end_ticks         bigint NULL,
            time_zone                text NULL,
            not_before               timestamptz NULL,
            not_after                timestamptz NULL,
            description              text NULL,
            queue                    text NOT NULL,
            calendar_name            text NULL,
            skip_if_previous_running boolean NOT NULL DEFAULT false,
            paused                   boolean NOT NULL DEFAULT false,
            created_at               timestamptz NOT NULL,
            last_run_at              timestamptz NULL,
            last_run_job_id          text NULL,
            next_run_at              timestamptz NULL,
            last_skipped_at          timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_recurring_due ON {s}.recurring (paused, next_run_at);

        CREATE TABLE IF NOT EXISTS {s}.calendars (
            name    text PRIMARY KEY,
            payload jsonb NOT NULL
        );

        CREATE TABLE IF NOT EXISTS {s}.continuations (
            child_id    text PRIMARY KEY,
            parent_id   text NOT NULL,
            fires_on    int NOT NULL,
            status      int NOT NULL,
            reason      text NULL,
            depth       int NOT NULL DEFAULT 0,
            created_at  timestamptz NOT NULL,
            resolved_at timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_continuations_parent ON {s}.continuations (parent_id);
        CREATE INDEX IF NOT EXISTS ix_continuations_pending ON {s}.continuations (status) WHERE status = 0;

        CREATE TABLE IF NOT EXISTS {s}.schema_version (
            version    int PRIMARY KEY,
            applied_at timestamptz NOT NULL
        );
        INSERT INTO {s}.schema_version (version, applied_at) VALUES (1, now())
        ON CONFLICT (version) DO NOTHING;
        """;
}
