using Guara.Storage.Conformance;
using Xunit;

namespace Guara.Storage.PostgreSql.Tests;

/// <summary>O provider PostgreSQL passa 100% do kit de conformidade.</summary>
[Collection("postgres")]
public sealed class PostgreSqlStorageConformanceTests(PostgresContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new PostgreSqlStorage(fixture.NewOptions(), timeProvider));
}
