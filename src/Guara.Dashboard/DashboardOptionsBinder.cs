using Guara.Configuration;

namespace Guara.Dashboard;

/// <summary>Leitura explícita da seção <c>Guara:Dashboard</c> (AOT-safe, sem reflection).</summary>
internal static class DashboardOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, DashboardOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Dashboard");
        options.BasePath =
            GuaraConfigurationValues.ReadString(section, nameof(options.BasePath)) ?? options.BasePath;
        options.RequireAuthorization =
            GuaraConfigurationValues.ReadBoolean(section, nameof(options.RequireAuthorization)) ?? options.RequireAuthorization;
        options.CookieSecret =
            GuaraConfigurationValues.ReadString(section, nameof(options.CookieSecret)) ?? options.CookieSecret;
        options.SessionTtl =
            GuaraConfigurationValues.ReadTimeSpan(section, nameof(options.SessionTtl)) ?? options.SessionTtl;
    }
}
