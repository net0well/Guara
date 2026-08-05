using Guara.Abstractions;
using Guara.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>Introspecção de filas derivada da coleção de jobs.</summary>
internal sealed class MongoQueueStorage(MongoCollections collections) : IQueueStorage
{
    public async ValueTask<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var filas = await collections.Jobs.DistinctAsync<string>("queue", new BsonDocument(), cancellationToken: ct);
        var resultado = await filas.ToListAsync(ct);
        resultado.Sort(StringComparer.Ordinal);
        return resultado;
    }

    public async ValueTask<long> GetLengthAsync(string queue, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        return await collections.Jobs.CountDocumentsAsync(
            new BsonDocument { ["queue"] = queue, ["state"] = (int)JobState.Enqueued },
            cancellationToken: ct);
    }
}
