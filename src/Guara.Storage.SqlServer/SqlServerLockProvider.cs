using Guara.Storage;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Locks distribuídos com TTL sobre a tabela <c>locks</c>: a posse tem dono (token) e
/// validade — crash do dono libera pela expiração, e só o dono renova/libera.
/// </summary>
internal sealed class SqlServerLockProvider(
    SqlServerConnections connections, SqlServerSchemaInitializer schema, string s, TimeProvider time) : ILockProvider
{
    public async ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        var owner = Guid.NewGuid().ToString("n");
        var now = time.GetUtcNow();

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // O SQL Server não tem upsert condicional numa instrução como o ON CONFLICT do
        // PostgreSQL. UPDLOCK + SERIALIZABLE trava a faixa da chave durante a checagem, o
        // que impede dois nós de inserirem a mesma chave na janela entre o teste e o insert.
        command.CommandText = $"""
            DECLARE @adquiriu int = 0;

            UPDATE {s}.locks WITH (UPDLOCK, SERIALIZABLE)
            SET owner = @owner, expires_at = @expiresAt
            WHERE [key] = @key AND expires_at < @now;
            SET @adquiriu = @@ROWCOUNT;

            IF @adquiriu = 0
            BEGIN
                INSERT INTO {s}.locks ([key], owner, expires_at)
                SELECT @key, @owner, @expiresAt
                WHERE NOT EXISTS (
                    SELECT 1 FROM {s}.locks WITH (UPDLOCK, SERIALIZABLE) WHERE [key] = @key);
                SET @adquiriu = @@ROWCOUNT;
            END

            SELECT @adquiriu;
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@expiresAt", now + ttl);
        command.Parameters.AddWithValue("@now", now);

        var adquiriu = (int)(await command.ExecuteScalarAsync(ct))! > 0;
        return adquiriu ? new SqlServerLockHandle(connections, s, time, key, owner) : null;
    }

    private sealed class SqlServerLockHandle(
        SqlServerConnections connections, string s, TimeProvider time, string key, string owner) : ILockHandle
    {
        public string Key => key;

        public async ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"UPDATE {s}.locks SET expires_at = @expiresAt WHERE [key] = @key AND owner = @owner";
            command.Parameters.AddWithValue("@expiresAt", time.GetUtcNow() + ttl);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@owner", owner);
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }

        public async ValueTask DisposeAsync()
        {
            // Liberação best-effort e não cancelável; se falhar, o TTL cobre.
            await using var connection = await connections.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {s}.locks WHERE [key] = @key AND owner = @owner";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@owner", owner);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
