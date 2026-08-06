using Guara.Abstractions;
using Guara.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

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

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<WorkerOptions>(sp =>
        {
            // Precedência: defaults → seção Guara:Worker → delegate (o código vence).
            var options = new WorkerOptions();
            WorkerOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return options;
        });
        builder.Services.TryAddSingleton(sp => new GuaraWorker(
            sp.GetRequiredService<Guara.Storage.IStorage>(),
            sp.GetRequiredService<IExecutor>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<WorkerOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<GuaraWorker>>() ?? NullLogger<GuaraWorker>.Instance));
        builder.Services.TryAddSingleton<IWorker>(sp => sp.GetRequiredService<GuaraWorker>());
        // O dispatcher enxerga a capacidade sem enxergar o worker inteiro: assim ele
        // dimensiona o lote sem ganhar o poder de ligar e desligar quem executa.
        builder.Services.TryAddSingleton<IWorkerCapacity>(sp => sp.GetRequiredService<GuaraWorker>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<WorkerRequested>, WorkerRequestedForwarder>());
        return builder;
    }
}
