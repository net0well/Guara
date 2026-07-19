using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Guara.SourceGenerators;

/// <summary>
/// Generator incremental do Guará: descobre métodos <c>[GuaraJob]</c> e emite, por
/// assembly, o registro em DI (<c>AddGuaraJobs()</c>), o módulo de invocação sem
/// reflection com os metadados dos atributos e as factories tipadas de descritor.
/// Parser e emitter são separados; o pipeline só carrega modelos equatable.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GuaraJobGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var jobs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Guara.Abstractions.GuaraJobAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => JobParser.Parse(ctx))
            .Collect();

        var assemblyName = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "GuaraJobs");

        context.RegisterSourceOutput(jobs.Combine(assemblyName), static (spc, source) =>
        {
            var text = JobEmitter.Emit(
                source.Left, source.Right,
                model => spc.ReportDiagnostic(JobDiagnostics.ToDiagnostic(model)));
            if (text is not null)
            {
                spc.AddSource("GuaraJobs.g.cs", SourceText.From(text, System.Text.Encoding.UTF8));
            }
        });
    }
}
