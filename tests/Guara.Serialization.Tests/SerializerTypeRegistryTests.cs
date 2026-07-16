using Xunit;

namespace Guara.Serialization.Tests;

public class SerializerTypeRegistryTests
{
    [Fact]
    public void Register_ResolvesBothDirections()
    {
        var registry = new SerializerTypeRegistry().Register<TestPayload>("testPayload");

        Assert.True(registry.TryGetType("testPayload", out var type));
        Assert.Equal(typeof(TestPayload), type);

        Assert.True(registry.TryGetDiscriminator(typeof(TestPayload), out var discriminator));
        Assert.Equal("testPayload", discriminator);
    }

    [Fact]
    public void Register_DuplicateDiscriminatorForOtherType_Throws()
    {
        var registry = new SerializerTypeRegistry().Register<TestPayload>("x");

        Assert.Throws<ArgumentException>(() => registry.Register<string>("x"));
    }

    [Fact]
    public void Register_SameTypeSameDiscriminator_IsIdempotent()
    {
        var registry = new SerializerTypeRegistry()
            .Register<TestPayload>("x")
            .Register<TestPayload>("x");

        Assert.True(registry.TryGetType("x", out _));
    }

    [Fact]
    public void TryGetType_Unknown_ReturnsFalse()
    {
        var registry = SerializerTypeRegistry.CreateDefault();

        Assert.False(registry.TryGetType("naoExiste", out _));
        Assert.False(registry.TryGetDiscriminator(typeof(SerializerTypeRegistryTests), out _));
    }

    [Fact]
    public void CreateDefault_IncludesCommonPrimitives()
    {
        var registry = SerializerTypeRegistry.CreateDefault();

        Assert.True(registry.TryGetDiscriminator(typeof(string), out _));
        Assert.True(registry.TryGetDiscriminator(typeof(int), out _));
        Assert.True(registry.TryGetDiscriminator(typeof(DateTimeOffset), out _));
        Assert.True(registry.TryGetDiscriminator(typeof(Guid), out _));
    }
}
