using System.Text.Json.Serialization;
using Guara.Abstractions;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Serialização dos payloads persistidos como <c>jsonb</c> (descriptor do job e
/// calendário), com contexto gerado em compilação — zero reflection, AOT-safe.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JobDescriptor))]
[JsonSerializable(typeof(CalendarRecord))]
internal sealed partial class PostgreSqlJsonContext : JsonSerializerContext;
