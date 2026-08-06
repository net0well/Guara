using Guara.Abstractions;
using Guara.Cluster;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Cluster</c>.</summary>
public static class ClusterServiceCollectionExtensions
{
    /// <summary>
    /// Liga a coordenação entre nós: eleição de líder com posse renovada sobre o lock
    /// distribuído do storage. O <c>AddGuaraServer()</c> já a liga — chamar aqui só é
    /// necessário para ajustar as opções ou para usar <see cref="ILeaderElection"/> em
    /// código próprio.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (validade da liderança, intervalo de renovação).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraCluster(
        this IGuaraBuilder builder, Action<ClusterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<ClusterOptions>(sp =>
        {
            // Precedência: defaults → seção Guara:Cluster → delegate (o código vence).
            var options = new ClusterOptions();
            ClusterOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return options;
        });
        builder.Services.TryAddSingleton<ILeaderElection>(sp => new LockLeaderElection(
            sp.GetRequiredService<Guara.Storage.IStorage>(),
            sp.GetRequiredService<ClusterOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<LockLeaderElection>>() ?? NullLogger<LockLeaderElection>.Instance));
        return builder;
    }
}
