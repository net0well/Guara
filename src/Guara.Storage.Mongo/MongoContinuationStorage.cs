using Guara.Abstractions;
using Guara.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>
/// Vínculos de continuação na coleção <c>continuations</c>. A resolução é um update
/// condicionado a <c>status = Pending</c>: entre nós concorrentes, exatamente um vence.
/// </summary>
internal sealed class MongoContinuationStorage(MongoCollections collections) : IContinuationStorage
{
    public async ValueTask AddAsync(ContinuationRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await collections.EnsureAsync(ct);

        // Inserção idempotente pelo id do filho: registrar duas vezes não duplica o vínculo.
        var documento = MongoDocuments.FromContinuation(record);
        documento.Remove("_id");
        await collections.Continuations.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", record.ChildId.Value),
            new BsonDocument("$setOnInsert", documento),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async ValueTask<ContinuationRecord?> GetByChildAsync(JobId childId, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documento = await collections.Continuations
            .Find(Builders<BsonDocument>.Filter.Eq("_id", childId.Value))
            .FirstOrDefaultAsync(ct);
        return documento is null ? null : MongoDocuments.ReadContinuation(documento);
    }

    public async ValueTask<IReadOnlyList<ContinuationRecord>> ListByParentAsync(JobId parentId, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documentos = await collections.Continuations
            .Find(Builders<BsonDocument>.Filter.Eq("parentId", parentId.Value))
            .Sort(Builders<BsonDocument>.Sort.Ascending("createdAt"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocuments.ReadContinuation)];
    }

    public async ValueTask<IReadOnlyList<ContinuationRecord>> ListPendingAsync(CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documentos = await collections.Continuations
            .Find(Builders<BsonDocument>.Filter.Eq("status", (int)ContinuationStatus.Pending))
            .Sort(Builders<BsonDocument>.Sort.Ascending("createdAt"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocuments.ReadContinuation)];
    }

    public async ValueTask<bool> TryResolveAsync(
        JobId childId, ContinuationStatus status, string? reason, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Continuations.UpdateOneAsync(
            new BsonDocument
            {
                ["_id"] = childId.Value,
                ["status"] = (int)ContinuationStatus.Pending,
            },
            new BsonDocument("$set", new BsonDocument
            {
                ["status"] = (int)status,
                ["reason"] = MongoDocuments.Text(reason),
                ["resolvedAt"] = resolvedAt.UtcTicks,
            }),
            cancellationToken: ct);
        return resultado.MatchedCount > 0;
    }
}
