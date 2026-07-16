using System.Text.Json.Serialization;

namespace Guara.Serialization.Tests;

public sealed record TestPayload(string Name, int Count);

[JsonSerializable(typeof(TestPayload))]
public sealed partial class TestJsonContext : JsonSerializerContext
{
}
