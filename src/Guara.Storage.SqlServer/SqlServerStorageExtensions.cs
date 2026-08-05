using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Storage.SqlServer</c>.</summary>
public static class SqlServerStorageExtensions
{
    /// <summary>
    /// Usa SQL Server como storage do Guará ("o storage é a fila") com a connection
    /// string informada.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="connectionString">Connection string do banco.</param>
    /// <param name="configure">Ajuste opcional (schema, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseSqlServerStorage(
        this IGuaraBuilder builder, string connectionString, Action<SqlServerStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseSqlServerStorage(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Usa SQL Server como storage do Guará, com a connection string vinda da seção
    /// <c>Guara:Storage:SqlServer</c> (via <c>UseConfiguration</c>) e/ou do delegate.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (connection string, schema, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseSqlServerStorage(
        this IGuaraBuilder builder, Action<SqlServerStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IStorage>(sp =>
        {
            // Precedência: defaults → seção Guara:Storage:SqlServer → delegate (o código vence).
            var options = new SqlServerStorageOptions();
            SqlServerStorageOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return new SqlServerStorage(options, sp.GetRequiredService<TimeProvider>());
        });
        return builder;
    }
}
