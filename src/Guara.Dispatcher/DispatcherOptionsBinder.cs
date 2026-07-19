using Guara.Configuration;

namespace Guara.Dispatcher;

/// <summary>Leitura explícita da seção <c>Guara:Dispatcher</c> (AOT-safe, sem reflection).</summary>
internal static class DispatcherOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, DispatcherOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Dispatcher");
        options.PollingInterval =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.PollingInterval)) ?? options.PollingInterval;
        options.Queues =
            GuaraConfigurationValues.ReadStringArray(section, nameof(options.Queues)) ?? options.Queues;
        options.LeaseDuration =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.LeaseDuration)) ?? options.LeaseDuration;
        options.MaxBackoff =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.MaxBackoff)) ?? options.MaxBackoff;
    }
}
