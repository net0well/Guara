using Guara.Storage;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>
/// Locks distribuídos com TTL sobre a tabela <c>locks</c>: a posse tem dono (token) e
/// validade — crash do dono libera pela expiração, e só o dono renova/libera.
/// </summary>
internal sealed class MySqlLockProvider(
    MySqlDataSource dataSource, MySqlSchemaInitializer schema, string p, TimeProvider time) : ILockProvider
{
    public async ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        var owner = Guid.NewGuid().ToString("n");
        var now = time.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();

        // Upsert numa instrução: a chave já existente só troca de dono se estiver vencida.
        // O ON DUPLICATE KEY resolve a corrida entre dois nós inserindo a mesma chave — o
        // segundo vira update em vez de erro de chave duplicada.
        command.CommandText = $"""
            INSERT INTO {p}locks (`key`, owner, expires_at)
            VALUES (@key, @owner, @expiresAt)
            ON DUPLICATE KEY UPDATE
                owner = IF(expires_at < @now, VALUES(owner), owner),
                expires_at = IF(expires_at < @now, VALUES(expires_at), expires_at)
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@expiresAt", MySqlTime.ToDatabase(now + ttl));
        command.Parameters.AddWithValue("@now", MySqlTime.ToDatabase(now));
        await command.ExecuteNonQueryAsync(ct);

        // O ON DUPLICATE KEY não distingue "tomei o lock vencido" de "o dono atual seguiu";
        // quem responde isso é a leitura do dono gravado.
        await using var confirm = connection.CreateCommand();
        confirm.CommandText = $"SELECT owner FROM {p}locks WHERE `key` = @key";
        confirm.Parameters.AddWithValue("@key", key);
        var dono = (string?)await confirm.ExecuteScalarAsync(ct);

        return dono == owner ? new MySqlLockHandle(dataSource, p, time, key, owner) : null;
    }

    private sealed class MySqlLockHandle(
        MySqlDataSource dataSource, string p, TimeProvider time, string key, string owner) : ILockHandle
    {
        public string Key => key;

        public async ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"UPDATE {p}locks SET expires_at = @expiresAt WHERE `key` = @key AND owner = @owner";
            command.Parameters.AddWithValue("@expiresAt", MySqlTime.ToDatabase(time.GetUtcNow() + ttl));
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@owner", owner);
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }

        public async ValueTask DisposeAsync()
        {
            // Liberação best-effort e não cancelável; se falhar, o TTL cobre.
            await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {p}locks WHERE `key` = @key AND owner = @owner";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@owner", owner);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
