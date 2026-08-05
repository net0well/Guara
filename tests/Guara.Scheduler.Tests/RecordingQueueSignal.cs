using Guara.Abstractions;

namespace Guara.Scheduler.Tests;

/// <summary>
/// Sinal de fila que apenas anota quem foi avisado, na ordem. Quem produz o aviso é o
/// objeto sob teste; a espera nunca é exercida aqui e devolve "tempo esgotado".
/// </summary>
internal sealed class RecordingQueueSignal : IQueueSignal
{
    public List<string> Sinalizadas { get; } = [];

    public ValueTask SignalAsync(string queue, CancellationToken ct)
    {
        Sinalizadas.Add(queue);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> WaitAsync(IReadOnlyList<string> queues, TimeSpan timeout, CancellationToken ct)
        => ValueTask.FromResult(false);
}

/// <summary>Sinal que falha sempre — o aviso é best-effort e não pode derrubar quem o emite.</summary>
internal sealed class FailingQueueSignal : IQueueSignal
{
    public ValueTask SignalAsync(string queue, CancellationToken ct)
        => throw new InvalidOperationException("transporte de sinal indisponível");

    public ValueTask<bool> WaitAsync(IReadOnlyList<string> queues, TimeSpan timeout, CancellationToken ct)
        => ValueTask.FromResult(false);
}
