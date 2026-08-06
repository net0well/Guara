using Guara.Configuration;

namespace Guara.Cluster;

/// <summary>Leitura explícita da seção <c>Guara:Cluster</c> (AOT-safe, sem reflection).</summary>
internal static class ClusterOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, ClusterOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Cluster");
        options.LeadershipTtl =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.LeadershipTtl)) ?? options.LeadershipTtl;
        options.RenewInterval =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.RenewInterval)) ?? options.RenewInterval;
    }
}
