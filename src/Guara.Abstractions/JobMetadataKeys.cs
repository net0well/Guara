namespace Guara.Abstractions;

/// <summary>
/// Chaves conhecidas de <see cref="JobDescriptor.Metadata"/> usadas pelo próprio
/// framework (prefixo <c>guara-</c>). Chaves do usuário convivem no mesmo dicionário.
/// </summary>
public static class JobMetadataKeys
{
    /// <summary>Id da definição recorrente que originou a ocorrência.</summary>
    public const string RecurringId = "guara-recorrente";

    /// <summary>
    /// Marca emitida pela factory gerada quando o job declara
    /// <c>[GuaraPularSeAnteriorEmExecucao]</c> — o builder de recorrentes a converte
    /// em <c>SkipIfPreviousRunning</c> automaticamente.
    /// </summary>
    public const string SkipIfPreviousRunning = "guara-pular-se-anterior";
}
