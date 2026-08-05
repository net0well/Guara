using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.Mongo;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Storage.Mongo</c>.</summary>
public static class MongoStorageExtensions
{
    /// <summary>
    /// Usa MongoDB como storage do Guará ("o storage é a fila") com a connection string
    /// informada.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="connectionString">Connection string do cluster.</param>
    /// <param name="configure">Ajuste opcional (banco, prefixo das coleções, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseMongoStorage(
        this IGuaraBuilder builder, string connectionString, Action<MongoStorageOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseMongoStorage(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Usa MongoDB como storage do Guará, com a connection string vinda da seção
    /// <c>Guara:Storage:Mongo</c> (via <c>UseConfiguration</c>) e/ou do delegate.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (connection string, banco, prefixo, AutoMigrate).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseMongoStorage(
        this IGuaraBuilder builder, Action<MongoStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IStorage>(sp =>
        {
            // Precedência: defaults → seção Guara:Storage:Mongo → delegate (o código vence).
            var options = new MongoStorageOptions();
            MongoStorageOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return new MongoStorage(options, sp.GetRequiredService<TimeProvider>());
        });
        return builder;
    }
}
