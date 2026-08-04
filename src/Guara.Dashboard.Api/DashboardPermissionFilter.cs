using Guara.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Dashboard.Api;

/// <summary>
/// Ação exigida por um endpoint do painel, anexada como metadado da rota para que a
/// exigência viva ao lado do mapeamento e não numa tabela paralela.
/// </summary>
/// <param name="Action">Nome da ação (ver <see cref="GuaraActions"/>).</param>
internal sealed record DashboardActionMetadata(string Action);

/// <summary>
/// Aplica a permissão declarada pela rota. Sem um <see cref="IGuaraAuthorizer"/>
/// registrado, o painel segue tudo-ou-nada — quem passou pelas regras de acesso opera
/// tudo, que é o comportamento de quem não ligou <c>AddGuaraAuthorization()</c>.
/// </summary>
internal sealed class DashboardPermissionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // Resolvido por requisição (e não no construtor) porque o autorizador é opcional:
        // ActivatorUtilities não sabe tratar dependência ausente como nula.
        var authorizer = http.RequestServices.GetService<IGuaraAuthorizer>();
        if (authorizer is null)
        {
            return await next(context);
        }

        if (http.GetEndpoint()?.Metadata.GetMetadata<DashboardActionMetadata>() is not { } required)
        {
            return await next(context);
        }

        if (await authorizer.AuthorizeAsync(http.User, required.Action, http.RequestAborted))
        {
            return await next(context);
        }

        return Results.Problem(
            $"Sua identidade não tem a permissão '{required.Action}' para esta ação do painel.",
            statusCode: StatusCodes.Status403Forbidden);
    }
}

/// <summary>Declara a ação exigida por uma rota do painel.</summary>
internal static class DashboardPermissionExtensions
{
    public static RouteHandlerBuilder RequireAction(this RouteHandlerBuilder builder, string action)
        => builder.WithMetadata(new DashboardActionMetadata(action));
}
