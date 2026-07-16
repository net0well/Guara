using Guara.Storage;
using Guara.Storage.Conformance;
using Guara.Storage.Memory;

namespace Guara.Storage.Memory.Tests;

/// <summary>O provider in-memory passa 100% do conformance kit (spec 011, AC-1).</summary>
public sealed class MemoryStorageConformanceTests : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new MemoryStorage(timeProvider));
}
