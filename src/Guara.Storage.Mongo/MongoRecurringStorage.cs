using Guara.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Guara.Storage.Mongo;

/// <summary>
/// Definições recorrentes e calendários. Agenda por intervalo e janela diária são
/// persistidas em ticks (round-trip exato de <see cref="TimeSpan"/>/<see cref="TimeOnly"/>).
/// </summary>
internal sealed class MongoRecurringStorage(MongoCollections collections) : IRecurringStorage
{
    public async ValueTask UpsertAsync(RecurringJobRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await collections.EnsureAsync(ct);

        var documento = MongoDocuments.FromRecurring(record);
        documento.Remove("_id");
        await collections.Recurring.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", record.Id),
            new BsonDocument("$set", documento),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async ValueTask<RecurringJobRecord?> GetAsync(string id, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documento = await collections.Recurring
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .FirstOrDefaultAsync(ct);
        return documento is null ? null : MongoDocuments.ReadRecurring(documento);
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Recurring.DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id), ct);
        return resultado.DeletedCount > 0;
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListAsync(CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documentos = await collections.Recurring
            .Find(new BsonDocument())
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocuments.ReadRecurring)];
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListDueAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        // nextRunAt nulo não casa com a comparação numérica: o MongoDB só compara dentro do
        // mesmo tipo BSON, então recorrente sem próxima execução fica fora sem cláusula extra.
        var documentos = await collections.Recurring
            .Find(new BsonDocument
            {
                ["paused"] = false,
                ["nextRunAt"] = new BsonDocument("$lte", now.UtcTicks),
            })
            .Sort(Builders<BsonDocument>.Sort.Ascending("nextRunAt"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocuments.ReadRecurring)];
    }

    public async ValueTask UpsertCalendarAsync(CalendarRecord calendar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        await collections.EnsureAsync(ct);

        var documento = MongoDocuments.FromCalendar(calendar);
        documento.Remove("_id");
        await collections.Calendars.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", calendar.Name),
            new BsonDocument("$set", documento),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async ValueTask<CalendarRecord?> GetCalendarAsync(string name, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documento = await collections.Calendars
            .Find(Builders<BsonDocument>.Filter.Eq("_id", name))
            .FirstOrDefaultAsync(ct);
        return documento is null ? null : MongoDocuments.ReadCalendar(documento);
    }

    public async ValueTask<bool> DeleteCalendarAsync(string name, CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var resultado = await collections.Calendars.DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", name), ct);
        return resultado.DeletedCount > 0;
    }

    public async ValueTask<IReadOnlyList<CalendarRecord>> ListCalendarsAsync(CancellationToken ct)
    {
        await collections.EnsureAsync(ct);
        var documentos = await collections.Calendars
            .Find(new BsonDocument())
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync(ct);
        return [.. documentos.Select(MongoDocuments.ReadCalendar)];
    }
}
