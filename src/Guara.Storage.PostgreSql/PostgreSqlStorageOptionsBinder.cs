using Guara.Configuration;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Leitura explícita da seção <c>Guara:Storage:PostgreSql</c> (AOT-safe, sem
/// reflection). A connection string é um segredo: lida daqui, nunca logada.
/// </summary>
internal static class PostgreSqlStorageOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, PostgreSqlStorageOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Storage").GetSection("PostgreSql");
        options.ConnectionString =
            GuaraConfigurationValues.ReadString(section, nameof(options.ConnectionString)) ?? options.ConnectionString;
        options.Schema =
            GuaraConfigurationValues.ReadString(section, nameof(options.Schema)) ?? options.Schema;
        options.AutoMigrate =
            GuaraConfigurationValues.ReadBoolean(section, nameof(options.AutoMigrate)) ?? options.AutoMigrate;
    }
}
