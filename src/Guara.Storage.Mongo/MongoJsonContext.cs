using System.Text.Json.Serialization;
using Guara.Storage;

namespace Guara.Storage.Mongo;

/// <summary>
/// Serialização do calendário, único payload que vai para o banco como texto em vez de
/// documento: ele é lido inteiro pelo nome e nunca consultado por campo, então mapear
/// cada regra para BSON não pagaria. Contexto gerado em compilação — zero reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CalendarRecord))]
internal sealed partial class MongoJsonContext : JsonSerializerContext;
