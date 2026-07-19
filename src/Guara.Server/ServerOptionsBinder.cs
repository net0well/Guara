using Guara.Configuration;

namespace Guara.Server;

/// <summary>Leitura explícita da seção <c>Guara:Server</c> (AOT-safe, sem reflection).</summary>
internal static class ServerOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, ServerOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Server");
        options.HeartbeatInterval =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.HeartbeatInterval)) ?? options.HeartbeatInterval;
        options.ServerTimeout =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.ServerTimeout)) ?? options.ServerTimeout;
        options.MaintenanceInterval =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.MaintenanceInterval)) ?? options.MaintenanceInterval;
        options.RecurringPollInterval =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.RecurringPollInterval)) ?? options.RecurringPollInterval;

        var retention = section.GetSection(nameof(options.Retention));
        var succeeded = GuaraConfigurationValues.ReadTimeSpan(retention, nameof(RetentionPolicy.Succeeded));
        var failed = GuaraConfigurationValues.ReadTimeSpan(retention, nameof(RetentionPolicy.Failed));
        if (succeeded is not null || failed is not null)
        {
            options.Retention = new RetentionPolicy(
                succeeded ?? options.Retention.Succeeded,
                failed ?? options.Retention.Failed);
        }
    }
}
