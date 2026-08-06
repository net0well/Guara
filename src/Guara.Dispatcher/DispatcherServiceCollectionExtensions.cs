using Guara.Abstractions;
using Guara.Dispatcher;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Dispatcher</c>.</summary>
public static class DispatcherServiceCollectionExtensions
{
    /// <summary>
    /// Liga o dispatcher do Guará (busca de jobs elegíveis com aquisição atômica).
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional das opções do dispatcher.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraDispatcher(
        this IGuaraBuilder builder, Action<DispatcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<DispatcherOptions>(sp =>
        {
            // Precedência: defaults → seção Guara:Dispatcher → delegate (o código vence).
            var options = new DispatcherOptions();
            DispatcherOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();
            return options;
        });
        builder.Services.TryAddSingleton<IDispatcher>(sp => new GuaraDispatcher(
            sp.GetRequiredService<Guara.Storage.IStorage>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IQueueSignal>(),
            sp.GetRequiredService<IWorkerCapacity>(),
            sp.GetRequiredService<DispatcherOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<GuaraDispatcher>>() ?? NullLogger<GuaraDispatcher>.Instance));
        return builder;
    }
}
