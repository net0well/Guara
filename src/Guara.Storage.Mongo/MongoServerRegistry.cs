using Guara.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>Registro de nós servidores na coleção <c>servers</c> (upsert por id).</summary>
internal sealed class MongoServerRegistry(MongoCollections collections) : IServerRegistry
{
    public async ValueTask AnnounceAsync(ServerNode node, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        await collections.EnsureAsync(ct);

        var documento = MongoDocuments.FromServer(node);
        documento.Remove("_id");
        await collections.Servers.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", node.Id),
            new BsonDocument("$set", documento),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async ValueTask<bool> HeartbeatAsync(string serverId, DateTimeOffset now, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Servers.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", serverId),
            new BsonDocument("$set", new BsonDocument("lastHeartbeat", now.UtcTicks)),
            cancellationToken: ct);
        return resultado.MatchedCount > 0;
    }

    public async ValueTask RemoveAsync(string serverId, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        await collections.Servers.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", serverId), ct);
    }

    public async ValueTask<IReadOnlyList<ServerNode>> ListAsync(CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documentos = await collections.Servers
            .Find(new BsonDocument())
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocuments.ReadServer)];
    }

    public async ValueTask<int> RemoveExpiredAsync(DateTimeOffset heartbeatBefore, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Servers.DeleteManyAsync(
            new BsonDocument("lastHeartbeat", new BsonDocument("$lt", heartbeatBefore.UtcTicks)), ct);
        return (int)resultado.DeletedCount;
    }
}
