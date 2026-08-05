using Guara.Abstractions;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Scheduler.Tests;

/// <summary>
/// Quem torna um job elegível agora avisa a fila; quem o deixa para depois, não. O aviso
/// existe para o dispatcher acordar na hora, e nunca pode derrubar quem o emite.
/// </summary>
public class QueueSignalTests
{
    private sealed class NullPublisher : IEventPublisher
    {
        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
            where TEvent : IGuaraEvent => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobDescriptor Descriptor(string queue = "default") => new("Tipo", "Metodo", default, queue);

    private static (GuaraClient Client, MemoryStorage Storage, RecordingQueueSignal Signal) NewClient(
        IQueueSignal? signalDoCliente = null)
    {
        var time = new FixedTimeProvider(T0);
        var storage = new MemoryStorage(time);
        var sinal = new RecordingQueueSignal();
        var client = new GuaraClient(
            storage, new NullPublisher(), signalDoCliente ?? sinal,
            new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(storage, sinal, time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);
        return (client, storage, sinal);
    }

    [Fact]
    public async Task Enfileirar_SignalsTheQueueOfTheJob()
    {
        var (client, _, signal) = NewClient();

        await client.EnfileirarAsync(Descriptor("relatorios"), Ct);

        Assert.Equal(["relatorios"], signal.Sinalizadas);
    }

    [Fact]
    public async Task Agendar_WithDelay_DoesNotSignal()
    {
        var (client, _, signal) = NewClient();

        await client.AgendarAsync(Descriptor("relatorios"), TimeSpan.FromMinutes(5), Ct);

        Assert.Empty(signal.Sinalizadas);
    }

    [Fact]
    public async Task Agendar_WithoutDelay_SignalsTheQueue()
    {
        var (client, _, signal) = NewClient();

        await client.AgendarAsync(Descriptor("relatorios"), TimeSpan.Zero, Ct);

        Assert.Equal(["relatorios"], signal.Sinalizadas);
    }

    [Fact]
    public async Task ContinuarCom_DoesNotSignalWhileTheChildAwaitsTheParent()
    {
        var (client, _, signal) = NewClient();
        var paiId = await client.EnfileirarAsync(Descriptor("pais"), Ct);
        signal.Sinalizadas.Clear();

        await client.ContinuarComAsync(paiId, Descriptor("filhos"), ct: Ct);

        Assert.Empty(signal.Sinalizadas);
    }

    [Fact]
    public async Task PromotedContinuation_SignalsTheQueueOfTheChild()
    {
        var (client, storage, signal) = NewClient();
        var paiId = await client.EnfileirarAsync(Descriptor("pais"), Ct);
        await client.ContinuarComAsync(paiId, Descriptor("filhos"), ct: Ct);

        await storage.Jobs.UpdateStateAsync(paiId, JobState.Succeeded, null, Ct);
        signal.Sinalizadas.Clear();

        var promoter = new ContinuationPromoter(
            storage, signal, new FixedTimeProvider(T0), NullLogger<ContinuationPromoter>.Instance);
        await promoter.PromoteAsync(paiId, JobState.Succeeded, Ct);

        Assert.Equal(["filhos"], signal.Sinalizadas);
    }

    [Fact]
    public async Task Enfileirar_SucceedsEvenWhenTheSignalTransportFails()
    {
        var (client, storage, _) = NewClient(new FailingQueueSignal());

        var id = await client.EnfileirarAsync(Descriptor("relatorios"), Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotNull(job);
        Assert.Equal(JobState.Enqueued, job.State);
    }
}
