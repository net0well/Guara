using Guara.Configuration;

namespace Guara.Redis;

/// <summary>
/// Leitura explícita da seção <c>Guara:Redis</c> (AOT-safe, sem reflection). A connection
/// string é um segredo: lida daqui, nunca logada.
/// </summary>
internal static class RedisOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, RedisOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Redis");
        options.ConnectionString =
            GuaraConfigurationValues.ReadString(section, nameof(options.ConnectionString)) ?? options.ConnectionString;
        options.ChannelPrefix =
            GuaraConfigurationValues.ReadString(section, nameof(options.ChannelPrefix)) ?? options.ChannelPrefix;
    }
}
