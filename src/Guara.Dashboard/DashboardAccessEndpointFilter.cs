using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Guara.Dashboard;

/// <summary>
/// Portão de acesso do dashboard, aplicado a todo o grupo protegido (API, stream e UI):
/// materializa a sessão do login fixo na identidade, avalia as regras em E (exceção em
/// regra = negado, fail-safe) e responde ao negado do jeito certo — navegador anônimo
/// vai à página de login; API recebe 401/403 em JSON.
/// </summary>
internal sealed class DashboardAccessEndpointFilter(
    DashboardOptions options,
    DashboardSessionService sessions,
    IServiceProvider services,
    ILogger logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // A sessão do login fixo vira identidade antes das regras rodarem.
        if (http.User.Identity?.IsAuthenticated != true
            && http.Request.Cookies.TryGetValue(DashboardSessionService.CookieName, out var token)
            && sessions.Validate(token) is { } principal)
        {
            http.User = principal;
        }

        if (!options.RequireAuthorization)
        {
            return await next(context);
        }

        var allowed = await EvaluateAsync(http);
        if (allowed)
        {
            return await next(context);
        }

        var authenticated = http.User.Identity?.IsAuthenticated == true;
        var wantsHtml = HttpMethods.IsGet(http.Request.Method)
            && http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

        if (!authenticated && options.Access?.FixedLogin is not null && wantsHtml)
        {
            var retorno = Uri.EscapeDataString(http.Request.Path + http.Request.QueryString);
            return Results.Redirect($"{options.BasePath}/login?retorno={retorno}");
        }

        return authenticated
            ? Results.Problem(
                "Você está autenticado, mas as regras de acesso do dashboard negaram a entrada.",
                statusCode: StatusCodes.Status403Forbidden)
            : Results.Problem(
                "Autenticação requerida para acessar o dashboard do Guará.",
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private async ValueTask<bool> EvaluateAsync(HttpContext http)
    {
        if (options.Access is not { } access)
        {
            // Sem regras configuradas: exige identidade autenticada do host.
            return http.User.Identity?.IsAuthenticated == true;
        }

        var contexto = new DashboardContext(http);
        foreach (var factory in access.Rules)
        {
            try
            {
                if (!await factory(services).AutorizarAsync(contexto, http.RequestAborted))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Fail-safe: regra que lança nunca "abre por falha".
                logger.LogWarning(ex, "Regra de acesso do dashboard lançou exceção; acesso negado");
                return false;
            }
        }

        return true;
    }
}
