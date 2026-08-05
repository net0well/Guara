using Guara.Configuration;

namespace Guara.Storage.MySql;

/// <summary>
/// Leitura explícita da seção <c>Guara:Storage:MySql</c> (AOT-safe, sem reflection).
/// A connection string é um segredo: lida daqui, nunca logada.
/// </summary>
internal static class MySqlStorageOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, MySqlStorageOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Storage").GetSection("MySql");
        options.ConnectionString =
            GuaraConfigurationValues.ReadString(section, nameof(options.ConnectionString)) ?? options.ConnectionString;
        options.TablePrefix =
            GuaraConfigurationValues.ReadString(section, nameof(options.TablePrefix)) ?? options.TablePrefix;
        options.AutoMigrate =
            GuaraConfigurationValues.ReadBoolean(section, nameof(options.AutoMigrate)) ?? options.AutoMigrate;
    }
}
