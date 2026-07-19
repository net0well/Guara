using Guara.Abstractions;
using Guara.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Diagnostics</c>.</summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Liga a observabilidade do pipeline de execução: tracing (span por job na fonte
    /// <c>Guara</c>), logging estruturado e métricas (<c>guara.jobs.*</c>). O tracing
    /// envolve o logging, que envolve as métricas — logs saem correlacionados ao span.
    /// Colete com OpenTelemetry/dotnet-counters; nenhum exporter é imposto.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseGuaraDiagnostics(this IGuaraBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobMiddleware, TracingMiddleware>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobMiddleware, LoggingMiddleware>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobMiddleware, MetricsMiddleware>());
        return builder;
    }
}
