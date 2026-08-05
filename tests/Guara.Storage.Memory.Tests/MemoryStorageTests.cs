using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.Memory;
using Xunit;

namespace Guara.Storage.Memory.Tests;

/// <summary>Comportamentos específicos do provider in-memory.</summary>
public sealed class MemoryStorageTests
{
    [Fact]
    public void Capabilities_AreHonest()
    {
        var storage = new MemoryStorage();

        Assert.False(storage.Capabilities.SupportsTransactions);
        Assert.False(storage.Capabilities.SupportsDistributedLock); // lock é process-local
        Assert.False(storage.Capabilities.SupportsServerSideTimers);
        Assert.True(storage.Capabilities.SupportsServerSideFilter);
    }

    [Fact]
    public async Task CreateInsideCallerTransaction_Throws_NotSupported()
    {
        var storage = new MemoryStorage();
        var job = new JobRecord
        {
            Id = new JobId("j1"),
            Descriptor = new JobDescriptor("Tipo", "Metodo", default),
            State = JobState.Enqueued,
            Queue = "default",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var erro = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await storage.Jobs.CreateAsync(job, new FakeTransaction(), CancellationToken.None));
        Assert.Contains("SupportsTransactions", erro.Message, StringComparison.Ordinal);
    }

    /// <summary>Handle qualquer: a recusa acontece antes de o provider olhar o conteúdo.</summary>
    private sealed class FakeTransaction : IGuaraTransaction;
}
