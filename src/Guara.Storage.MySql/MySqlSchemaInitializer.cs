using Guara.Abstractions;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>
/// Garante o esquema no primeiro uso: DDL idempotente aplicada sob <c>GET_LOCK</c> —
/// N nós subindo juntos serializam a migração e todos enxergam o esquema consistente.
/// Com <see cref="MySqlStorageOptions.AutoMigrate"/> desligado, assume que o esquema já
/// foi aplicado pelo pipeline.
/// </summary>
internal sealed class MySqlSchemaInitializer(MySqlDataSource dataSource, MySqlStorageOptions options)
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
        // Nome do lock determinístico por prefixo, dentro dos 64 caracteres que o MySQL aceita.
        var recurso = $"guara:migrate:{options.TablePrefix}";

        // O lock é da conexão: precisa ser a mesma do começo ao fim da migração.
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using (var acquire = connection.CreateCommand())
        {
            acquire.CommandText = "SELECT GET_LOCK(@recurso, 60)";
            acquire.Parameters.AddWithValue("@recurso", recurso);
            // GET_LOCK devolve 1 (obteve), 0 (estourou o tempo) ou NULL (erro).
            if (await acquire.ExecuteScalarAsync(ct) is not 1L)
            {
                throw new InvalidOperationException(
                    "Não foi possível obter o lock de migração do esquema do Guará.");
            }
        }

        try
        {
            // Um lote por comando: o MySQL só aceita múltiplas instruções por comando com
            // AllowUserVariables/AllowLoadLocalInfile ligados, o que não se deve exigir de
            // quem usa o Guará. Índices vão inline no CREATE TABLE porque o MySQL não tem
            // CREATE INDEX IF NOT EXISTS.
            foreach (var lote in BuildDdl(options.TablePrefix))
            {
                await using var ddl = connection.CreateCommand();
                ddl.CommandText = lote;
                await ddl.ExecuteNonQueryAsync(ct);
            }

            await MigrarElegibilidadeAsync(connection, options.TablePrefix, ct);
        }
        finally
        {
            // A liberação não é cancelável: a conexão precisa devolver o lock antes de voltar ao pool.
            await using var release = connection.CreateCommand();
            release.CommandText = "SELECT RELEASE_LOCK(@recurso)";
            release.Parameters.AddWithValue("@recurso", recurso);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// DDL em lotes idempotentes. Colunas de texto são dimensionadas: só o que é payload
    /// por natureza (resultado e erro) usa <c>longtext</c>. Descritor e calendário são
    /// <c>json</c>: numa coluna de texto o <c>JSON_EXTRACT</c> trataria o conteúdo como um
    /// escalar-string em vez de documento, e a busca por caminho voltaria vazia.
    /// </summary>
    /// <summary>
    /// Traz uma tabela já existente para o esquema com elegibilidade materializada.
    /// <para>
    /// O MySQL 8 não tem <c>ADD COLUMN IF NOT EXISTS</c>, e a forma condicional em SQL
    /// exigiria variáveis de usuário — que este provider não obriga ninguém a habilitar.
    /// A checagem acontece aqui, contra o <c>information_schema</c>, e cada passo só roda
    /// se faltar.
    /// </para>
    /// </summary>
    private static async Task MigrarElegibilidadeAsync(MySqlConnection connection, string p, CancellationToken ct)
    {
        if (!await ExisteAsync(connection, ct,
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
                $"AND table_name = '{p}jobs' AND column_name = 'eligible_at'"))
        {
            await ExecutarAsync(connection, ct, $"ALTER TABLE {p}jobs ADD COLUMN eligible_at datetime(6) NULL");
            await ExecutarAsync(connection, ct, $"""
                UPDATE {p}jobs SET eligible_at = CASE state
                    WHEN {(int)JobState.Enqueued} THEN created_at
                    WHEN {(int)JobState.Scheduled} THEN scheduled_for
                    WHEN {(int)JobState.Retrying} THEN scheduled_for
                    WHEN {(int)JobState.Processing} THEN lease_until
                    ELSE NULL END
                WHERE eligible_at IS NULL
                """);
        }

        if (!await ExisteAsync(connection, ct,
                "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() " +
                $"AND table_name = '{p}jobs' AND index_name = 'ix_{p}jobs_due'"))
        {
            await ExecutarAsync(connection, ct, $"CREATE INDEX ix_{p}jobs_due ON {p}jobs (queue, eligible_at)");
        }

        // O índice antigo cobria a disjunção que deixou de existir: manter só custaria
        // escrita a cada transição de estado, sem servir a nenhuma consulta.
        if (await ExisteAsync(connection, ct,
                "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() " +
                $"AND table_name = '{p}jobs' AND index_name = 'ix_{p}jobs_eligibility'"))
        {
            await ExecutarAsync(connection, ct, $"DROP INDEX ix_{p}jobs_eligibility ON {p}jobs");
        }
    }

    private static async Task<bool> ExisteAsync(MySqlConnection connection, CancellationToken ct, string consulta)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = consulta;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static async Task ExecutarAsync(MySqlConnection connection, CancellationToken ct, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static IEnumerable<string> BuildDdl(string p)
    {
        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}jobs (
                id            varchar(200) NOT NULL,
                descriptor    json         NOT NULL,
                state         int          NOT NULL,
                attempt       int          NOT NULL DEFAULT 0,
                queue         varchar(200) NOT NULL,
                created_at    datetime(6)  NOT NULL,
                scheduled_for datetime(6)  NULL,
                lease_until   datetime(6)  NULL,
                finished_at   datetime(6)  NULL,
                result        longtext     NULL,
                error         longtext     NULL,
                eligible_at   datetime(6)  NULL,
                PRIMARY KEY (id),
                INDEX ix_{p}jobs_due (queue, eligible_at),
                INDEX ix_{p}jobs_purge (state, finished_at)
            ) ENGINE=InnoDB
            """;

        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}servers (
                id              varchar(200)  NOT NULL,
                machine_name    varchar(200)  NOT NULL,
                started_at      datetime(6)   NOT NULL,
                last_heartbeat  datetime(6)   NOT NULL,
                queues          varchar(2000) NOT NULL DEFAULT '[]',
                max_concurrency int           NOT NULL DEFAULT 0,
                PRIMARY KEY (id)
            ) ENGINE=InnoDB
            """;

        // `key` é palavra reservada no MySQL: a coluna vive entre crases em toda consulta.
        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}locks (
                `key`      varchar(400) NOT NULL,
                owner      varchar(200) NOT NULL,
                expires_at datetime(6)  NOT NULL,
                PRIMARY KEY (`key`)
            ) ENGINE=InnoDB
            """;

        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}recurring (
                id                       varchar(200) NOT NULL,
                descriptor               json         NOT NULL,
                cron                     varchar(200) NULL,
                interval_ticks           bigint       NULL,
                window_start_ticks       bigint       NULL,
                window_end_ticks         bigint       NULL,
                time_zone                varchar(100) NULL,
                not_before               datetime(6)  NULL,
                not_after                datetime(6)  NULL,
                description              varchar(500) NULL,
                queue                    varchar(200) NOT NULL,
                calendar_name            varchar(200) NULL,
                skip_if_previous_running tinyint(1)   NOT NULL DEFAULT 0,
                paused                   tinyint(1)   NOT NULL DEFAULT 0,
                created_at               datetime(6)  NOT NULL,
                last_run_at              datetime(6)  NULL,
                last_run_job_id          varchar(200) NULL,
                next_run_at              datetime(6)  NULL,
                last_skipped_at          datetime(6)  NULL,
                PRIMARY KEY (id),
                INDEX ix_{p}recurring_due (paused, next_run_at)
            ) ENGINE=InnoDB
            """;

        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}calendars (
                name    varchar(200) NOT NULL,
                payload json         NOT NULL,
                PRIMARY KEY (name)
            ) ENGINE=InnoDB
            """;

        // Sem índice parcial no MySQL: o índice cobre status inteiro, e a varredura de
        // pendentes continua sendo uma faixa contígua dele.
        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}continuations (
                child_id    varchar(200) NOT NULL,
                parent_id   varchar(200) NOT NULL,
                fires_on    int          NOT NULL,
                status      int          NOT NULL,
                reason      longtext     NULL,
                depth       int          NOT NULL DEFAULT 0,
                created_at  datetime(6)  NOT NULL,
                resolved_at datetime(6)  NULL,
                PRIMARY KEY (child_id),
                INDEX ix_{p}continuations_parent (parent_id),
                INDEX ix_{p}continuations_pending (status, created_at)
            ) ENGINE=InnoDB
            """;

        yield return $"""
            CREATE TABLE IF NOT EXISTS {p}schema_version (
                version    int         NOT NULL,
                applied_at datetime(6) NOT NULL,
                PRIMARY KEY (version)
            ) ENGINE=InnoDB
            """;

        yield return $"INSERT IGNORE INTO {p}schema_version (version, applied_at) VALUES (1, UTC_TIMESTAMP(6))";
    }
}
