using System.Globalization;
using System.Text;
using System.Text.Json;
using Guara.Abstractions;
using Xunit;

namespace Guara.Serialization.Tests;

public class SystemTextJsonSerializerTests
{
    private static SystemTextJsonSerializer NewSerializer(SerializerTypeRegistry? registry = null)
    {
        var options = new JsonSerializerOptions();
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(GuaraJsonContext.Default);

        return new SystemTextJsonSerializer(
            registry ?? SerializerTypeRegistry.CreateDefault().Register<TestPayload>("testPayload"),
            options);
    }

    // AC-1 — round-trip de JobDescriptor
    [Fact]
    public void Serialize_JobDescriptor_RoundTrips()
    {
        var serializer = NewSerializer();
        var descriptor = new JobDescriptor("Meu.Tipo", "MeuMetodo", new byte[] { 1, 2, 3 }, "alta")
        {
            Metadata = new Dictionary<string, string> { ["correlationId"] = "abc" },
        };

        var bytes = serializer.Serialize(descriptor);
        var restored = serializer.Deserialize<JobDescriptor>(bytes.Span);

        Assert.NotNull(restored);
        Assert.Equal(descriptor.TypeName, restored.TypeName);
        Assert.Equal(descriptor.MethodName, restored.MethodName);
        Assert.Equal(descriptor.Queue, restored.Queue);
        Assert.Equal(descriptor.Arguments.ToArray(), restored.Arguments.ToArray());
        Assert.Equal("abc", restored.Metadata?["correlationId"]);
    }

    // AC-1 — round-trip de argumentos (primitivos + tipo custom + null)
    [Fact]
    public void SerializeArgs_MixedArgs_RoundTrip()
    {
        var serializer = NewSerializer();
        object?[] args = ["texto", 42, null, new TestPayload("relatorio", 7), 3.14];

        var envelope = serializer.SerializeArgs(args);
        var restored = serializer.DeserializeArgs(envelope.Span);

        Assert.Equal(5, restored.Length);
        Assert.Equal("texto", restored[0]);
        Assert.Equal(42, restored[1]);
        Assert.Null(restored[2]);
        Assert.Equal(new TestPayload("relatorio", 7), restored[3]);
        Assert.Equal(3.14, restored[4]);
    }

    // AC-3 — allowlist: discriminador desconhecido nunca instancia tipo
    [Fact]
    public void DeserializeArgs_UnknownDiscriminator_Throws()
    {
        var serializer = NewSerializer();
        var payload = Encoding.UTF8.GetBytes("""{"v":1,"args":[{"t":"tipoMalicioso","d":{}}]}""");

        var ex = Assert.Throws<GuaraSerializationException>(() => serializer.DeserializeArgs(payload));
        Assert.Contains("tipoMalicioso", ex.Message);
    }

    // AC-3 (espelho) — serializar tipo não registrado falha com mensagem acionável
    [Fact]
    public void SerializeArgs_UnregisteredType_Throws()
    {
        var serializer = NewSerializer(SerializerTypeRegistry.CreateDefault()); // sem TestPayload

        var ex = Assert.Throws<GuaraSerializationException>(
            () => serializer.SerializeArgs(new object?[] { new TestPayload("x", 1) }));
        Assert.Contains(nameof(TestPayload), ex.Message);
    }

    // AC-4 — campo desconhecido (payload de versão futura) é ignorado
    [Fact]
    public void Deserialize_UnknownField_IsIgnored()
    {
        var serializer = NewSerializer();
        var json = Encoding.UTF8.GetBytes("""{"Name":"ok","Count":2,"CampoFuturo":true}""");

        var result = serializer.Deserialize<TestPayload>(json);

        Assert.Equal(new TestPayload("ok", 2), result);
    }

    // AC-5 — campo ausente (payload antigo) assume default
    [Fact]
    public void Deserialize_MissingField_UsesDefault()
    {
        var serializer = NewSerializer();
        var json = Encoding.UTF8.GetBytes("""{"Name":"ok"}""");

        var result = serializer.Deserialize<TestPayload>(json);

        Assert.NotNull(result);
        Assert.Equal("ok", result.Name);
        Assert.Equal(0, result.Count);
    }

    // AC-6 — payload corrompido gera erro determinístico (não crash)
    [Fact]
    public void Deserialize_CorruptPayload_ThrowsGuaraSerializationException()
    {
        var serializer = NewSerializer();
        var garbage = new byte[] { 0xFF, 0x00, 0x42, 0x13 };

        Assert.Throws<GuaraSerializationException>(() => serializer.Deserialize<TestPayload>(garbage));
        Assert.Throws<GuaraSerializationException>(() => serializer.DeserializeArgs(garbage));
    }

    // AC-7 — payload idêntico independentemente da cultura da máquina
    [Fact]
    public void SerializeArgs_IsCultureInvariant()
    {
        var serializer = NewSerializer();
        object?[] args = [1234.56, new DateTimeOffset(2026, 7, 16, 3, 0, 0, TimeSpan.Zero)];

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = serializer.SerializeArgs(args).ToArray();

            CultureInfo.CurrentCulture = new CultureInfo("pt-BR"); // vírgula decimal
            var ptBr = serializer.SerializeArgs(args).ToArray();

            Assert.Equal(invariant, ptBr);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // Envelope de versão futura é rejeitado explicitamente (DD-3)
    [Fact]
    public void DeserializeArgs_UnsupportedEnvelopeVersion_Throws()
    {
        var serializer = NewSerializer();
        var payload = Encoding.UTF8.GetBytes("""{"v":99,"args":[]}""");

        var ex = Assert.Throws<GuaraSerializationException>(() => serializer.DeserializeArgs(payload));
        Assert.Contains("99", ex.Message);
    }
}
