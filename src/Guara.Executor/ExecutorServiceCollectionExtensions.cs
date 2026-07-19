using Guara.Abstractions;
using Guara.Core;
using Guara.Executor;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Executor</c>.</summary>
public static class ExecutorServiceCollectionExtensions
{
    /// <summary>
    /// Liga o executor do Guará: pipeline de execução, invocador de jobs (módulos
    /// gerados e/ou registro manual), metadados declarados e política de retentativa.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configureRetry">Ajuste opcional da política de retentativa global.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraExecutor(
        this IGuaraBuilder builder, Action<RetryOptions>? configureRetry = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<RetryOptions>(sp =>
        {
            // Precedência: defaults → seção Guara:Retry → delegate (o código vence).
            var options = new RetryOptions();
            RetryOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configureRetry?.Invoke(options);
            RetryOptionsBinder.Validate(options);
            return options;
        });
        builder.Services.TryAddSingleton<JobHandlerRegistry>(sp =>
        {
            // Módulos gerados registram-se na materialização; o registro manual
            // continua possível resolvendo o registry depois.
            var registry = new JobHandlerRegistry();
            foreach (var module in sp.GetServices<IJobModule>())
            {
                module.Register(registry);
            }

            return registry;
        });
        builder.Services.TryAddSingleton<IJobMetadataProvider>(
            sp => sp.GetRequiredService<JobHandlerRegistry>());
        builder.Services.TryAddSingleton<IJobInvoker>(
            sp => new RegistryJobInvoker(sp.GetRequiredService<JobHandlerRegistry>(), sp));
        builder.Services.TryAddSingleton<IExecutor>(sp => new GuaraExecutor(
            sp.GetRequiredService<Guara.Storage.IStorage>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IJobInvoker>(),
            sp.GetRequiredService<IJobMetadataProvider>(),
            sp.GetRequiredService<RetryOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetServices<IJobMiddleware>(),
            sp.GetService<ILogger<GuaraExecutor>>() ?? NullLogger<GuaraExecutor>.Instance));
        return builder;
    }
}
