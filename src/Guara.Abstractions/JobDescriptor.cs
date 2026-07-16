namespace Guara.Abstractions;

/// <summary>
/// Descrição imutável e serializável do que executar: tipo-alvo, método,
/// argumentos já serializados e metadados. Ver <c>Guara.Serialization</c>.
/// </summary>
/// <param name="TypeName">Nome do tipo que contém o método do job.</param>
/// <param name="MethodName">Nome do método a executar.</param>
/// <param name="Arguments">Argumentos já serializados.</param>
/// <param name="Queue">Fila em que o job deve rodar.</param>
public sealed record JobDescriptor(
    string TypeName,
    string MethodName,
    ReadOnlyMemory<byte> Arguments,
    string Queue = "default")
{
    /// <summary>Metadados/headers opcionais (correlação, tenant, etc.).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
