namespace Guara.Abstractions;

/// <summary>
/// Identificador opaco de um job. <see cref="IsEmpty"/> indica a ausência de job
/// (equivalente a <c>default</c>). A representação é textual e agnóstica de provider.
/// </summary>
/// <param name="Value">Representação textual do identificador.</param>
public readonly record struct JobId(string Value)
{
    /// <summary>Indica se o identificador está vazio ("nenhum job").</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Um <see cref="JobId"/> vazio.</summary>
    public static JobId None => default;

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
