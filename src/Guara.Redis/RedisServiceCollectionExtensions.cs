using Guara.Abstractions;
using Guara.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Redis</c>.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Usa o Redis para levar o aviso de trabalho novo entre nós: enfileirar num nó acorda
    /// o dispatcher de todos os outros, sem baixar o intervalo de busca. O storage não
    /// muda — a verdade durável continua no provider configurado.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="connectionString">Connection string do Redis.</param>
    /// <param name="configure">Ajuste opcional (prefixo dos canais).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseRedis(
        this IGuaraBuilder builder, string connectionString, Action<RedisOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseRedis(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Usa o Redis para levar o aviso de trabalho novo entre nós, com a connection string
    /// vinda da seção <c>Guara:Redis</c> (via <c>UseConfiguration</c>) e/ou do delegate.
    /// Quando a aplicação já registra um <c>IConnectionMultiplexer</c> no contêiner, o
    /// Guará usa o dela e dispensa a connection string.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (connection string, prefixo dos canais).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseRedis(this IGuaraBuilder builder, Action<RedisOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);

        // Remove em vez de TryAdd: AddGuara() já registrou o sinal em processo, e escolher
        // um transporte é justamente substituir aquele padrão.
        builder.Services.RemoveAll<IQueueSignal>();
        builder.Services.AddSingleton<IQueueSignal>(sp =>
        {
            // Precedência: defaults → seção Guara:Redis → delegate (o código vence).
            var options = new RedisOptions();
            RedisOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);

            var conexao = sp.GetService<IConnectionMultiplexer>();
            options.Validate(conexao is not null);

            return new RedisQueueSignal(
                options,
                conexao,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetService<ILogger<RedisQueueSignal>>() ?? NullLogger<RedisQueueSignal>.Instance);
        });
        return builder;
    }
}
