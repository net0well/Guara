using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Guara.Abstractions;

namespace Guara.Serialization;

/// <summary>
/// Implementação default de <see cref="ISerializer"/> sobre System.Text.Json com
/// source generators — zero reflection, AOT-safe.
/// Argumentos de job trafegam num envelope versionado com <b>discriminador</b> por
/// argumento; a desserialização resolve tipos apenas pela allowlist
/// (<see cref="SerializerTypeRegistry"/>) — nunca por nome de tipo no payload.
/// Tolerante a versão: campos desconhecidos são ignorados; ausentes assumem default.
/// </summary>
internal sealed class SystemTextJsonSerializer : ISerializer
{
    private const int EnvelopeVersion = 1;

    private readonly SerializerTypeRegistry _registry;
    private readonly JsonSerializerOptions _options;

    /// <summary>Cria o serializer.</summary>
    /// <param name="registry">Allowlist de tipos para argumentos de job.</param>
    /// <param name="options">
    /// Opções com os contextos source-gen no <c>TypeInfoResolverChain</c>;
    /// quando omitido, usa <see cref="GuaraJsonContext"/> (tipos do framework + primitivos).
    /// </param>
    public SystemTextJsonSerializer(SerializerTypeRegistry registry, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _options = options ?? new JsonSerializerOptions { TypeInfoResolver = GuaraJsonContext.Default };
        _options.MakeReadOnly();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        var typeInfo = (JsonTypeInfo<T>)GetTypeInfo(typeof(T));
        return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        var typeInfo = (JsonTypeInfo<T>)GetTypeInfo(typeof(T));
        try
        {
            return JsonSerializer.Deserialize(data, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new GuaraSerializationException(
                $"Payload inválido/corrompido ao desserializar '{typeof(T)}'.", ex);
        }
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> SerializeArgs(ReadOnlySpan<object?> args)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteNumber("v", EnvelopeVersion);
        writer.WriteStartArray("args");

        foreach (var arg in args)
        {
            writer.WriteStartObject();
            if (arg is null)
            {
                writer.WriteNull("t");
                writer.WriteNull("d");
            }
            else
            {
                var type = arg.GetType();
                if (!_registry.TryGetDiscriminator(type, out var discriminator))
                {
                    throw new GuaraSerializationException(
                        $"O tipo '{type}' não está registrado na allowlist de serialização. " +
                        "Registre-o no SerializerTypeRegistry com um discriminador estável.");
                }

                writer.WriteString("t", discriminator);
                writer.WritePropertyName("d");
                JsonSerializer.Serialize(writer, arg, GetTypeInfo(type));
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenMemory;
    }

    /// <inheritdoc />
    public object?[] DeserializeArgs(ReadOnlySpan<byte> data)
    {
        try
        {
            var reader = new Utf8JsonReader(data);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            var version = root.GetProperty("v").GetInt32();
            if (version != EnvelopeVersion)
            {
                throw new GuaraSerializationException(
                    $"Versão de envelope de argumentos não suportada: {version} (esperada: {EnvelopeVersion}).");
            }

            var argsElement = root.GetProperty("args");
            var result = new object?[argsElement.GetArrayLength()];
            var index = 0;

            foreach (var element in argsElement.EnumerateArray())
            {
                var discriminatorElement = element.GetProperty("t");
                if (discriminatorElement.ValueKind == JsonValueKind.Null)
                {
                    result[index++] = null;
                    continue;
                }

                var discriminator = discriminatorElement.GetString()!;
                if (!_registry.TryGetType(discriminator, out var type))
                {
                    // Allowlist: nunca instancia tipo desconhecido vindo do payload.
                    throw new GuaraSerializationException(
                        $"O discriminador '{discriminator}' não está registrado na allowlist de serialização.");
                }

                result[index++] = element.GetProperty("d").Deserialize(GetTypeInfo(type));
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new GuaraSerializationException("Envelope de argumentos inválido/corrompido.", ex);
        }
    }

    private JsonTypeInfo GetTypeInfo(Type type)
    {
        try
        {
            return _options.GetTypeInfo(type);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            throw new GuaraSerializationException(
                $"Nenhum contexto source-gen resolve o tipo '{type}'. " +
                "Adicione o tipo a um JsonSerializerContext e encadeie-o no TypeInfoResolverChain.", ex);
        }
    }
}
