using Guara.Storage.Conformance;
using Xunit;

namespace Guara.Storage.MySql.Tests;

/// <summary>O provider MySQL passa 100% do kit de conformidade.</summary>
[Collection("mysql")]
public sealed class MySqlStorageConformanceTests(MySqlContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new MySqlStorage(fixture.NewOptions(), timeProvider));
}
