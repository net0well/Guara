using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Um vínculo de continuação pendente cujo pai já tem desfecho — o que a varredura de
/// recuperação precisa para decidir entre enfileirar o filho e descartar a cadeia.
/// </summary>
public sealed record ResolvableContinuation
{
    /// <summary>O vínculo pendente.</summary>
    public required ContinuationRecord Continuation { get; init; }

    /// <summary>
    /// Estado final do pai, ou <c>null</c> quando ele não existe mais — caso em que a
    /// cadeia é descartada, porque o gatilho nunca vai poder ser avaliado.
    /// </summary>
    public JobState? ParentState { get; init; }
}
