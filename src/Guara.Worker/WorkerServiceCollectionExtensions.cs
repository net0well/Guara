using Guara.Abstractions;
using Guara.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // namespace obrigatório (ADR-0006)

/// <summary>Extensão única do pacote <c>Guara.Worker</c>.</summary>
public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Liga o worker do Guará (slots de execução com renovação de lease e drain gracioso).
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional das opções do worker.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraWorker(
        this IGuaraBuilder builder, Action<WorkerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new WorkerOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton(sp => new GuaraWorker(
            sp.GetRequiredService<Guara.Storage.IStorage>(),
            sp.GetRequiredService<IExecutor>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<WorkerOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<GuaraWorker>>() ?? NullLogger<GuaraWorker>.Instance));
        builder.Services.TryAddSingleton<IWorker>(sp => sp.GetRequiredService<GuaraWorker>());
        builder.Services.AddSingleton<IEventHandler<WorkerRequested>>(
            sp => sp.GetRequiredService<GuaraWorker>());
        return builder;
    }
}
