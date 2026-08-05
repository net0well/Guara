using Guara.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>
/// Locks distribuídos com TTL sobre a coleção <c>locks</c>: a posse tem dono (token) e
/// validade — crash do dono libera pela expiração, e só o dono renova/libera.
/// </summary>
internal sealed class MongoLockProvider(MongoCollections collections, TimeProvider time) : ILockProvider
{
    public async ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var owner = Guid.NewGuid().ToString("n");
        var now = time.GetUtcNow();

        try
        {
            // Upsert com filtro que só casa lock vencido: se a chave existe e ainda vale, o
            // filtro não casa e o upsert tenta inserir uma chave que já existe — a violação
            // do índice único do _id é exatamente o sinal de "o lock está com outro".
            await collections.Locks.UpdateOneAsync(
                new BsonDocument
                {
                    ["_id"] = key,
                    ["expiresAt"] = new BsonDocument("$lt", now.UtcTicks),
                },
                new BsonDocument("$set", new BsonDocument
                {
                    ["owner"] = owner,
                    ["expiresAt"] = (now + ttl).UtcTicks,
                }),
                new UpdateOptions { IsUpsert = true },
                ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return null;
        }

        return new MongoLockHandle(collections, time, key, owner);
    }

    private sealed class MongoLockHandle(
        MongoCollections collections, TimeProvider time, string key, string owner) : ILockHandle
    {
        public string Key => key;

        public async ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
        {
            var resultado = await collections.Locks.UpdateOneAsync(
                new BsonDocument { ["_id"] = key, ["owner"] = owner },
                new BsonDocument("$set", new BsonDocument("expiresAt", (time.GetUtcNow() + ttl).UtcTicks)),
                cancellationToken: ct);
            return resultado.MatchedCount > 0;
        }

        public async ValueTask DisposeAsync()
        {
            // Liberação best-effort e não cancelável; se falhar, o TTL cobre.
            await collections.Locks.DeleteOneAsync(
                new BsonDocument { ["_id"] = key, ["owner"] = owner }, CancellationToken.None);
        }
    }
}
