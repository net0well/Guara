using Guara.Configuration;

namespace Guara.Worker;

/// <summary>Leitura explícita da seção <c>Guara:Worker</c> (AOT-safe, sem reflection).</summary>
internal static class WorkerOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, WorkerOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Worker");
        options.MaxConcurrency =
            GuaraConfigurationValues.ReadInt32(section, nameof(options.MaxConcurrency)) ?? options.MaxConcurrency;
        options.ShutdownDrainTimeout =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.ShutdownDrainTimeout)) ?? options.ShutdownDrainTimeout;
        options.LeaseRenewInterval =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.LeaseRenewInterval)) ?? options.LeaseRenewInterval;
        options.LeaseDuration =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.LeaseDuration)) ?? options.LeaseDuration;
    }
}
