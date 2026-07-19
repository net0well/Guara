using Guara.Storage;
using Guara.Storage.Conformance;
using Guara.Storage.Memory;

namespace Guara.Storage.Memory.Tests;

/// <summary>O provider in-memory passa 100% do kit de conformidade.</summary>
public sealed class MemoryStorageConformanceTests : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new MemoryStorage(timeProvider));
}
