using Guara.Core;
using Xunit;

namespace Guara.Core.Tests;

public class InProcessQueueSignalTests
{
    // Teto largo onde o teste espera ser acordado pelo aviso: quem termina o caso é o
    // sinal, não o relógio, então o valor só existe para o teste não travar se quebrar.
    private static readonly TimeSpan TetoLargo = TimeSpan.FromSeconds(30);

    // Teto curto onde o teste espera o tempo esgotar de fato — é ele que define a duração.
    private static readonly TimeSpan TetoCurto = TimeSpan.FromMilliseconds(100);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InProcessQueueSignal NewSignal() => new(TimeProvider.System);

    [Fact]
    public async Task Wait_ReturnsImmediately_WhenSignalArrivedBeforeAnyoneWaited()
    {
        var signal = NewSignal();

        await signal.SignalAsync("default", Ct);

        Assert.True(await signal.WaitAsync(["default"], TetoCurto, Ct));
    }

    [Fact]
    public async Task RetainedSignal_SatisfiesOnlyTheNextWait()
    {
        var signal = NewSignal();
        await signal.SignalAsync("default", Ct);

        Assert.True(await signal.WaitAsync(["default"], TetoCurto, Ct));
        Assert.False(await signal.WaitAsync(["default"], TetoCurto, Ct));
    }

    [Fact]
    public async Task Wait_WakesUp_WhenSignalArrivesDuringTheWait()
    {
        var signal = NewSignal();

        var espera = signal.WaitAsync(["default"], TetoLargo, Ct).AsTask();
        await Task.Delay(20, Ct);
        await signal.SignalAsync("default", Ct);

        Assert.True(await espera);
    }

    [Fact]
    public async Task Wait_IgnoresSignalForAnotherQueue()
    {
        var signal = NewSignal();

        var espera = signal.WaitAsync(["relatorios"], TetoCurto, Ct).AsTask();
        await signal.SignalAsync("emails", Ct);

        Assert.False(await espera);
    }

    [Fact]
    public async Task Wait_WakesUp_OnAnyOfTheQueuesOfInterest()
    {
        var signal = NewSignal();

        var espera = signal.WaitAsync(["relatorios", "emails"], TetoLargo, Ct).AsTask();
        await Task.Delay(20, Ct);
        await signal.SignalAsync("emails", Ct);

        Assert.True(await espera);
    }

    [Fact]
    public async Task Signal_WakesEveryInterestedWaiter()
    {
        var signal = NewSignal();

        var primeira = signal.WaitAsync(["default"], TetoLargo, Ct).AsTask();
        var segunda = signal.WaitAsync(["default"], TetoLargo, Ct).AsTask();
        await Task.Delay(20, Ct);
        await signal.SignalAsync("default", Ct);

        Assert.True(await primeira);
        Assert.True(await segunda);
    }

    [Fact]
    public async Task Wait_ReturnsFalse_WhenTimeoutExpires()
    {
        var signal = NewSignal();

        Assert.False(await signal.WaitAsync(["default"], TetoCurto, Ct));
    }

    [Fact]
    public async Task Wait_Throws_WhenCancelled()
    {
        var signal = NewSignal();
        using var cancelamento = new CancellationTokenSource();

        var espera = signal.WaitAsync(["default"], TetoLargo, cancelamento.Token).AsTask();
        await cancelamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => espera);
    }

    [Fact]
    public async Task Signal_ReachesEveryWaiter_EvenWhenTheyRegisterConcurrently()
    {
        var signal = NewSignal();
        var esperas = Enumerable.Range(0, 16)
            .Select(_ => signal.WaitAsync(["default"], TetoLargo, Ct).AsTask())
            .ToArray();

        var todas = Task.WhenAll(esperas);

        // Avisa em laço porque as esperas sobem em paralelo: quem ainda não estava
        // registrado no primeiro aviso depende do próximo (ou do que ficou retido).
        while (!todas.IsCompleted)
        {
            await signal.SignalAsync("default", Ct);
            await Task.Delay(10, Ct);
        }

        Assert.All(await todas, Assert.True);
    }
}
