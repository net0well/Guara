using Guara.Storage.Conformance;
using Xunit;

namespace Guara.Storage.SqlServer.Tests;

/// <summary>O provider SQL Server passa 100% do kit de conformidade.</summary>
[Collection("sqlserver")]
public sealed class SqlServerStorageConformanceTests(SqlServerContainerFixture fixture) : StorageConformanceTests
{
    protected override ValueTask<IStorage> CreateStorageCoreAsync(TimeProvider timeProvider)
        => ValueTask.FromResult<IStorage>(new SqlServerStorage(fixture.NewOptions(), timeProvider));
}
