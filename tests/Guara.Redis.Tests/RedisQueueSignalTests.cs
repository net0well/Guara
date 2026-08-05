using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Redis.Tests;

/// <summary>
/// O que só o Redis entrega: o aviso emitido num nó acorda o dispatcher de outro. Cada
/// instância de <see cref="RedisQueueSignal"/> representa um nó distinto do cluster.
/// </summary>
[Collection("redis")]
public class RedisQueueSignalTests(RedisContainerFixture fixture)
{
    // Teto largo onde quem termina o caso é o aviso, não o relógio.
    private static readonly TimeSpan TetoLargo = TimeSpan.FromSeconds(30);

    // Teto curto onde o teste espera o tempo esgotar de fato.
    private static readonly TimeSpan TetoCurto = TimeSpan.FromSeconds(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RedisQueueSignal NewNode(RedisOptions options) => new(
        options, conexaoDoContainer: null, TimeProvider.System, NullLogger<RedisQueueSignal>.Instance);

    [Fact]
    public async Task Signal_FromAnotherNode_WakesTheWaiter()
    {
        var options = fixture.NewOptions();
        await using var quemEnfileira = NewNode(options);
        await using var quemDespacha = NewNode(options);

        var espera = quemDespacha.WaitAsync(["relatorios"], TetoLargo, Ct).AsTask();
        await Task.Delay(300, Ct); // dá tempo de a assinatura subir antes da publicação

        await quemEnfileira.SignalAsync("relatorios", Ct);

        Assert.True(await espera);
    }

    [Fact]
    public async Task Signal_ForAnotherQueue_DoesNotWakeTheWaiter()
    {
        var options = fixture.NewOptions();
        await using var quemEnfileira = NewNode(options);
        await using var quemDespacha = NewNode(options);

        var espera = quemDespacha.WaitAsync(["relatorios"], TetoCurto, Ct).AsTask();
        await Task.Delay(300, Ct);

        await quemEnfileira.SignalAsync("emails", Ct);

        Assert.False(await espera);
    }

    [Fact]
    public async Task Signal_WakesTheWaiterOnItsOwnNode()
    {
        await using var no = NewNode(fixture.NewOptions());

        var espera = no.WaitAsync(["relatorios"], TetoLargo, Ct).AsTask();
        await Task.Delay(300, Ct);

        await no.SignalAsync("relatorios", Ct);

        Assert.True(await espera);
    }

    [Fact]
    public async Task DifferentChannelPrefixes_DoNotWakeEachOther()
    {
        // Duas instalações dividindo o mesmo Redis: o prefixo é o que as mantém surdas
        // uma para a outra.
        await using var instalacaoA = NewNode(fixture.NewOptions());
        await using var instalacaoB = NewNode(fixture.NewOptions());

        var espera = instalacaoB.WaitAsync(["relatorios"], TetoCurto, Ct).AsTask();
        await Task.Delay(300, Ct);

        await instalacaoA.SignalAsync("relatorios", Ct);

        Assert.False(await espera);
    }

    [Fact]
    public async Task Wait_ReturnsFalse_WhenNobodySignals()
    {
        await using var no = NewNode(fixture.NewOptions());

        Assert.False(await no.WaitAsync(["relatorios"], TetoCurto, Ct));
    }
}

/// <summary>
/// O aviso é acessório: com o Redis fora, o nó continua acordando localmente e nada
/// estoura. Não usa container justamente porque o cenário é o Redis inalcançável.
/// </summary>
public class RedisQueueSignalOfflineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RedisQueueSignal Offline() => new(
        new RedisOptions
        {
            // Porta sem ninguém escutando; o tempo curto de conexão mantém o teste rápido.
            ConnectionString = "127.0.0.1:6399,connectTimeout=200,connectRetry=1",
            ChannelPrefix = "offline",
        },
        conexaoDoContainer: null,
        TimeProvider.System,
        NullLogger<RedisQueueSignal>.Instance);

    [Fact]
    public async Task Signal_DoesNotThrow_AndStillWakesTheLocalWaiter()
    {
        await using var no = Offline();

        var espera = no.WaitAsync(["relatorios"], TimeSpan.FromSeconds(30), Ct).AsTask();
        await Task.Delay(300, Ct);

        await no.SignalAsync("relatorios", Ct);

        Assert.True(await espera);
    }

    [Fact]
    public async Task Wait_DoesNotThrow_WhenTheSubscriptionCannotBeMade()
    {
        await using var no = Offline();

        Assert.False(await no.WaitAsync(["relatorios"], TimeSpan.FromMilliseconds(500), Ct));
    }
}
