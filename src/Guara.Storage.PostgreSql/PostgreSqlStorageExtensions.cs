using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Storage.PostgreSql</c>.</summary>
public static class PostgreSqlStorageExtensions
{
    /// <summary>
    /// Usa PostgreSQL como storage do Guará ("o storage é a fila") com a connection
    /// string informada.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="connectionString">Connection string do banco.</param>
    /// <param name="configure">Ajuste opcional (schema, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UsePostgreSqlStorage(
        this IGuaraBuilder builder, string connectionString, Action<PostgreSqlStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UsePostgreSqlStorage(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Usa PostgreSQL como storage do Guará, com a connection string vinda da seção
    /// <c>Guara:Storage:PostgreSql</c> (via <c>UseConfiguration</c>) e/ou do delegate.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (connection string, schema, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UsePostgreSqlStorage(
        this IGuaraBuilder builder, Action<PostgreSqlStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IStorage>(sp =>
        {
            // Precedência: defaults → seção Guara:Storage:PostgreSql → delegate (o código vence).
            var options = new PostgreSqlStorageOptions();
            PostgreSqlStorageOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return new PostgreSqlStorage(options, sp.GetRequiredService<TimeProvider>());
        });
        return builder;
    }
}
