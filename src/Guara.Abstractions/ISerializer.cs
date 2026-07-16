namespace Guara.Abstractions;

/// <summary>
/// Contrato de (de)serialização do Guará, agnóstico de formato.
/// A implementação default (<c>Guara.Serialization</c>) usa System.Text.Json com
/// source generators — zero reflection, AOT-safe — e uma allowlist de tipos:
/// a desserialização nunca instancia tipos não registrados.
/// </summary>
public interface ISerializer
{
    /// <summary>Serializa um valor de tipo estático conhecido.</summary>
    /// <typeparam name="T">Tipo do valor.</typeparam>
    /// <param name="value">Valor a serializar.</param>
    /// <returns>Payload serializado.</returns>
    ReadOnlyMemory<byte> Serialize<T>(T value);

    /// <summary>Desserializa um payload para um tipo estático conhecido.</summary>
    /// <typeparam name="T">Tipo esperado.</typeparam>
    /// <param name="data">Payload serializado.</param>
    /// <returns>O valor desserializado, ou <c>null</c> quando o payload representa nulo.</returns>
    T? Deserialize<T>(ReadOnlySpan<byte> data);

    /// <summary>
    /// Serializa os argumentos de um job num envelope com discriminador de tipo por
    /// argumento (formato "aberto": os tipos estáticos não são conhecidos no call site).
    /// </summary>
    /// <param name="args">Argumentos do job (podem conter <c>null</c>).</param>
    /// <returns>Envelope serializado.</returns>
    ReadOnlyMemory<byte> SerializeArgs(ReadOnlySpan<object?> args);

    /// <summary>
    /// Desserializa um envelope de argumentos, resolvendo cada tipo pela allowlist.
    /// Falha explicitamente para tipos não registrados — nunca os instancia.
    /// </summary>
    /// <param name="data">Envelope serializado.</param>
    /// <returns>Os argumentos materializados.</returns>
    object?[] DeserializeArgs(ReadOnlySpan<byte> data);
}
