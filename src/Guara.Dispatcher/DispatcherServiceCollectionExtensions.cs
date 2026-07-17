using Guara.Abstractions;
using Guara.Dispatcher;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // namespace obrigatório (ADR-0006)

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

        var options = new DispatcherOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<IDispatcher>(sp => new GuaraDispatcher(
            sp.GetRequiredService<Guara.Storage.IStorage>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<DispatcherOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<GuaraDispatcher>>() ?? NullLogger<GuaraDispatcher>.Instance));
        return builder;
    }
}
