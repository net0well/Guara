using Microsoft.CodeAnalysis;

namespace Guara.SourceGenerators;

/// <summary>
/// Mapa dos tipos de argumento suportados → trechos de escrita/leitura JSON gerados
/// (Utf8JsonWriter/Reader diretos — sem contexto de serialização em runtime, porque
/// generators não enxergam a saída uns dos outros). Tipos fora do mapa geram erro de
/// compilação, nunca falha em produção.
/// </summary>
internal static class ArgTypeMap
{
    private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";

    private static readonly SymbolDisplayFormat FullyQualifiedNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Resolve o parâmetro para o modelo com os trechos de serialização.</summary>
    /// <param name="name">Nome do parâmetro.</param>
    /// <param name="type">Tipo do parâmetro.</param>
    /// <returns>O modelo (com <see cref="ArgKind.Unsupported"/> quando fora do mapa).</returns>
    public static ParameterModel Resolve(string name, ITypeSymbol type)
    {
        var fullType = type.ToDisplayString(FullyQualifiedNullable);
        var value = $"@{name}";

        // Nullable<T>: embrulha os trechos do tipo interno com o tratamento de null.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var inner = Core(nullable.TypeArguments[0], $"{value}.Value");
            if (inner is null)
            {
                return new ParameterModel(name, fullType, ArgKind.Unsupported, false);
            }

            var (kind, writer, reader) = inner.Value;
            return new ParameterModel(
                name, fullType, kind, false,
                WriterStatement: $"if ({value}.HasValue) {{ {writer} }} else {{ writer.WriteNullValue(); }}",
                ReaderExpression: $"reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? default({fullType}) : {reader}");
        }

        var core = Core(type, value);
        if (core is null)
        {
            return new ParameterModel(name, fullType, ArgKind.Unsupported, false);
        }

        var (coreKind, coreWriter, coreReader) = core.Value;

        // Referência declarada anulável (string?/Uri?): o payload pode carregar null.
        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            coreWriter = $"if ({value} is null) {{ writer.WriteNullValue(); }} else {{ {coreWriter} }}";
            coreReader = $"reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : {coreReader}";
        }

        return new ParameterModel(name, fullType, coreKind, false, coreWriter, coreReader);
    }

    private static (ArgKind Kind, string Writer, string Reader)? Core(ITypeSymbol type, string value)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            // Valor inteiro subjacente — estável para renomeações de membro.
            if (enumType.EnumUnderlyingType?.SpecialType == SpecialType.System_UInt64)
            {
                return null; // ulong não cabe em long: fora do mapa
            }

            var enumFullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (ArgKind.Enum,
                $"writer.WriteNumberValue((long){value});",
                $"({enumFullName})reader.GetInt64()");
        }

        return type.SpecialType switch
        {
            SpecialType.System_Byte => Number(value, "GetByte"),
            SpecialType.System_SByte => Number(value, "GetSByte"),
            SpecialType.System_Int16 => Number(value, "GetInt16"),
            SpecialType.System_UInt16 => Number(value, "GetUInt16"),
            SpecialType.System_Int32 => Number(value, "GetInt32"),
            SpecialType.System_UInt32 => Number(value, "GetUInt32"),
            SpecialType.System_Int64 => Number(value, "GetInt64"),
            SpecialType.System_UInt64 => Number(value, "GetUInt64"),
            SpecialType.System_Single => Number(value, "GetSingle"),
            SpecialType.System_Double => Number(value, "GetDouble"),
            SpecialType.System_Decimal => Number(value, "GetDecimal"),
            SpecialType.System_Boolean => (ArgKind.Boolean,
                $"writer.WriteBooleanValue({value});", "reader.GetBoolean()"),
            SpecialType.System_String => (ArgKind.String,
                $"writer.WriteStringValue({value});", "reader.GetString()!"),
            SpecialType.System_Char => (ArgKind.Char,
                $"writer.WriteStringValue({value}.ToString());", "reader.GetString()![0]"),
            SpecialType.System_DateTime => (ArgKind.DateTime,
                $"writer.WriteStringValue({value});", "reader.GetDateTime()"),
            _ => type.ToDisplayString() switch
            {
                "System.Guid" => (ArgKind.Guid,
                    $"writer.WriteStringValue({value});", "reader.GetGuid()"),
                "System.DateTimeOffset" => (ArgKind.DateTimeOffset,
                    $"writer.WriteStringValue({value});", "reader.GetDateTimeOffset()"),
                "System.TimeSpan" => (ArgKind.TimeSpan,
                    $"writer.WriteStringValue({value}.ToString(\"c\", {Invariant}));",
                    $"global::System.TimeSpan.ParseExact(reader.GetString()!, \"c\", {Invariant})"),
                "System.DateOnly" => (ArgKind.DateOnly,
                    $"writer.WriteStringValue({value}.ToString(\"O\", {Invariant}));",
                    $"global::System.DateOnly.Parse(reader.GetString()!, {Invariant})"),
                "System.TimeOnly" => (ArgKind.TimeOnly,
                    $"writer.WriteStringValue({value}.ToString(\"O\", {Invariant}));",
                    $"global::System.TimeOnly.Parse(reader.GetString()!, {Invariant})"),
                "System.Uri" => (ArgKind.Uri,
                    $"writer.WriteStringValue({value}.ToString());",
                    "new global::System.Uri(reader.GetString()!, global::System.UriKind.RelativeOrAbsolute)"),
                _ => ((ArgKind, string, string)?)null,
            },
        };

        static (ArgKind, string, string) Number(string value, string readerMethod)
            => (ArgKind.Number, $"writer.WriteNumberValue({value});", $"reader.{readerMethod}()");
    }
}
