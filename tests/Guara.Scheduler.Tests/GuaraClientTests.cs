using Guara.Abstractions;
using Guara.Scheduler;
using Guara.Storage;
using Guara.Storage.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Scheduler.Tests;

public class GuaraClientTests
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

    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (GuaraClient Client, MemoryStorage Storage, RecordingPublisher Events) NewClient()
    {
        var time = new FixedTimeProvider(T0);
        var storage = new MemoryStorage(time);
        var events = new RecordingPublisher();
        var client = new GuaraClient(
            storage, events, new RecordingQueueSignal(), new RecurrenceCalculator(new GuaraCronParser()),
            new ContinuationPromoter(
                storage, new RecordingQueueSignal(), time, NullLogger<ContinuationPromoter>.Instance),
            time, NullLogger<GuaraClient>.Instance);
        return (client, storage, events);
    }

    private static JobDescriptor Descriptor(string queue = "default")
        => new("Meu.Tipo", "MeuMetodo", default, queue);

    [Fact]
    public async Task Enfileirar_PersistsEnqueuedRecord_AndEmitsJobCreated()
    {
        var (client, storage, events) = NewClient();

        var id = await client.EnfileirarAsync(Descriptor("relatorios"), Ct);

        Assert.False(id.IsEmpty);
        var record = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotNull(record);
        Assert.Equal(JobState.Enqueued, record.State);
        Assert.Equal("relatorios", record.Queue);
        Assert.Equal(T0, record.CreatedAt);

        var created = Assert.Single(events.Published.OfType<JobCreated>());
        Assert.Equal(id, created.Id);
    }

    [Fact]
    public async Task Agendar_PersistsScheduledRecord_AndEmitsBothEvents()
    {
        var (client, storage, events) = NewClient();

        var id = await client.AgendarAsync(Descriptor(), TimeSpan.FromHours(24), Ct);

        var record = await storage.Jobs.GetAsync(id, Ct);
        Assert.NotNull(record);
        Assert.Equal(JobState.Scheduled, record.State);
        Assert.Equal(T0 + TimeSpan.FromHours(24), record.ScheduledFor);

        Assert.Single(events.Published.OfType<JobCreated>());
        Assert.Single(events.Published.OfType<JobScheduled>());
    }

    [Fact]
    public async Task Agendar_NegativeDelay_Throws()
    {
        var (client, _, _) = NewClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await client.AgendarAsync(Descriptor(), TimeSpan.FromSeconds(-1), Ct));
    }

    [Fact]
    public async Task Excluir_RemovesPendingJob()
    {
        var (client, storage, _) = NewClient();
        var id = await client.EnfileirarAsync(Descriptor(), Ct);

        Assert.True(await client.ExcluirAsync(id, Ct));
        Assert.Null(await storage.Jobs.GetAsync(id, Ct));
    }

    [Fact]
    public async Task Excluir_UnknownJob_ReturnsFalse()
    {
        var (client, _, _) = NewClient();
        Assert.False(await client.ExcluirAsync(new JobId("nao-existe"), Ct));
    }

    [Fact]
    public async Task Ids_AreUnique()
    {
        var (client, _, _) = NewClient();
        var a = await client.EnfileirarAsync(Descriptor(), Ct);
        var b = await client.EnfileirarAsync(Descriptor(), Ct);
        Assert.NotEqual(a, b);
    }
}
