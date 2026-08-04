using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Guara.SourceGenerators;

/// <summary>Descriptors dos diagnósticos do generator (erros em build, nunca em runtime).</summary>
internal static class JobDiagnostics
{
    private const string Category = "Guara.SourceGenerators";

    /// <summary>Parâmetro de job com tipo fora do conjunto serializável suportado.</summary>
    public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        "GUARA0102", "Parâmetro de job não serializável",
        "O parâmetro '{0}' do job '{1}' tem o tipo '{2}', fora do conjunto suportado " +
        "(números, string, bool, char, Guid, datas/horas, TimeSpan, Uri, enums e seus anuláveis)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Placeholder de chave de concorrência sem argumento correspondente.</summary>
    public static readonly DiagnosticDescriptor InvalidConcurrencyPlaceholder = new(
        "GUARA0103", "Placeholder de chave inválido",
        "A chave de concorrência '{0}' do job '{1}' referencia o placeholder {{{2}}}, " +
        "mas o método tem apenas {3} argumento(s) serializável(is)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Métodos ou tipos genéricos não são suportados.</summary>
    public static readonly DiagnosticDescriptor GenericJobNotSupported = new(
        "GUARA0105", "Job genérico não suportado",
        "O job '{0}' é genérico (método ou tipo) — jobs genéricos não são suportados",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Retorno deve ser Task, ValueTask ou void.</summary>
    public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
        "GUARA0106", "Retorno de job não suportado",
        "O job '{0}' retorna '{1}' — use Task, ValueTask ou void (resultados tipados chegam depois)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>CancellationToken só é aceito como último parâmetro.</summary>
    public static readonly DiagnosticDescriptor CancellationTokenMustBeLast = new(
        "GUARA0107", "CancellationToken fora de posição",
        "No job '{0}', o CancellationToken deve ser o último parâmetro",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Dois jobs com o mesmo discriminador (tipo.método).</summary>
    public static readonly DiagnosticDescriptor DuplicateJob = new(
        "GUARA0108", "Job duplicado",
        "Mais de um método [GuaraJob] resolve para '{0}' — nomes de tipo+método devem ser únicos",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Parâmetro por referência não é suportado.</summary>
    public static readonly DiagnosticDescriptor ByRefParameterNotSupported = new(
        "GUARA0109", "Parâmetro por referência",
        "O parâmetro '{0}' do job '{1}' é ref/out/in — argumentos de job são por valor",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = new()
    {
        [UnsupportedParameterType.Id] = UnsupportedParameterType,
        [InvalidConcurrencyPlaceholder.Id] = InvalidConcurrencyPlaceholder,
        [GenericJobNotSupported.Id] = GenericJobNotSupported,
        [UnsupportedReturnType.Id] = UnsupportedReturnType,
        [CancellationTokenMustBeLast.Id] = CancellationTokenMustBeLast,
        [DuplicateJob.Id] = DuplicateJob,
        [ByRefParameterNotSupported.Id] = ByRefParameterNotSupported,
    };

    /// <summary>Reconstrói o <see cref="Diagnostic"/> a partir do modelo equatable.</summary>
    /// <param name="model">Modelo coletado pelo parser.</param>
    /// <returns>O diagnóstico pronto para reportar.</returns>
    public static Diagnostic ToDiagnostic(DiagnosticModel model)
    {
        var location = model.Location is { } l
            ? Location.Create(
                l.FilePath,
                new TextSpan(l.Start, l.Length),
                new LinePositionSpan(
                    new LinePosition(l.StartLine, l.StartCharacter),
                    new LinePosition(l.EndLine, l.EndCharacter)))
            : Location.None;

        object[] args = [.. model.MessageArgs.Items];
        return Diagnostic.Create(ById[model.Id], location, args);
    }
}
