using Guara.Abstractions;
using Guara.Dashboard;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Dashboard</c>.</summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Liga o dashboard completo: a API (<c>Guara.Dashboard.Api</c>), as opções da
    /// composição e a autenticação com regras fluentes —
    /// <c>AddGuaraDashboard(dash =&gt; dash.UseGuaraAuthentication(auth =&gt; ...))</c>.
    /// Monte as rotas com <c>app.MapGuaraDashboard()</c>.
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional (rota base, regras de acesso, sessão).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraDashboard(
        this IGuaraBuilder builder, Action<DashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddGuaraDashboardApi();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<DashboardOptions>(sp =>
        {
            // Precedência: defaults → seção Guara:Dashboard → delegate (o código vence).
            var options = new DashboardOptions();
            DashboardOptionsBinder.Bind(sp.GetService<Guara.Configuration.GuaraConfiguration>(), options);
            configure?.Invoke(options);
            options.Validate();

            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Guara.Dashboard")
                ?? NullLogger.Instance;
            if (!options.RequireAuthorization)
            {
                logger.LogWarning(
                    "Dashboard do Guará SEM autorização (RequireAuthorization=false): qualquer um acessa " +
                    "os dados e as ações de jobs. Use apenas em ambiente fechado.");
            }
            else if (options.Access is null)
            {
                logger.LogInformation(
                    "Dashboard sem regras próprias: exigindo usuário autenticado pela aplicação. " +
                    "Configure UseGuaraAuthentication(...) para regras finas ou login fixo.");
            }

            if (options.Access?.FixedLogin is not null)
            {
                logger.LogWarning(
                    "Login fixo do dashboard habilitado: mantenha a senha fora do código " +
                    "(variável de ambiente/user-secrets) e sirva sob HTTPS.");
            }

            return options;
        });
        builder.Services.TryAddSingleton<DashboardSessionService>(sp => new DashboardSessionService(
            sp.GetRequiredService<DashboardOptions>(), sp.GetRequiredService<TimeProvider>()));
        builder.Services.TryAddSingleton<LoginRateLimiter>(sp => new LoginRateLimiter(
            sp.GetRequiredService<TimeProvider>()));
        return builder;
    }
}
