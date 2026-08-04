using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Guara.SourceGenerators;

/// <summary>
/// Parser do pipeline: transforma o método <c>[GuaraJob]</c> em um <see cref="JobModel"/>
/// equatable — nenhum símbolo do Roslyn escapa daqui (disciplina incremental).
/// </summary>
internal static class JobParser
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>Extrai o modelo do método marcado.</summary>
    /// <param name="context">Contexto do <c>ForAttributeWithMetadataName</c>.</param>
    /// <returns>O modelo, ou <c>null</c> quando o alvo não é um método.</returns>
    public static JobModel? Parse(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol method)
        {
            return null;
        }

        var type = method.ContainingType;
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticModel>();
        var location = ToLocationModel(method.Locations.Length > 0 ? method.Locations[0] : null);
        var jobDisplay = $"{type.Name}.{method.Name}";

        if (method.IsGenericMethod || type.IsGenericType)
        {
            diagnostics.Add(new DiagnosticModel(
                JobDiagnostics.GenericJobNotSupported.Id, location, Args(jobDisplay)));
        }

        var returnKind = ResolveReturnKind(method);
        if (returnKind is null)
        {
            diagnostics.Add(new DiagnosticModel(
                JobDiagnostics.UnsupportedReturnType.Id, location,
                Args(jobDisplay, method.ReturnType.ToDisplayString())));
        }

        var parameters = ParseParameters(method, jobDisplay, location, diagnostics);
        var serializableCount = 0;
        foreach (var parameter in parameters)
        {
            if (!parameter.IsCancellationToken)
            {
                serializableCount++;
            }
        }

        var (queue, maxAttempts, timeoutSeconds, skipIfPrevious, disableConcurrency, keyTemplate, waitSeconds) =
            ReadBehaviorAttributes(method, type);

        if (keyTemplate is not null)
        {
            foreach (Match match in Placeholder.Matches(keyTemplate))
            {
                var index = int.Parse(match.Groups[1].Value);
                if (index >= serializableCount)
                {
                    diagnostics.Add(new DiagnosticModel(
                        JobDiagnostics.InvalidConcurrencyPlaceholder.Id, location,
                        Args(keyTemplate, jobDisplay, index.ToString(), serializableCount.ToString())));
                }
            }
        }

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new JobModel
        {
            TypeName = fullName.StartsWith("global::", System.StringComparison.Ordinal) ? fullName.Substring(8) : fullName,
            TypeFullName = fullName,
            TypeShortName = type.Name,
            MethodName = method.Name,
            ReturnKind = returnKind ?? ReturnKind.Task,
            IsStatic = method.IsStatic,
            Parameters = new EquatableArray<ParameterModel>([.. parameters]),
            Queue = queue,
            MaxAttempts = maxAttempts,
            TimeoutSeconds = timeoutSeconds,
            SkipIfPreviousRunning = skipIfPrevious,
            DisableConcurrency = disableConcurrency,
            ConcurrencyKeyTemplate = keyTemplate,
            ConcurrencyWaitSeconds = waitSeconds,
            Diagnostics = new EquatableArray<DiagnosticModel>(diagnostics.ToImmutable()),
            Location = location,
        };
    }

    private static ImmutableArray<ParameterModel> ParseParameters(
        IMethodSymbol method, string jobDisplay, LocationModel? location,
        ImmutableArray<DiagnosticModel>.Builder diagnostics)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterModel>(method.Parameters.Length);
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            var fullType = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (parameter.RefKind != RefKind.None)
            {
                diagnostics.Add(new DiagnosticModel(
                    JobDiagnostics.ByRefParameterNotSupported.Id, location, Args(parameter.Name, jobDisplay)));
                builder.Add(new ParameterModel(parameter.Name, fullType, ArgKind.Unsupported, false));
                continue;
            }

            if (IsCancellationToken(parameter.Type))
            {
                if (i != method.Parameters.Length - 1)
                {
                    diagnostics.Add(new DiagnosticModel(
                        JobDiagnostics.CancellationTokenMustBeLast.Id, location, Args(jobDisplay)));
                }

                builder.Add(new ParameterModel(parameter.Name, fullType, ArgKind.String, IsCancellationToken: true));
                continue;
            }

            var model = ArgTypeMap.Resolve(parameter.Name, parameter.Type);
            if (model.Kind == ArgKind.Unsupported)
            {
                diagnostics.Add(new DiagnosticModel(
                    JobDiagnostics.UnsupportedParameterType.Id, location,
                    Args(parameter.Name, jobDisplay, parameter.Type.ToDisplayString())));
            }

            builder.Add(model);
        }

        return builder.ToImmutable();
    }

    private static (string? Queue, int? MaxAttempts, int? TimeoutSeconds, bool SkipIfPrevious,
        bool DisableConcurrency, string? KeyTemplate, int WaitSeconds)
        ReadBehaviorAttributes(IMethodSymbol method, INamedTypeSymbol type)
    {
        string? queue = null;
        int? maxAttempts = null;
        int? timeoutSeconds = null;
        var skip = false;
        var disableConcurrency = false;
        string? keyTemplate = null;
        var waitSeconds = 0;

        // Classe primeiro, método depois: o método sobrescreve (precedência declarada).
        foreach (var attribute in type.GetAttributes())
        {
            Apply(attribute);
        }

        foreach (var attribute in method.GetAttributes())
        {
            Apply(attribute);
        }

        return (queue, maxAttempts, timeoutSeconds, skip, disableConcurrency, keyTemplate, waitSeconds);

        void Apply(AttributeData attribute)
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "Guara.Abstractions.GuaraFilaAttribute"
                    when attribute.ConstructorArguments.Length == 1
                         && attribute.ConstructorArguments[0].Value is string nome:
                    queue = nome;
                    break;
                case "Guara.Abstractions.GuaraRetentativasAttribute"
                    when attribute.ConstructorArguments.Length == 1
                         && attribute.ConstructorArguments[0].Value is int maximo:
                    maxAttempts = maximo;
                    break;
                case "Guara.Abstractions.GuaraTempoLimiteAttribute"
                    when attribute.ConstructorArguments.Length == 1
                         && attribute.ConstructorArguments[0].Value is int segundos:
                    timeoutSeconds = segundos;
                    break;
                case "Guara.Abstractions.GuaraPularSeAnteriorEmExecucaoAttribute":
                    skip = true;
                    break;
                case "Guara.Abstractions.GuaraDesabilitarConcorrenciaAttribute":
                    disableConcurrency = true;
                    foreach (var named in attribute.NamedArguments)
                    {
                        if (named.Key == "Chave" && named.Value.Value is string chave)
                        {
                            keyTemplate = chave;
                        }
                        else if (named.Key == "EsperaSegundos" && named.Value.Value is int espera)
                        {
                            waitSeconds = espera;
                        }
                    }

                    break;
            }
        }
    }

    private static ReturnKind? ResolveReturnKind(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
        {
            return ReturnKind.Void;
        }

        return method.ReturnType.ToDisplayString() switch
        {
            "System.Threading.Tasks.Task" => ReturnKind.Task,
            "System.Threading.Tasks.ValueTask" => ReturnKind.ValueTask,
            _ => null,
        };
    }

    private static bool IsCancellationToken(ITypeSymbol type)
        => type.ToDisplayString() == "System.Threading.CancellationToken";

    private static EquatableArray<string> Args(params string[] values)
        => new([.. values]);

    private static LocationModel? ToLocationModel(Location? location)
    {
        if (location is null || location.SourceTree is null)
        {
            return null;
        }

        var span = location.GetLineSpan();
        return new LocationModel(
            location.SourceTree.FilePath,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            span.StartLinePosition.Line,
            span.StartLinePosition.Character,
            span.EndLinePosition.Line,
            span.EndLinePosition.Character);
    }
}
