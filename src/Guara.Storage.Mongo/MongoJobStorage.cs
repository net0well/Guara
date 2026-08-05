using System.Text.RegularExpressions;
using Guara.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>
/// Persistência de jobs sobre MongoDB. A aquisição é um <c>findAndModify</c>: o servidor
/// casa, atualiza e devolve o documento numa única operação atômica, então dois nós nunca
/// levam o mesmo job — quem chega depois simplesmente não casa mais com o filtro. Toda
/// comparação temporal usa o relógio <b>injetado</b> do nó chamador (nunca o do banco) —
/// mesma semântica dos demais providers.
/// </summary>
internal sealed class MongoJobStorage(MongoCollections collections, TimeProvider time) : IJobStorage
{
    public ValueTask<JobId> CreateAsync(JobRecord record, IGuaraTransaction transaction, CancellationToken ct)
        => throw new NotSupportedException(
            "MongoStorage não participa de transação do chamador " +
            "(Capabilities.SupportsTransactions = false): transação multi-documento no MongoDB " +
            "exige replica set, e um servidor standalone não a oferece. Para enfileirar junto com " +
            "a gravação do negócio, use um provider relacional.");

    public async ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await collections.EnsureAsync(ct);

        // Idempotente pelo id: recriar o mesmo job não duplica nem sobrescreve. O upsert
        // com $setOnInsert só grava quando o documento ainda não existe.
        var documento = MongoDocuments.FromJob(record);
        documento.Remove("_id");
        await collections.Jobs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", record.Id.Value),
            new BsonDocument("$setOnInsert", documento),
            new UpdateOptions { IsUpsert = true },
            ct);
        return record.Id;
    }

    public async ValueTask<JobRecord?> AcquireNextDueAsync(
        string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var agora = now.UtcTicks;

        // scheduledFor/leaseUntil nulos não casam com uma comparação numérica: o MongoDB só
        // compara dentro do mesmo tipo BSON, então nulo fica de fora sem cláusula extra.
        var filtro = new BsonDocument
        {
            ["queue"] = queue,
            ["$or"] = new BsonArray
            {
                new BsonDocument("state", (int)JobState.Enqueued),
                new BsonDocument
                {
                    ["state"] = new BsonDocument("$in",
                        new BsonArray { (int)JobState.Scheduled, (int)JobState.Retrying }),
                    ["scheduledFor"] = new BsonDocument("$lte", agora),
                },
                new BsonDocument
                {
                    ["state"] = (int)JobState.Processing,
                    ["leaseUntil"] = new BsonDocument("$lt", agora),
                },
            },
        };

        var documento = await collections.Jobs.FindOneAndUpdateAsync<BsonDocument>(
            filtro,
            new BsonDocument("$set", new BsonDocument
            {
                ["state"] = (int)JobState.Processing,
                ["leaseUntil"] = (now + lease).UtcTicks,
            }),
            new FindOneAndUpdateOptions<BsonDocument>
            {
                // A fila é FIFO por criação; o documento volta já com o estado novo.
                Sort = Builders<BsonDocument>.Sort.Ascending("createdAt"),
                ReturnDocument = ReturnDocument.After,
            },
            ct);

        return documento is null ? null : MongoDocuments.ReadJob(documento);
    }

    public async ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Jobs.UpdateOneAsync(
            new BsonDocument
            {
                ["_id"] = id.Value,
                ["state"] = (int)JobState.Processing,
                ["leaseUntil"] = new BsonDocument("$ne", BsonNull.Value),
            },
            new BsonDocument("$set", new BsonDocument("leaseUntil", (time.GetUtcNow() + lease).UtcTicks)),
            cancellationToken: ct);
        return resultado.MatchedCount > 0;
    }

    public async ValueTask ScheduleRetryAsync(JobId id, string error, DateTimeOffset retryAt, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        await collections.Jobs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.Value),
            new BsonDocument
            {
                ["$set"] = new BsonDocument
                {
                    ["state"] = (int)JobState.Retrying,
                    ["error"] = error,
                    ["scheduledFor"] = retryAt.UtcTicks,
                    ["leaseUntil"] = BsonNull.Value,
                },
                ["$inc"] = new BsonDocument("attempt", 1),
            },
            cancellationToken: ct);
    }

    public async ValueTask RescheduleAsync(JobId id, DateTimeOffset scheduledFor, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        await collections.Jobs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.Value),
            new BsonDocument("$set", new BsonDocument
            {
                ["state"] = (int)JobState.Scheduled,
                ["scheduledFor"] = scheduledFor.UtcTicks,
                ["leaseUntil"] = BsonNull.Value,
            }),
            cancellationToken: ct);
    }

    public async ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);

        var set = new BsonDocument { ["state"] = (int)state };

        if (state is JobState.Succeeded)
        {
            // $literal protege o valor do usuário: num pipeline de update, um texto que
            // começa com '$' seria interpretado como caminho de campo.
            set["result"] = new BsonDocument("$literal", MongoDocuments.Text(resultOrError));
        }

        if (state is JobState.Failed or JobState.Retrying)
        {
            set["error"] = new BsonDocument("$literal", MongoDocuments.Text(resultOrError));
        }

        if (state is not JobState.Processing)
        {
            set["leaseUntil"] = BsonNull.Value;
        }

        if (state is JobState.Succeeded or JobState.Failed)
        {
            // Só o primeiro término conta: reprocessar não reescreve o instante original.
            set["finishedAt"] = new BsonDocument("$ifNull",
                new BsonArray { "$finishedAt", time.GetUtcNow().UtcTicks });
        }

        // Pipeline de update porque finishedAt depende do valor atual do próprio documento.
        // Ao contrário do MySQL, um único estágio avalia tudo contra o documento de entrada,
        // então a ordem dos campos aqui não importa.
        await collections.Jobs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.Value),
            PipelineDefinition<BsonDocument, BsonDocument>.Create(new BsonDocument("$set", set)),
            cancellationToken: ct);
    }

    public async ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documento = await collections.Jobs
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id.Value))
            .FirstOrDefaultAsync(ct);
        return documento is null ? null : MongoDocuments.ReadJob(documento);
    }

    public async ValueTask<bool> DeleteAsync(JobId id, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Jobs.DeleteOneAsync(
            new BsonDocument
            {
                ["_id"] = id.Value,
                ["state"] = new BsonDocument("$ne", (int)JobState.Processing),
            }, ct);
        return resultado.DeletedCount > 0;
    }

    public async ValueTask<int> PurgeAsync(JobState state, DateTimeOffset finishedBefore, CancellationToken ct)
    {
        if (state is not (JobState.Succeeded or JobState.Failed))
        {
            throw new ArgumentException(
                $"Apenas estados terminais (Succeeded/Failed) podem ser purgados; recebido: {state}.", nameof(state));
        }

        await collections.EnsureAsync(ct);
        var resultado = await collections.Jobs.DeleteManyAsync(
            new BsonDocument
            {
                ["state"] = (int)state,
                ["finishedAt"] = new BsonDocument("$lt", finishedBefore.UtcTicks),
            }, ct);
        return (int)resultado.DeletedCount;
    }

    public async ValueTask<IReadOnlyDictionary<JobState, long>> CountByStateAsync(string? queue, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);

        var estagios = new List<BsonDocument>();
        if (queue is not null)
        {
            estagios.Add(new BsonDocument("$match", new BsonDocument("queue", queue)));
        }

        estagios.Add(new BsonDocument("$group", new BsonDocument
        {
            ["_id"] = "$state",
            ["total"] = new BsonDocument("$sum", 1),
        }));

        var counts = new Dictionary<JobState, long>();
        using var cursor = await collections.Jobs.AggregateAsync<BsonDocument>(
            PipelineDefinition<BsonDocument, BsonDocument>.Create(estagios), cancellationToken: ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var documento in cursor.Current)
            {
                counts[(JobState)documento["_id"].AsInt32] = documento["total"].ToInt64();
            }
        }

        return counts;
    }

    public async ValueTask<IReadOnlyList<JobRecord>> ListAsync(JobQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await collections.EnsureAsync(ct);
        var page = query.EffectivePage;
        var pageSize = query.EffectivePageSize;

        var documentos = await collections.Jobs
            .Find(BuildFilter(query))
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return [.. documentos.Select(MongoDocuments.ReadJob)];
    }

    public async ValueTask<long> CountAsync(JobQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await collections.EnsureAsync(ct);
        return await collections.Jobs.CountDocumentsAsync(BuildFilter(query), cancellationToken: ct);
    }

    public async ValueTask<IReadOnlyList<JobSeriesPoint>> GetSeriesAsync(
        JobSeriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await collections.EnsureAsync(ct);

        var de = query.From.UtcTicks;
        var bucket = query.Bucket.Ticks;

        var filtro = new BsonDocument
        {
            ["state"] = new BsonDocument("$in",
                new BsonArray { (int)JobState.Succeeded, (int)JobState.Failed }),
            ["finishedAt"] = new BsonDocument { ["$gte"] = de, ["$lt"] = query.To.UtcTicks },
        };
        if (query.Queue is { } fila)
        {
            filtro["queue"] = fila;
        }

        // O índice do balde vem da diferença já subtraída, e não do tick absoluto: o
        // $divide do MongoDB opera em ponto flutuante, e o tick absoluto (~6e17) passa da
        // faixa de inteiros exatos do double, enquanto a diferença cabe com folga.
        var estagios = new List<BsonDocument>
        {
            new("$match", filtro),
            new("$project", new BsonDocument
            {
                ["balde"] = new BsonDocument("$floor", new BsonDocument("$divide",
                    new BsonArray { new BsonDocument("$subtract", new BsonArray { "$finishedAt", de }), bucket })),
                ["state"] = 1,
                ["duracao"] = new BsonDocument("$subtract", new BsonArray { "$finishedAt", "$createdAt" }),
            }),
            new("$group", new BsonDocument
            {
                ["_id"] = "$balde",
                ["succeeded"] = new BsonDocument("$sum", new BsonDocument("$cond",
                    new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$state", (int)JobState.Succeeded }), 1, 0,
                    })),
                ["failed"] = new BsonDocument("$sum", new BsonDocument("$cond",
                    new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$state", (int)JobState.Failed }), 1, 0,
                    })),
                ["duracoes"] = new BsonDocument("$push", "$duracao"),
            }),
            // O acumulador $percentile do MongoDB é aproximado. O percentil aqui é discreto
            // e exato: ordena as durações do balde e pega a de posição CEIL(p * n) — a
            // primeira cuja distribuição acumulada alcança p, igual aos demais providers.
            new("$project", new BsonDocument
            {
                ["succeeded"] = 1,
                ["failed"] = 1,
                ["p50"] = PercentilDiscreto(0.50),
                ["p95"] = PercentilDiscreto(0.95),
            }),
        };

        var baldes = new Dictionary<long, (long Succeeded, long Failed, TimeSpan? P50, TimeSpan? P95)>();
        using (var cursor = await collections.Jobs.AggregateAsync<BsonDocument>(
            PipelineDefinition<BsonDocument, BsonDocument>.Create(estagios), cancellationToken: ct))
        {
            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var documento in cursor.Current)
                {
                    baldes[documento["_id"].ToInt64()] = (
                        documento["succeeded"].ToInt64(),
                        documento["failed"].ToInt64(),
                        LerDuracao(documento["p50"]),
                        LerDuracao(documento["p95"]));
                }
            }
        }

        // A janela volta contínua: balde sem job finalizado é ponto zerado, não buraco.
        var points = new List<JobSeriesPoint>();
        var indice = 0L;
        foreach (var inicio in query.Buckets())
        {
            points.Add(baldes.TryGetValue(indice, out var balde)
                ? new JobSeriesPoint(inicio, balde.Succeeded, balde.Failed, balde.P50, balde.P95)
                : new JobSeriesPoint(inicio, 0, 0, null, null));
            indice++;
        }

        return points;
    }

    private static BsonDocument PercentilDiscreto(double percentil) =>
        new("$let", new BsonDocument
        {
            ["vars"] = new BsonDocument
            {
                ["ordenadas"] = new BsonDocument("$sortArray", new BsonDocument
                {
                    ["input"] = "$duracoes",
                    ["sortBy"] = 1,
                }),
            },
            ["in"] = new BsonDocument("$arrayElemAt", new BsonArray
            {
                "$$ordenadas",
                new BsonDocument("$subtract", new BsonArray
                {
                    new BsonDocument("$toInt", new BsonDocument("$ceil", new BsonDocument("$multiply",
                        new BsonArray { percentil, new BsonDocument("$size", "$$ordenadas") }))),
                    1,
                }),
            }),
        });

    private static TimeSpan? LerDuracao(BsonValue valor)
        => valor.IsBsonNull ? null : TimeSpan.FromTicks(valor.ToInt64());

    private static BsonDocument BuildFilter(JobQuery query)
    {
        var filtro = new BsonDocument();

        if (query.State is { } state)
        {
            filtro["state"] = (int)state;
        }

        if (query.Queue is { } queue)
        {
            filtro["queue"] = queue;
        }

        if (query.TypeName is { } typeName)
        {
            filtro["descriptor.typeName"] = typeName;
        }

        if (query.From is not null || query.To is not null)
        {
            var faixa = new BsonDocument();
            if (query.From is { } inicio)
            {
                faixa["$gte"] = inicio.UtcTicks;
            }

            if (query.To is { } fim)
            {
                faixa["$lt"] = fim.UtcTicks;
            }

            filtro["createdAt"] = faixa;
        }

        if (query.Text is { Length: > 0 } text)
        {
            // O mesmo trecho procurado no id e nos nomes do descritor, sem diferenciar
            // maiúsculas. O texto do operador é literal: Escape neutraliza qualquer
            // metacaractere antes de virar expressão regular.
            var padrao = new BsonRegularExpression(Regex.Escape(text), "i");
            filtro["$or"] = new BsonArray
            {
                new BsonDocument("_id", padrao),
                new BsonDocument("descriptor.typeName", padrao),
                new BsonDocument("descriptor.methodName", padrao),
            };
        }

        return filtro;
    }
}
