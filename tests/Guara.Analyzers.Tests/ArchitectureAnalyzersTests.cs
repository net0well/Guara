using System.Collections.Immutable;
using Guara.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Guara.Analyzers.Tests;

public class ArchitectureAnalyzersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<MetadataReference> PlataformaBase =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(caminho => (MetadataReference)MetadataReference.CreateFromFile(caminho))];

    /// <summary>Compila um assembly falso com o nome pedido, para servir de referência.</summary>
    private static MetadataReference Falso(string nomeDoAssembly, string codigo)
    {
        var compilacao = CSharpCompilation.Create(
            nomeDoAssembly,
            [CSharpSyntaxTree.ParseText(codigo, new CSharpParseOptions(LanguageVersion.Latest))],
            PlataformaBase,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var stream = new MemoryStream();
        var emissao = compilacao.Emit(stream);
        Assert.True(emissao.Success, string.Join("\n", emissao.Diagnostics));
        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalisarAsync(
        DiagnosticAnalyzer analisador,
        string nomeDoAssembly,
        string codigo,
        params MetadataReference[] referencias)
    {
        var compilacao = CSharpCompilation.Create(
            nomeDoAssembly,
            [CSharpSyntaxTree.ParseText(codigo, new CSharpParseOptions(LanguageVersion.Latest))],
            [.. PlataformaBase, .. referencias],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return await compilacao
            .WithAnalyzers([analisador])
            .GetAnalyzerDiagnosticsAsync(Ct);
    }

    // --- GUARA0001: dependência invertida ---

    [Fact]
    public async Task Abstractions_ReferenciandoCore_Acusa()
    {
        var core = Falso("Guara.Core", "namespace Guara.Core { public class Pipeline { } }");

        var diagnosticos = await AnalisarAsync(
            new DependencyDirectionAnalyzer(),
            "Guara.Abstractions",
            "namespace Guara.Abstractions { public interface IContrato { } }",
            core);

        var d = Assert.Single(diagnosticos);
        Assert.Equal("GUARA0001", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Guara.Core", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Core_ReferenciandoAbstractions_NaoAcusa()
    {
        var abstracoes = Falso("Guara.Abstractions", "namespace Guara.Abstractions { public interface IContrato { } }");

        var diagnosticos = await AnalisarAsync(
            new DependencyDirectionAnalyzer(),
            "Guara.Core",
            "namespace Guara.Core { public class Pipeline { } }",
            abstracoes);

        Assert.Empty(diagnosticos);
    }

    [Fact]
    public async Task Provider_ReferenciandoContratosDeStorage_NaoAcusa()
    {
        var contratos = Falso("Guara.Storage", "namespace Guara.Storage { public interface IStorage { } }");

        var diagnosticos = await AnalisarAsync(
            new DependencyDirectionAnalyzer(),
            "Guara.Storage.PostgreSql",
            "namespace Guara.Storage.PostgreSql { internal class Impl { } }",
            contratos);

        Assert.Empty(diagnosticos);
    }

    [Fact]
    public async Task Provider_ReferenciandoMotor_Acusa()
    {
        var motor = Falso("Guara.Worker", "namespace Guara.Worker { public class Motor { } }");

        var diagnosticos = await AnalisarAsync(
            new DependencyDirectionAnalyzer(),
            "Guara.Storage.PostgreSql",
            "namespace Guara.Storage.PostgreSql { internal class Impl { } }",
            motor);

        Assert.Equal("GUARA0001", Assert.Single(diagnosticos).Id);
    }

    [Fact]
    public async Task AplicacaoDoUsuario_NaoEAnalisada()
    {
        var painel = Falso("Guara.Dashboard", "namespace Guara.Dashboard { public class Painel { } }");

        // Um app referencia tudo por definição; a regra vale entre componentes do produto.
        var diagnosticos = await AnalisarAsync(
            new DependencyDirectionAnalyzer(),
            "MinhaApi",
            "namespace MinhaApi { public class Program { } }",
            painel);

        Assert.Empty(diagnosticos);
    }

    // --- GUARA0002: motor alcançando provider concreto ---

    private const string ProviderFalso = """
        namespace Guara.Storage.PostgreSql
        {
            public class PostgreSqlStorage { public static int Contador; }
        }
        """;

    [Fact]
    public async Task Motor_UsandoTipoDoProvider_Acusa()
    {
        var provider = Falso("Guara.Storage.PostgreSql", ProviderFalso);

        var diagnosticos = await AnalisarAsync(
            new ConcreteProviderAnalyzer(),
            "Guara.Dispatcher",
            """
            using Guara.Storage.PostgreSql;
            namespace Guara.Dispatcher
            {
                internal class Busca { private PostgreSqlStorage? _storage; }
            }
            """,
            provider);

        var d = Assert.Single(diagnosticos);
        Assert.Equal("GUARA0002", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("PostgreSqlStorage", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Composicao_UsandoTipoDoProvider_NaoAcusa()
    {
        var provider = Falso("Guara.Storage.PostgreSql", ProviderFalso);

        // Hosting e aplicações escolhem o provider: é exatamente o ponto de composição.
        var diagnosticos = await AnalisarAsync(
            new ConcreteProviderAnalyzer(),
            "Guara.Hosting",
            """
            using Guara.Storage.PostgreSql;
            namespace Guara.Hosting
            {
                internal class Composicao { private PostgreSqlStorage? _storage; }
            }
            """,
            provider);

        Assert.Empty(diagnosticos);
    }

    [Fact]
    public async Task Motor_UsandoContratoDeStorage_NaoAcusa()
    {
        var contratos = Falso("Guara.Storage", "namespace Guara.Storage { public interface IStorage { } }");

        var diagnosticos = await AnalisarAsync(
            new ConcreteProviderAnalyzer(),
            "Guara.Dispatcher",
            """
            using Guara.Storage;
            namespace Guara.Dispatcher
            {
                internal class Busca { private IStorage? _storage; }
            }
            """,
            contratos);

        Assert.Empty(diagnosticos);
    }
}
