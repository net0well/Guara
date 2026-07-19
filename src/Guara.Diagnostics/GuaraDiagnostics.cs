using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Guara.Diagnostics;

/// <summary>
/// Nomes e instrumentos de observabilidade do Guará. Colete com qualquer listener
/// padrão do .NET (OpenTelemetry, dotnet-counters, <c>ActivityListener</c>) — o
/// framework não força exporter.
/// </summary>
public static class GuaraDiagnostics
{
    /// <summary>Nome do <see cref="ActivitySource"/> dos spans de execução.</summary>
    public const string ActivitySourceName = "Guara";

    /// <summary>Nome do <see cref="Meter"/> das métricas.</summary>
    public const string MeterName = "Guara";

    /// <summary>Fonte dos spans de execução de jobs.</summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    /// <summary>Medidor das métricas <c>guara.*</c>.</summary>
    public static Meter Meter { get; } = new(MeterName);
}
