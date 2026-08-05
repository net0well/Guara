using Guara.Configuration;

namespace Guara.Storage.Mongo;

/// <summary>
/// Leitura explícita da seção <c>Guara:Storage:Mongo</c> (AOT-safe, sem reflection).
/// A connection string é um segredo: lida daqui, nunca logada.
/// </summary>
internal static class MongoStorageOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, MongoStorageOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Storage").GetSection("Mongo");
        options.ConnectionString =
            GuaraConfigurationValues.ReadString(section, nameof(options.ConnectionString)) ?? options.ConnectionString;
        options.Database =
            GuaraConfigurationValues.ReadString(section, nameof(options.Database)) ?? options.Database;
        options.CollectionPrefix =
            GuaraConfigurationValues.ReadString(section, nameof(options.CollectionPrefix)) ?? options.CollectionPrefix;
        options.AutoMigrate =
            GuaraConfigurationValues.ReadBoolean(section, nameof(options.AutoMigrate)) ?? options.AutoMigrate;
    }
}
