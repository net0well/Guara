using Guara.Abstractions;
using Guara.Core;
using Guara.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Hosting</c> — o ponto de entrada do Guará.</summary>
public static class GuaraServiceCollectionExtensions
{
    /// <summary>
    /// Liga o núcleo do Guará (event bus, máquina de estados, opções globais) e devolve
    /// o builder fluente para encadear o storage e os demais componentes:
    /// <c>services.AddGuara().UseMemoryStorage().AddGuaraServer()</c>.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Ajuste opcional das opções globais (validadas imediatamente).</param>
    /// <returns>O builder do Guará, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuara(this IServiceCollection services, Action<GuaraOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GuaraOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JobStateMachine>();
        services.TryAddSingleton<IEventPublisher, InProcessEventPublisher>();

        return new GuaraBuilder(services);
    }
}
