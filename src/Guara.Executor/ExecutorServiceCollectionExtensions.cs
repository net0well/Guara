using Guara.Abstractions;
using Guara.Core;
using Guara.Executor;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Executor</c>.</summary>
public static class ExecutorServiceCollectionExtensions
{
    /// <summary>
    /// Liga o executor do Guará: pipeline de execução, invocador de jobs
    /// (registro manual até o source generator) e política de retentativa.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configureRetry">Ajuste opcional da política de retentativa global.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraExecutor(
        this IGuaraBuilder builder, Action<RetryOptions>? configureRetry = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var retryOptions = new RetryOptions();
        configureRetry?.Invoke(retryOptions);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton(retryOptions);
        builder.Services.TryAddSingleton<JobHandlerRegistry>();
        builder.Services.TryAddSingleton<IJobInvoker, RegistryJobInvoker>();
        builder.Services.TryAddSingleton<IExecutor, GuaraExecutor>();
        return builder;
    }
}
