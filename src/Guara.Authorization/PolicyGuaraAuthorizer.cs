using System.Security.Claims;
using Guara.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Guara.Authorization;

/// <summary>
/// Avalia as ações do painel sobre as policies do ASP.NET Core. A ordem é fixa e vai do
/// mais barato ao mais caro: anônimo nunca passa; papel de administrador concede tudo;
/// depois a policy da ação (ou a default); e, na ausência de policy, a claim explícita
/// de permissão. Toda saída não coberta por uma dessas concessões é negação.
/// </summary>
internal sealed class PolicyGuaraAuthorizer(
    IAuthorizationService authorization,
    GuaraAuthorizationOptions options,
    ILogger<PolicyGuaraAuthorizer> logger) : IGuaraAuthorizer
{
    public async ValueTask<bool> AuthorizeAsync(
        ClaimsPrincipal user, string action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ct.ThrowIfCancellationRequested();

        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        foreach (var role in options.AdminRoles)
        {
            if (user.IsInRole(role))
            {
                return true;
            }
        }

        var policy = options.ActionPolicies.TryGetValue(action, out var mapped)
            ? mapped
            : options.DefaultPolicy;

        if (policy is not null)
        {
            // O IAuthorizationService do ASP.NET Core não recebe token: a avaliação de
            // policy é local e síncrona na prática, então não há o que cancelar aqui.
            var result = await authorization.AuthorizeAsync(user, resource: null, policy);
            if (!result.Succeeded)
            {
                logger.LogDebug(
                    "Ação {Action} negada para {User}: policy {Policy} não satisfeita",
                    action, user.Identity.Name, policy);
            }

            return result.Succeeded;
        }

        var granted = user.HasClaim(GuaraClaimTypes.Permission, action);
        if (!granted)
        {
            logger.LogDebug(
                "Ação {Action} negada para {User}: sem policy mapeada e sem a claim de permissão",
                action, user.Identity.Name);
        }

        return granted;
    }
}
