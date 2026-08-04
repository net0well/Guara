using System.Text.Json.Serialization;
using Guara.Abstractions;

namespace Guara.Serialization;

/// <summary>
/// Contexto de serialização gerado em compilação (<see cref="JsonSerializerContext"/>)
/// para os tipos do framework e primitivos comuns — zero reflection em runtime.
/// Tipos do usuário entram via contextos adicionais encadeados
/// no <c>TypeInfoResolverChain</c> (futuramente gerados pelo <c>Guara.SourceGenerators</c>).
/// </summary>
[JsonSerializable(typeof(JobDescriptor))]
[JsonSerializable(typeof(JobId))]
[JsonSerializable(typeof(JobState))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class GuaraJsonContext : JsonSerializerContext
{
}
