using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>
/// As coleções do Guará e a criação dos índices no primeiro uso. A criação de índice no
/// MongoDB é idempotente pelo nome e segura entre nós concorrentes, então não há lock de
/// migração como nos providers relacionais — N nós subindo juntos convergem sozinhos.
/// </summary>
internal sealed class MongoCollections
{
    private readonly MongoStorageOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    public MongoCollections(IMongoDatabase database, MongoStorageOptions options)
    {
        _options = options;
        var p = options.CollectionPrefix;

        Jobs = database.GetCollection<BsonDocument>($"{p}jobs");
        Servers = database.GetCollection<BsonDocument>($"{p}servers");
        Locks = database.GetCollection<BsonDocument>($"{p}locks");
        Recurring = database.GetCollection<BsonDocument>($"{p}recurring");
        Calendars = database.GetCollection<BsonDocument>($"{p}calendars");
        Continuations = database.GetCollection<BsonDocument>($"{p}continuations");
    }

    public IMongoCollection<BsonDocument> Jobs { get; }

    public IMongoCollection<BsonDocument> Servers { get; }

    public IMongoCollection<BsonDocument> Locks { get; }

    public IMongoCollection<BsonDocument> Recurring { get; }

    public IMongoCollection<BsonDocument> Calendars { get; }

    public IMongoCollection<BsonDocument> Continuations { get; }

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

            if (_options.AutoMigrate)
            {
                await CreateIndexesAsync(ct);
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CreateIndexesAsync(CancellationToken ct)
    {
        var jobs = Builders<BsonDocument>.IndexKeys;
        await Jobs.Indexes.CreateManyAsync(
            [
                // Cobre a busca do próximo job elegível: um único intervalo ordenado por
                // elegibilidade, que já é a ordem em que a fila entrega.
                new CreateIndexModel<BsonDocument>(
                    jobs.Ascending("queue").Ascending("eligibleAt"),
                    new CreateIndexOptions { Name = "ix_due" }),
                new CreateIndexModel<BsonDocument>(
                    jobs.Ascending("state").Ascending("finishedAt"),
                    new CreateIndexOptions { Name = "ix_purge" }),
                new CreateIndexModel<BsonDocument>(
                    jobs.Descending("createdAt"),
                    new CreateIndexOptions { Name = "ix_listagem" }),
            ], ct);

        var recurring = Builders<BsonDocument>.IndexKeys;
        await Recurring.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                recurring.Ascending("paused").Ascending("nextRunAt"),
                new CreateIndexOptions { Name = "ix_due" }),
            cancellationToken: ct);

        var continuations = Builders<BsonDocument>.IndexKeys;
        await Continuations.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<BsonDocument>(
                    continuations.Ascending("parentId"),
                    new CreateIndexOptions { Name = "ix_parent" }),
                // Índice parcial: só as pendentes entram, que é o que a varredura de
                // recuperação lê — as resolvidas nunca aparecem nessa consulta.
                new CreateIndexModel<BsonDocument>(
                    continuations.Ascending("status").Ascending("createdAt"),
                    new CreateIndexOptions<BsonDocument>
                    {
                        Name = "ix_pending",
                        PartialFilterExpression =
                            new BsonDocument("status", (int)ContinuationStatus.Pending),
                    }),
            ], ct);

        await Servers.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("lastHeartbeat"),
                new CreateIndexOptions { Name = "ix_heartbeat" }),
            cancellationToken: ct);
    }
}
