using System.Text.Json.Serialization;
using Guara.Abstractions;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Serialização dos payloads persistidos como texto (descritor do job, calendário e a
/// lista de filas do nó), com contexto gerado em compilação — zero reflection, AOT-safe.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JobDescriptor))]
[JsonSerializable(typeof(CalendarRecord))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class SqlServerJsonContext : JsonSerializerContext;
