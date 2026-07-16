namespace Guara.Serialization;

/// <summary>
/// Erro de (de)serialização do Guará: payload corrompido, tipo não registrado na
/// allowlist ou envelope em versão não suportada. Um job com payload inválido vira
/// <c>Failed</c> com motivo — nunca derruba o worker (spec 003).
/// </summary>
public sealed class GuaraSerializationException : Exception
{
    /// <summary>Cria a exceção com uma mensagem descritiva.</summary>
    /// <param name="message">Mensagem do erro.</param>
    public GuaraSerializationException(string message) : base(message)
    {
    }

    /// <summary>Cria a exceção com mensagem e causa original.</summary>
    /// <param name="message">Mensagem do erro.</param>
    /// <param name="innerException">Exceção original.</param>
    public GuaraSerializationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
