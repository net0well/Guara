namespace Guara.Abstractions;

/// <summary>Estado final do pai que dispara uma continuação.</summary>
public enum ContinuationTrigger
{
    /// <summary>Dispara apenas quando o pai conclui com sucesso (default).</summary>
    OnSucceeded,

    /// <summary>Dispara quando o pai atinge qualquer estado final (sucesso ou falha).</summary>
    OnAnyFinishedState,
}

/// <summary>Opções de uma continuação.</summary>
/// <param name="Trigger">Estado final do pai que dispara o filho.</param>
public sealed record ContinuationOptions(ContinuationTrigger Trigger = ContinuationTrigger.OnSucceeded);
