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
        var documento = MongoDocumentMapper.FromContinuation(record);
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
        return documento is null ? null : MongoDocumentMapper.ReadContinuation(documento);
    }

    public async ValueTask<IReadOnlyList<ContinuationRecord>> ListByParentAsync(JobId parentId, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documentos = await collections.Continuations
            .Find(Builders<BsonDocument>.Filter.Eq("parentId", parentId.Value))
            .Sort(Builders<BsonDocument>.Sort.Ascending("createdAt"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocumentMapper.ReadContinuation)];
    }

    public async ValueTask<IReadOnlyList<ResolvableContinuation>> ListResolvablePendingAsync(
        int max, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        await collections.EnsureAsync(ct);

        // $lookup é o equivalente ao LEFT JOIN dos relacionais, e é o que permite filtrar
        // pelo desfecho do pai no servidor. Sem ele, limitar por quantidade traria os
        // pendentes mais antigos — quase todos com pai ainda rodando — e os resolvíveis
        // poderiam nunca aparecer.
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("status", (int)ContinuationStatus.Pending)),
            new("$sort", new BsonDocument("createdAt", 1)),
            new("$lookup", new BsonDocument
            {
                ["from"] = collections.Jobs.CollectionNamespace.CollectionName,
                ["localField"] = "parentId",
                ["foreignField"] = "_id",
                ["as"] = "pai",
            }),
            new("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("pai", new BsonDocument("$size", 0)),
                new BsonDocument("pai.state", (int)JobState.Succeeded),
                new BsonDocument("pai.state", (int)JobState.Failed),
            })),
            new("$limit", max),
        };

        var documentos = await collections.Continuations
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);

        var resolviveis = new List<ResolvableContinuation>(documentos.Count);
        foreach (var documento in documentos)
        {
            var pai = documento["pai"].AsBsonArray;
            resolviveis.Add(new ResolvableContinuation
            {
                Continuation = MongoDocumentMapper.ReadContinuation(documento),
                ParentState = pai.Count == 0 ? null : (JobState)pai[0]["state"].AsInt32,
            });
        }

        return resolviveis;
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
                ["reason"] = MongoDocumentMapper.Text(reason),
                ["resolvedAt"] = resolvedAt.UtcTicks,
            }),
            cancellationToken: ct);
        return resultado.MatchedCount > 0;
    }
}
