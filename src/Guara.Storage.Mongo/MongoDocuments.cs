using System.Text.Json;
using Guara.Abstractions;
using MongoDB.Bson;

namespace Guara.Storage.Mongo;

/// <summary>
/// Conversão entre os registros do Guará e documentos BSON, escrita à mão campo a campo.
/// O mapeamento automático do driver descobre membros por reflection em runtime, o que o
/// Guará não admite: aqui não há class map, e o que vai para o banco é sempre explícito.
/// <para>
/// Instantes viajam como ticks UTC (<c>int64</c>) e não como data BSON, que tem precisão
/// de milissegundo e truncaria o valor original — ticks fazem round-trip exato e ordenam
/// igual.
/// </para>
/// </summary>
internal static class MongoDocuments
{
    public static BsonValue Instant(DateTimeOffset value) => value.UtcTicks;

    public static BsonValue Instant(DateTimeOffset? value)
        => value is { } instante ? instante.UtcTicks : BsonNull.Value;

    public static DateTimeOffset ReadInstant(BsonValue value)
        => new(value.AsInt64, TimeSpan.Zero);

    public static DateTimeOffset? ReadInstantOrNull(BsonValue value)
        => value.IsBsonNull ? null : ReadInstant(value);

    public static BsonValue Text(string? value) => value is null ? BsonNull.Value : value;

    public static string? ReadTextOrNull(BsonValue value) => value.IsBsonNull ? null : value.AsString;

    public static BsonDocument FromJob(JobRecord record) => new()
    {
        ["_id"] = record.Id.Value,
        ["descriptor"] = FromDescriptor(record.Descriptor),
        ["state"] = (int)record.State,
        ["attempt"] = record.Attempt,
        ["queue"] = record.Queue,
        ["createdAt"] = Instant(record.CreatedAt),
        ["scheduledFor"] = Instant(record.ScheduledFor),
        ["leaseUntil"] = Instant(record.LeaseUntil),
        ["finishedAt"] = Instant(record.FinishedAt),
        ["result"] = Text(record.Result),
        ["error"] = Text(record.Error),
        ["eligibleAt"] = Instant(JobEligibility.For(record)),
    };

    public static JobRecord ReadJob(BsonDocument document) => new()
    {
        Id = new JobId(document["_id"].AsString),
        Descriptor = ReadDescriptor(document["descriptor"].AsBsonDocument),
        State = (JobState)document["state"].AsInt32,
        Attempt = document["attempt"].AsInt32,
        Queue = document["queue"].AsString,
        CreatedAt = ReadInstant(document["createdAt"]),
        ScheduledFor = ReadInstantOrNull(document["scheduledFor"]),
        LeaseUntil = ReadInstantOrNull(document["leaseUntil"]),
        FinishedAt = ReadInstantOrNull(document["finishedAt"]),
        Result = ReadTextOrNull(document["result"]),
        Error = ReadTextOrNull(document["error"]),
    };

    public static BsonDocument FromDescriptor(JobDescriptor descriptor) => new()
    {
        ["typeName"] = descriptor.TypeName,
        ["methodName"] = descriptor.MethodName,
        ["arguments"] = new BsonBinaryData(descriptor.Arguments.ToArray()),
        ["queue"] = descriptor.Queue,
        // Chave de metadado é texto livre do usuário e o MongoDB restringe nomes de campo:
        // guardar como lista de pares aceita qualquer chave sem sanitizar nada.
        ["metadata"] = descriptor.Metadata is { } metadata
            ? new BsonArray(metadata.Select(par => new BsonDocument { ["k"] = par.Key, ["v"] = par.Value }))
            : BsonNull.Value,
    };

    public static JobDescriptor ReadDescriptor(BsonDocument document) => new(
        document["typeName"].AsString,
        document["methodName"].AsString,
        document["arguments"].AsByteArray,
        document["queue"].AsString)
    {
        Metadata = document["metadata"].IsBsonNull
            ? null
            : document["metadata"].AsBsonArray.ToDictionary(
                par => par["k"].AsString, par => par["v"].AsString, StringComparer.Ordinal),
    };

    public static BsonDocument FromRecurring(RecurringJobRecord record) => new()
    {
        ["_id"] = record.Id,
        ["descriptor"] = FromDescriptor(record.Descriptor),
        ["cron"] = Text(record.CronExpression),
        // Intervalo e janela diária viajam em ticks: round-trip exato de TimeSpan/TimeOnly.
        ["intervalTicks"] = record.Interval is { } intervalo ? intervalo.Ticks : BsonNull.Value,
        ["windowStartTicks"] = record.WindowStart is { } inicio ? inicio.Ticks : BsonNull.Value,
        ["windowEndTicks"] = record.WindowEnd is { } fim ? fim.Ticks : BsonNull.Value,
        ["timeZone"] = Text(record.TimeZoneId),
        ["notBefore"] = Instant(record.NotBefore),
        ["notAfter"] = Instant(record.NotAfter),
        ["description"] = Text(record.Description),
        ["queue"] = record.Queue,
        ["calendarName"] = Text(record.CalendarName),
        ["skipIfPreviousRunning"] = record.SkipIfPreviousRunning,
        ["paused"] = record.Paused,
        ["createdAt"] = Instant(record.CreatedAt),
        ["lastRunAt"] = Instant(record.LastRunAt),
        ["lastRunJobId"] = Text(record.LastRunJobId?.Value),
        ["nextRunAt"] = Instant(record.NextRunAt),
        ["lastSkippedAt"] = Instant(record.LastSkippedAt),
    };

