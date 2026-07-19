using Guara.Abstractions;
using Guara.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Configuration</c>.</summary>
public static class ConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Liga a configuração externa do Guará: cada componente passa a ler sua seção
    /// <c>Guara:{Componente}</c> (appsettings/env/secrets) ao materializar as opções.
    /// Precedência: default das opções → configuração → delegate de código (o código
    /// vence). Valores inválidos falham no startup; segredos nunca são logados.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configuration">Raiz de configuração da aplicação.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseConfiguration(this IGuaraBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.TryAddSingleton(new GuaraConfiguration(configuration));
        return builder;
    }
}
