using Guara.Abstractions;
using Xunit;

namespace Guara.Core.Tests;

public class JobIdTests
{
    [Fact]
    public void Default_IsEmpty()
    {
        JobId id = default;
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void WithValue_IsNotEmpty()
    {
        var id = new JobId("abc");
        Assert.False(id.IsEmpty);
        Assert.Equal("abc", id.Value);
    }
}
