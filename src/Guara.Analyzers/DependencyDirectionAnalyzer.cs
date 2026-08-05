using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Guara.Analyzers;

/// <summary>
/// GUARA0001 — dependência invertida. Um pacote do Guará não pode referenciar outro que
/// esteja acima dele no grafo de camadas.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyDirectionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Id do diagnóstico.</summary>
    public const string DiagnosticId = "GUARA0001";

    private static readonly DiagnosticDescriptor Regra = new(
        DiagnosticId,
        title: "Dependência invertida entre componentes do Guará",
        messageFormat: "'{0}' referencia '{1}', que está acima dele na hierarquia de dependências; a seta aponta sempre para os contratos",
        category: "Guara.Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "As dependências do Guará são unidirecionais e terminam em Guara.Abstractions, que não "
            + "depende de nada. Referenciar um componente de cima cria ciclo conceitual e impede que "
            + "os pacotes evoluam e sejam publicados de forma independente.",
        helpLinkUri: "https://github.com/net0well/Guara/blob/main/docs/dependency-rules.md",
        // O diagnóstico sai no fim da compilação, e não de um nó de sintaxe. A marca avisa a
        // IDE disso: sem ela, o resultado seria descartado na análise em tempo real e a
        // violação só apareceria no build completo.
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Regra);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // A regra é sobre o grafo de referências, não sobre sintaxe: uma única avaliação por
        // compilação basta, e não custa nada enquanto se digita.
        context.RegisterCompilationAction(Analisar);
    }

    private static void Analisar(CompilationAnalysisContext context)
    {
        var atual = context.Compilation.Assembly.Name;
        if (!GuaraArchitecture.TentarObterCamada(atual, out var camadaAtual))
        {
            // Aplicação do usuário ou pacote de terceiro: as regras internas não valem.
            return;
        }

        foreach (var referencia in context.Compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var nome = referencia.Name;
            if (!GuaraArchitecture.TentarObterCamada(nome, out var camadaReferenciada))
            {
                continue;
            }

            if (camadaReferenciada > camadaAtual)
            {
                context.ReportDiagnostic(Diagnostic.Create(Regra, Location.None, atual, nome));
            }
        }
    }
}
