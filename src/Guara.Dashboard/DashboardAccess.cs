using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Guara.Dashboard;

/// <summary>
/// Regra de acesso ao dashboard — o equivalente Guará do
/// <c>IDashboardAuthorizationFilter</c> do Hangfire, com acesso completo à requisição.
/// Todas as regras embutidas implementam este contrato; regras custom entram via
/// <c>ComRegra&lt;T&gt;()</c>. Exceção em uma regra é tratada como <b>negado</b> (fail-safe).
/// </summary>
public interface IDashboardAccessRule
{
    /// <summary>Decide se a requisição pode acessar o dashboard.</summary>
    /// <param name="contexto">Contexto da requisição.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> para permitir.</returns>
    ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct);
}

/// <summary>Contexto da requisição ao dashboard (inclui o <see cref="HttpContext"/> completo).</summary>
public sealed class DashboardContext(HttpContext httpContext)
{
    /// <summary>A requisição completa — como o <c>GetHttpContext()</c> do Hangfire.</summary>
    public HttpContext HttpContext { get; } = httpContext;

    /// <summary>Identidade estabelecida (host ou sessão do login fixo).</summary>
    public ClaimsPrincipal User => HttpContext.User;

    /// <summary>IP remoto (atrás de proxy, exige ForwardedHeaders configurado no host).</summary>
    public IPAddress? RemoteIp => HttpContext.Connection.RemoteIpAddress;

    /// <summary>Caminho requisitado.</summary>
    public PathString Path => HttpContext.Request.Path;
}
