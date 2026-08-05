using Guara.Abstractions;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Scheduler.Tests;

/// <summary>
/// O caminho transacional do cliente: repassa o handle ao storage e não deixa nada
/// observável escapar antes de o chamador confirmar.
/// </summary>
public class TransactionalEnqueueTests
{
    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<IGuaraEvent> Published { get; } = [];

        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
            where TEvent : IGuaraEvent
        {
            Published.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobDescriptor Descriptor(string queue = "default") => new("Tipo", "Metodo", default, queue);

    private static (GuaraClient Client, RecordingTransactionalStorage Storage,
        RecordingQueueSignal Signal, RecordingPublisher Events) NewClient()
    {
        var time = new FixedTimeProvider(T0);
        var storage = new RecordingTransactionalStorage(time);
        var signal = new RecordingQueueSignal();
        var events = new RecordingPublisher();
        var client = new GuaraClient(
            storage, events, signal, new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(storage, signal, time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);
        return (client, storage, signal, events);
    }

    [Fact]
    public async Task Enfileirar_PassesTheCallerTransactionToTheStorage()
    {
        var (client, storage, _, _) = NewClient();
        var transacao = new FakeCallerTransaction();

        await client.EnfileirarAsync(Descriptor("relatorios"), transacao, Ct);

        Assert.Same(transacao, Assert.Single(storage.RecordingJobs.TransacoesRecebidas));
    }

    [Fact]
    public async Task Enfileirar_InsideTransaction_PersistsAsEnqueued()
    {
        var (client, storage, _, _) = NewClient();

        var id = await client.EnfileirarAsync(Descriptor("relatorios"), new FakeCallerTransaction(), Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotNull(job);
        Assert.Equal(JobState.Enqueued, job.State);
        Assert.Equal("relatorios", job.Queue);
        Assert.Null(job.ScheduledFor);
    }

    [Fact]
    public async Task Agendar_InsideTransaction_PersistsScheduledForTheDelay()
    {
        var (client, storage, _, _) = NewClient();

        var id = await client.AgendarAsync(
            Descriptor(), TimeSpan.FromMinutes(30), new FakeCallerTransaction(), Ct);

        var job = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotNull(job);
        Assert.Equal(JobState.Scheduled, job.State);
        Assert.Equal(T0.AddMinutes(30), job.ScheduledFor);
    }

    /// <summary>
    /// O aviso acordaria o dispatcher para buscar um job que ainda não é visível — ou que
    /// vai sumir no rollback. O caminho transacional troca latência por atomicidade.
    /// </summary>
    [Fact]
    public async Task Enfileirar_InsideTransaction_DoesNotSignalTheQueue()
    {
        var (client, _, signal, _) = NewClient();

        await client.EnfileirarAsync(Descriptor("relatorios"), new FakeCallerTransaction(), Ct);

        Assert.Empty(signal.Sinalizadas);
    }

    /// <summary>Pelo mesmo motivo do aviso: nada observável escapa antes da confirmação.</summary>
    [Fact]
    public async Task Enfileirar_InsideTransaction_DoesNotPublishEvents()
    {
        var (client, _, _, events) = NewClient();

        await client.EnfileirarAsync(Descriptor(), new FakeCallerTransaction(), Ct);

        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task Enfileirar_WithoutTransaction_StillSignalsAndPublishes()
    {
        var (client, _, signal, events) = NewClient();

        await client.EnfileirarAsync(Descriptor("relatorios"), Ct);

        Assert.Equal(["relatorios"], signal.Sinalizadas);
        Assert.NotEmpty(events.Published);
    }

    [Fact]
    public async Task Agendar_InsideTransaction_RejectsNegativeDelay()
    {
        var (client, _, _, _) = NewClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await client.AgendarAsync(Descriptor(), TimeSpan.FromSeconds(-1), new FakeCallerTransaction(), Ct));
    }

    [Fact]
    public async Task Enfileirar_InsideTransaction_SurfacesTheRefusalOfAProviderWithoutSupport()
    {
        var time = new FixedTimeProvider(T0);
        var storage = new MemoryStorage(time);
        var signal = new RecordingQueueSignal();
        var client = new GuaraClient(
            storage, new RecordingPublisher(), signal, new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(storage, signal, time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);

        var erro = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await client.EnfileirarAsync(Descriptor(), new FakeCallerTransaction(), Ct));
        Assert.Contains("SupportsTransactions", erro.Message, StringComparison.Ordinal);
    }
}
