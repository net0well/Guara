using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Cluster.Tests;

/// <summary>
/// A eleição existe para que o trabalho que não se divide entre nós rode em um só — e
/// para que quem deixa de ser líder pare de agir como tal.
/// </summary>
public class LeaderElectionTests
{
    private const string Papel = "guara:recurring";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ClusterOptions Rapido = new()
    {
        LeadershipTtl = TimeSpan.FromMilliseconds(400),
        RenewInterval = TimeSpan.FromMilliseconds(50),
    };

    private static LockLeaderElection NewNode(IStorage storage, ClusterOptions? options = null)
        => new(storage, options ?? Rapido, TimeProvider.System, NullLogger<LockLeaderElection>.Instance);

    [Fact]
    public async Task OnlyOneNodeLeadsARole()
    {
        var storage = new MemoryStorage();
        var primeiro = NewNode(storage);
        var segundo = NewNode(storage);

        await using var lideranca = await primeiro.TryAcquireAsync(Papel, Ct);

        Assert.NotNull(lideranca);
        Assert.Equal(Papel, lideranca.Role);
        Assert.Null(await segundo.TryAcquireAsync(Papel, Ct));
    }

    [Fact]
    public async Task DifferentRolesDoNotCompete()
    {
        var storage = new MemoryStorage();
        var eleicao = NewNode(storage);

        await using var recorrentes = await eleicao.TryAcquireAsync("guara:recurring", Ct);
        await using var manutencao = await eleicao.TryAcquireAsync("guara:maintenance", Ct);

        Assert.NotNull(recorrentes);
        Assert.NotNull(manutencao);
    }

    /// <summary>
    /// O motivo de o componente existir: sem renovação, uma liderança que dura mais que a
    /// validade seria roubada no meio do trabalho.
    /// </summary>
    [Fact]
    public async Task RenewalKeepsLeadershipPastTheTtl()
    {
        var storage = new MemoryStorage();
        var lider = NewNode(storage);
        var rival = NewNode(storage);

        await using var lideranca = await lider.TryAcquireAsync(Papel, Ct);
        Assert.NotNull(lideranca);

        // Bem além da validade: só a renovação em segundo plano segura o papel.
        await Task.Delay(Rapido.LeadershipTtl * 3, Ct);

        Assert.Null(await rival.TryAcquireAsync(Papel, Ct));
        Assert.False(lideranca.Lost.IsCancellationRequested);
    }

    [Fact]
    public async Task ReleasingHandsTheRoleOverImmediately()
    {
        var storage = new MemoryStorage();
        var primeiro = NewNode(storage);
        var segundo = NewNode(storage);

        var lideranca = await primeiro.TryAcquireAsync(Papel, Ct);
        Assert.NotNull(lideranca);

        // Devolver não espera a validade vencer: o papel volta para disputa na hora.
        await lideranca.DisposeAsync();

        await using var assumida = await segundo.TryAcquireAsync(Papel, Ct);
        Assert.NotNull(assumida);
    }

    /// <summary>
    /// Posse perdida precisa chegar a quem lidera. O duplo recusa renovar porque roubar o
    /// lock por fora não funcionaria: a renovação em segundo plano o mantém vivo, que é
    /// exatamente o comportamento que o caso anterior verifica.
    /// </summary>
    [Fact]
    public async Task LostIsCancelledWhenRenewalFails()
    {
        var eleicao = NewNode(new StorageQueRecusaRenovar());

        await using var lideranca = await eleicao.TryAcquireAsync(Papel, Ct);
        Assert.NotNull(lideranca);

        await lideranca.Lost.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(lideranca.Lost.IsCancellationRequested);
    }

    [Fact]
    public async Task LostIsCancelledWhenTheStorageFails()
    {
        var eleicao = NewNode(new StorageQueRecusaRenovar(lancar: true));

        await using var lideranca = await eleicao.TryAcquireAsync(Papel, Ct);
        Assert.NotNull(lideranca);

        // Falha ao falar com o storage é indistinguível de posse perdida para quem
        // depende dela: ceder o papel é a escolha segura.
        await lideranca.Lost.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(lideranca.Lost.IsCancellationRequested);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(10, 10)]
    [InlineData(5, 10)]
    public void OptionsRejectRenewIntervalWithoutSlack(int ttlSegundos, int renovacaoSegundos)
    {
        var options = new ClusterOptions
        {
            LeadershipTtl = TimeSpan.FromSeconds(ttlSegundos),
            RenewInterval = TimeSpan.FromSeconds(renovacaoSegundos),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    /// <summary>
    /// Concede a posse e depois a nega na renovação — só os locks são exercitados, então
    /// o resto do storage não precisa existir.
    /// </summary>
    private sealed class StorageQueRecusaRenovar(bool lancar = false) : IStorage
    {
        public StorageCapabilities Capabilities => throw new NotSupportedException();

        public IJobStorage Jobs => throw new NotSupportedException();

        public IQueueStorage Queues => throw new NotSupportedException();

        public ILockProvider Locks { get; } = new Provider(lancar);

        public IServerRegistry Servers => throw new NotSupportedException();

        public IRecurringStorage Recurring => throw new NotSupportedException();

        public IContinuationStorage Continuations => throw new NotSupportedException();

        private sealed class Provider(bool lancar) : ILockProvider
        {
            public ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
                => ValueTask.FromResult<ILockHandle?>(new Handle(key, lancar));
        }

        private sealed class Handle(string key, bool lancar) : ILockHandle
        {
            public string Key => key;

            public ValueTask<bool> RenewAsync(TimeSpan ttl, CancellationToken ct)
                => lancar
                    ? throw new InvalidOperationException("storage indisponível")
                    : ValueTask.FromResult(false);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

/// <summary>Espera um token ser cancelado, sem depender de polling no teste.</summary>
internal static class CancellationTokenWaitExtensions
{
    public static async Task WaitAsync(this CancellationToken token, TimeSpan timeout)
    {
        var aguardo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registro = token.Register(() => aguardo.TrySetResult());
        await aguardo.Task.WaitAsync(timeout);
    }
}
