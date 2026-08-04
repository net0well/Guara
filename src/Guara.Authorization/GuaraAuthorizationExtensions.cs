using Guara.Abstractions;
using Guara.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>
/// Extensão única do pacote <c>Guara.Authorization</c>. O nome evita colidir com o
/// <c>AuthorizationServiceCollectionExtensions</c> do próprio ASP.NET Core, que vive
/// neste mesmo namespace.
/// </summary>
public static class GuaraAuthorizationExtensions
{
    /// <summary>
    /// Liga as permissões por ação do painel. Sem esta chamada o painel continua
    /// tudo-ou-nada (quem passa pelas regras de acesso faz tudo); com ela, cada ação
    /// passa a exigir sua concessão — e o que não foi concedido é negado.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Mapeamento de ações para policies e papéis de administrador.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraAuthorization(
        this IGuaraBuilder builder, Action<GuaraAuthorizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Núcleo de policies do ASP.NET Core (avaliador, provider e handlers), sem o
        // middleware HTTP — o painel avalia por endpoint, não por pipeline. É idempotente
        // e preserva as policies que o host já tenha registrado.
        builder.Services.AddAuthorizationCore();

        builder.Services.TryAddSingleton<GuaraAuthorizationOptions>(_ =>
        {
            var options = new GuaraAuthorizationOptions();
            configure?.Invoke(options);
            options.Validate();
            return options;
        });

        builder.Services.TryAddSingleton<IGuaraAuthorizer>(sp => new PolicyGuaraAuthorizer(
            sp.GetRequiredService<IAuthorizationService>(),
            sp.GetRequiredService<GuaraAuthorizationOptions>(),
            sp.GetService<ILogger<PolicyGuaraAuthorizer>>() ?? NullLogger<PolicyGuaraAuthorizer>.Instance));

        return builder;
    }
}
