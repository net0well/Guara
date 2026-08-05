using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Guara.Analyzers;

/// <summary>
/// GUARA0002 — motor referenciando implementação concreta de provider. Os motores
/// conversam com <c>IStorage</c> e companhia; alcançar o provider os prende à tecnologia.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConcreteProviderAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Id do diagnóstico.</summary>
    public const string DiagnosticId = "GUARA0002";

    private static readonly DiagnosticDescriptor Regra = new(
        DiagnosticId,
        title: "Motor do Guará referenciando provider concreto",
        messageFormat: "'{0}' usa '{1}', de '{2}'; motores falam com os contratos, nunca com a tecnologia",
        category: "Guara.Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Trocar de provider precisa ser a troca de uma linha na composição. Um motor que "
            + "alcança a implementação concreta amarra o núcleo a um banco específico e quebra essa "
            + "promessa.",
        helpLinkUri: "https://github.com/net0well/Guara/blob/main/docs/anti-patterns.md");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Regra);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(inicio =>
        {
            // Só motores são analisados. Sair aqui evita percorrer a sintaxe de qualquer
            // outro projeto — inclusive o do usuário, que pode e deve nomear o provider.
            if (!GuaraArchitecture.EhMotor(inicio.Compilation.Assembly.Name))
            {
                return;
            }

            inicio.RegisterSyntaxNodeAction(
                Analisar,
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName);
        });
    }

    private static void Analisar(SyntaxNodeAnalysisContext context)
    {
        var simbolo = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;

        // Namespace não é acoplamento: cada segmento de um `using` resolve para um símbolo
        // de namespace, e acusá-los transformaria uma violação em três diagnósticos sobre a
        // mesma linha. O que prende o motor ao provider é usar o tipo.
        if (simbolo is null or INamespaceSymbol)
        {
            return;
        }

        if (simbolo.ContainingAssembly is not { } origem)
        {
            return;
        }

        if (!GuaraArchitecture.EhImplementacaoConcreta(origem.Name))
        {
            return;
        }

        var nome = simbolo is ITypeSymbol tipo ? tipo.Name : simbolo.ContainingType?.Name ?? simbolo.Name;
        context.ReportDiagnostic(Diagnostic.Create(
            Regra,
            ((SimpleNameSyntax)context.Node).GetLocation(),
            context.Compilation.Assembly.Name,
            nome,
            origem.Name));
    }
}