    public static RecurringJobRecord ReadRecurring(BsonDocument document) => new()
    {
        Id = document["_id"].AsString,
        Descriptor = ReadDescriptor(document["descriptor"].AsBsonDocument),
        CronExpression = ReadTextOrNull(document["cron"]),
        Interval = document["intervalTicks"].IsBsonNull
            ? null
            : TimeSpan.FromTicks(document["intervalTicks"].AsInt64),
        WindowStart = document["windowStartTicks"].IsBsonNull
            ? null
            : new TimeOnly(document["windowStartTicks"].AsInt64),
        WindowEnd = document["windowEndTicks"].IsBsonNull
            ? null
            : new TimeOnly(document["windowEndTicks"].AsInt64),
        TimeZoneId = ReadTextOrNull(document["timeZone"]),
        NotBefore = ReadInstantOrNull(document["notBefore"]),
        NotAfter = ReadInstantOrNull(document["notAfter"]),
        Description = ReadTextOrNull(document["description"]),
        Queue = document["queue"].AsString,
        CalendarName = ReadTextOrNull(document["calendarName"]),
        SkipIfPreviousRunning = document["skipIfPreviousRunning"].AsBoolean,
        Paused = document["paused"].AsBoolean,
        CreatedAt = ReadInstant(document["createdAt"]),
        LastRunAt = ReadInstantOrNull(document["lastRunAt"]),
        LastRunJobId = document["lastRunJobId"].IsBsonNull
            ? null
            : new JobId(document["lastRunJobId"].AsString),
        NextRunAt = ReadInstantOrNull(document["nextRunAt"]),
        LastSkippedAt = ReadInstantOrNull(document["lastSkippedAt"]),
    };

    public static BsonDocument FromContinuation(ContinuationRecord record) => new()
    {
        ["_id"] = record.ChildId.Value,
        ["parentId"] = record.ParentId.Value,
        ["firesOn"] = (int)record.Trigger,
        ["status"] = (int)record.Status,
        ["reason"] = Text(record.Reason),
        ["depth"] = record.Depth,
        ["createdAt"] = Instant(record.CreatedAt),
        ["resolvedAt"] = Instant(record.ResolvedAt),
    };

    public static ContinuationRecord ReadContinuation(BsonDocument document) => new()
    {
        ChildId = new JobId(document["_id"].AsString),
        ParentId = new JobId(document["parentId"].AsString),
        Trigger = (ContinuationTrigger)document["firesOn"].AsInt32,
        Status = (ContinuationStatus)document["status"].AsInt32,
        Reason = ReadTextOrNull(document["reason"]),
        Depth = document["depth"].AsInt32,
        CreatedAt = ReadInstant(document["createdAt"]),
        ResolvedAt = ReadInstantOrNull(document["resolvedAt"]),
    };

    public static BsonDocument FromServer(ServerNode node) => new()
    {
        ["_id"] = node.Id,
        ["machineName"] = node.MachineName,
        ["startedAt"] = Instant(node.StartedAt),
        ["lastHeartbeat"] = Instant(node.LastHeartbeat),
        ["queues"] = new BsonArray(node.Queues),
        ["maxConcurrency"] = node.MaxConcurrency,
        ["roles"] = new BsonArray(node.Roles),
    };

    public static ServerNode ReadServer(BsonDocument document) => new()
    {
        Id = document["_id"].AsString,
        MachineName = document["machineName"].AsString,
        StartedAt = ReadInstant(document["startedAt"]),
        LastHeartbeat = ReadInstant(document["lastHeartbeat"]),
        Queues = [.. document["queues"].AsBsonArray.Select(fila => fila.AsString)],
        MaxConcurrency = document["maxConcurrency"].AsInt32,

        // Documento gravado antes da coluna existir não tem o campo: nó sem papel até
        // reanunciar, em vez de erro de leitura.
        Roles = document.TryGetValue("roles", out var papeis)
            ? [.. papeis.AsBsonArray.Select(papel => papel.AsString)]
            : [],
    };

    public static BsonDocument FromCalendar(CalendarRecord calendar) => new()
    {
        ["_id"] = calendar.Name,
        ["payload"] = JsonSerializer.Serialize(calendar, MongoJsonContext.Default.CalendarRecord),
    };

    public static CalendarRecord ReadCalendar(BsonDocument document)
        => JsonSerializer.Deserialize(document["payload"].AsString, MongoJsonContext.Default.CalendarRecord)!;
}
