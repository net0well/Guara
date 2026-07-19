using Guara.Storage;
using Npgsql;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Locks distribuídos com TTL sobre a tabela <c>locks</c>: a posse tem dono (token) e
/// validade — crash do dono libera pela expiração, e só o dono renova/libera. O upsert
/// condicional decide atomicamente entre "chave livre", "posse expirada" (assume) e
/// "posse viva" (nega).
/// </summary>
internal sealed class PostgreSqlLockProvider(
    NpgsqlDataSource dataSource, PostgreSqlSchemaInitializer schema, string s, TimeProvider time) : ILockProvider
{
    public async ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        var owner = Guid.NewGuid().ToString("n");
        var now = time.GetUtcNow();

        await using var command = dataSource.CreateCommand($"""
            INSERT INTO {s}.locks AS locks (key, owner, expires_at)
            VALUES (@key, @owner, @expiresAt)
            ON CONFLICT (key) DO UPDATE SET owner = EXCLUDED.owner, expires_at = EXCLUDED.expires_at
            WHERE locks.expires_at < @now
            RETURNING 1
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("expiresAt", now + ttl);
        command.Parameters.AddWithValue("now", now);

        var acquired = await command.ExecuteScalarAsync(ct) is not null;
        return acquired ? new PostgreSqlLockHandle(dataSource, s, time, key, owner) : null;
    }

    private sealed class PostgreSqlLockHandle(
        NpgsqlDataSource dataSource, string s, TimeProvider time, string key, string owner) : ILockHandle
    {
        public string Key => key;

        public async ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
        {
            await using var command = dataSource.CreateCommand(
                $"UPDATE {s}.locks SET expires_at = @expiresAt WHERE key = @key AND owner = @owner");
            command.Parameters.AddWithValue("expiresAt", time.GetUtcNow() + ttl);
            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("owner", owner);
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }

        public async ValueTask DisposeAsync()
        {
            // Liberação best-effort e não cancelável; se falhar, o TTL cobre.
            await using var command = dataSource.CreateCommand(
                $"DELETE FROM {s}.locks WHERE key = @key AND owner = @owner");
            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("owner", owner);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
