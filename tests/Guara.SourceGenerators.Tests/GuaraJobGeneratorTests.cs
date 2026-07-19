using Guara.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Guara.SourceGenerators.Tests;

public class GuaraJobGeneratorTests
{
    private const string ValidJob = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Guara.Abstractions;

        namespace Demo.Jobs;

        public sealed class RelatorioServico;

        public sealed class RelatorioJobs(RelatorioServico servico)
        {
            [GuaraJob]
            [GuaraFila("relatorios")]
            [GuaraRetentativas(5)]
            [GuaraTempoLimite(300)]
            [GuaraDesabilitarConcorrencia(Chave = "cliente-{0}")]
            public Task GerarAsync(int clienteId, string? observacao, CancellationToken ct)
                => Task.CompletedTask;

            [GuaraJob]
            public ValueTask LimparAsync() => ValueTask.CompletedTask;
        }
        """;

    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(Abstractions.GuaraJobAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Executor.JobHandlerRegistry).Assembly.Location),
            ])
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Demo.Jobs",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new GuaraJobGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    [Fact]
    public void ValidJobs_GenerateModuleFactoryAndArgs_ThatCompile()
    {
        var (result, output) = Run(ValidJob);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("AddGuaraJobs", generated);
        Assert.Contains("RelatorioJobsGuara", generated);                       // factory tipada
        Assert.Contains("\"Demo.Jobs.RelatorioJobs\", \"GerarAsync\"", generated); // discriminador estável
        Assert.Contains("Queue = \"relatorios\"", generated);
        Assert.Contains("MaxAttempts = 5", generated);
        Assert.Contains("TimeoutSeconds = 300", generated);
        Assert.Contains("cliente-{0}", generated);

        // A prova mais forte: o código gerado compila junto com o consumidor, sem erros.
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Generation_IsDeterministic()
    {
        var first = Run(ValidJob).Result.GeneratedTrees.Single().ToString();
        var second = Run(ValidJob).Result.GeneratedTrees.Single().ToString();

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("public Task M(object payload) => Task.CompletedTask;", "GUARA0102")]
    [InlineData("public Task M<T>(int x) => Task.CompletedTask;", "GUARA0105")]
    [InlineData("public Task<int> M(int x) => Task.FromResult(x);", "GUARA0106")]
    [InlineData("public Task M(System.Threading.CancellationToken ct, int x) => Task.CompletedTask;", "GUARA0107")]
    [InlineData("public Task M(ref int x) => Task.CompletedTask;", "GUARA0109")]
    public void InvalidJobs_ReportBuildErrors(string method, string expectedId)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Guara.Abstractions;

            namespace Demo.Jobs;

            public sealed class Jobs
            {
                [GuaraJob]
                {{method}}
            }
            """;

        var (result, _) = Run(source);

        Assert.Contains(result.Diagnostics, d => d.Id == expectedId);
    }

    [Fact]
    public void InvalidConcurrencyPlaceholder_ReportsBuildError()
    {
        var source = """
            using System.Threading.Tasks;
            using Guara.Abstractions;

            namespace Demo.Jobs;

            public sealed class Jobs
            {
                [GuaraJob]
                [GuaraDesabilitarConcorrencia(Chave = "cliente-{2}")]
                public Task M(int clienteId) => Task.CompletedTask;
            }
            """;

        var (result, _) = Run(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "GUARA0103");
    }

    [Fact]
    public void DuplicateDiscriminator_ReportsBuildError()
    {
        var source = """
            using System.Threading.Tasks;
            using Guara.Abstractions;

            namespace Demo.Jobs;

            public sealed partial class Jobs
            {
                [GuaraJob]
                public Task M(int x) => Task.CompletedTask;
            }

            public sealed partial class Jobs
            {
                [GuaraJob]
                public Task M(string? y) => Task.CompletedTask;
            }
            """;

        var (result, _) = Run(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "GUARA0108");
    }

    [Fact]
    public void SkipIfPreviousRunning_IsStampedOnTypedDescriptor()
    {
        var source = """
            using System.Threading.Tasks;
            using Guara.Abstractions;

            namespace Demo.Jobs;

            public sealed class Sincronizacao
            {
                [GuaraJob]
                [GuaraPularSeAnteriorEmExecucao]
                public Task RodarAsync() => Task.CompletedTask;
            }
            """;

        var (result, output) = Run(source);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("guara-pular-se-anterior", generated); // a factory marca o descriptor
        Assert.Contains("SkipIfPreviousRunning = true", generated);
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void StaticJob_InvokesWithoutServiceResolution()
    {
        var source = """
            using System.Threading.Tasks;
            using Guara.Abstractions;

            namespace Demo.Jobs;

            public static class Manutencao
            {
                [GuaraJob]
                public static Task LimparAsync(int dias) => Task.CompletedTask;
            }
            """;

        var (result, output) = Run(source);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.DoesNotContain("GetRequiredService<global::Demo.Jobs.Manutencao>", generated);
        Assert.Contains("global::Demo.Jobs.Manutencao.LimparAsync", generated);
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
