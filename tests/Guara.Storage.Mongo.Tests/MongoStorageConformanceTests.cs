using Guara.Storage.Conformance;
using Xunit;

namespace Guara.Storage.Mongo.Tests;

/// <summary>O provider MongoDB passa 100% do kit de conformidade.</summary>
[Collection("mongo")]
public sealed class MongoStorageConformanceTests(MongoContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new MongoStorage(fixture.NewOptions(), timeProvider));
}
