using Guara.Storage;
using Guara.Storage.Memory;
using Xunit;

namespace Guara.Storage.Memory.Tests;

/// <summary>Comportamentos específicos do provider in-memory (spec 011).</summary>
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
    public async Task BeginTransaction_Throws_NotSupported()
    {
        var storage = new MemoryStorage();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await storage.BeginTransactionAsync(CancellationToken.None));
    }
}
