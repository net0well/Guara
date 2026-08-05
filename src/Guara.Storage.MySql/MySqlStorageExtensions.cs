using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.MySql;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Storage.MySql</c>.</summary>
public static class MySqlStorageExtensions
{
    /// <summary>
    /// Usa MySQL 8+ como storage do Guará ("o storage é a fila") com a connection string
    /// informada.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="connectionString">Connection string do banco.</param>
    /// <param name="configure">Ajuste opcional (prefixo das tabelas, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseMySqlStorage(
        this IGuaraBuilder builder, string connectionString, Action<MySqlStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseMySqlStorage(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Usa MySQL 8+ como storage do Guará, com a connection string vinda da seção
    /// <c>Guara:Storage:MySql</c> (via <c>UseConfiguration</c>) e/ou do delegate.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (connection string, prefixo, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseMySqlStorage(
        this IGuaraBuilder builder, Action<MySqlStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IStorage>(sp =>
        {
            // Precedência: defaults → seção Guara:Storage:MySql → delegate (o código vence).
            var options = new MySqlStorageOptions();
            MySqlStorageOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return new MySqlStorage(options, sp.GetRequiredService<TimeProvider>());
        });
        return builder;
    }
}
