using System.Reflection;
using Guara.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Builder; // extensões de mapeamento aparecem junto das do ASP.NET Core

/// <summary>Montagem do dashboard no pipeline do host.</summary>
public static class DashboardEndpointExtensions
{
    private static readonly Lazy<byte[]> Logo = new(() =>
    {
        using var stream = typeof(DashboardEndpointExtensions).Assembly
            .GetManifestResourceStream("Guara.Dashboard.Assets.logo.png")
            ?? throw new InvalidOperationException(
                "Logo do Guará não encontrada nos recursos embutidos — build do pacote Guara.Dashboard corrompido.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    });

    /// <summary>
    /// Monta o dashboard: login/logout e assets públicos, e — sob o portão de acesso —
    /// a API (<c>{base}/api/v1</c>), o stream SSE e a UI. Rota base vem de
    /// <see cref="DashboardOptions.BasePath"/> (ou do parâmetro, que vence).
    /// </summary>
    /// <param name="endpoints">Builder de rotas do host.</param>
    /// <param name="basePath">Rota base opcional (sobrepõe as opções).</param>
    /// <returns>O próprio builder, para encadeamento.</returns>
    public static IEndpointRouteBuilder MapGuaraDashboard(
        this IEndpointRouteBuilder endpoints, string? basePath = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<DashboardOptions>()
            ?? throw new InvalidOperationException(
                "Chame AddGuaraDashboard() no registro de serviços antes de MapGuaraDashboard().");
        var root = basePath ?? options.BasePath;

        // Público: página de login, logout e assets (a página de login precisa deles).
        var open = endpoints.MapGroup(root).WithTags("Guara");
        open.MapGet("/login", (HttpContext http, DashboardOptions opt) =>
        {
            var english = DashboardPages.PrefersEnglish(http.Request.Headers.AcceptLanguage);
            var html = DashboardPages.Login(
                root,
                retorno: http.Request.Query["retorno"],
                erro: http.Request.Query["erro"],
                fixedLoginEnabled: opt.Access?.FixedLogin is not null,
                english: english);
            return Results.Content(html, "text/html; charset=utf-8");
        }).WithName("GuaraLoginPage");

        open.MapPost("/login", HandleLoginAsync).WithName("GuaraLogin").DisableAntiforgery();

        open.MapGet("/logout", (HttpContext http) =>
        {
            http.Response.Cookies.Delete(DashboardSessionService.CookieName, new CookieOptions { Path = root });
            return Results.Redirect($"{root}/login");
        }).WithName("GuaraLogout");

        open.MapGet("/assets/logo.png", () =>
            Results.Bytes(Logo.Value, "image/png")).WithName("GuaraLogo");

        // Protegido: tudo passa pelo portão de acesso (regras em E, fail-safe).
        var filter = new DashboardAccessEndpointFilter(
            options,
            endpoints.ServiceProvider.GetRequiredService<DashboardSessionService>(),
            endpoints.ServiceProvider,
            endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Guara.Dashboard")
                ?? NullLogger.Instance);
        var secured = endpoints.MapGroup(root).WithTags("Guara").AddEndpointFilter(filter);

        var spa = endpoints.ServiceProvider.GetRequiredService<DashboardSpa>();
        secured.MapGuaraDashboardApi("/api/v1");
        secured.MapGet("/", (HttpContext http) => ServeUi(http, null)).WithName("GuaraUi");
        secured.MapGet("/{**caminho}", (HttpContext http, string caminho) => ServeUi(http, caminho))
            .WithName("GuaraUiFallback");

        return endpoints;

        IResult ServeUi(HttpContext http, string? caminho)
        {
            if (spa.Available)
            {
                if (caminho is { Length: > 0 } && spa.Asset(caminho) is { } bytes)
                {
                    // Assets têm hash no nome: cache longo e imutável.
                    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                    return Results.Bytes(bytes, DashboardSpa.ContentType(caminho));
                }

                // Shell e deep-links da SPA: index com o <base href> ajustado ao BasePath.
                http.Response.Headers.CacheControl = "no-cache";
                return Results.Bytes(spa.Index(root), "text/html; charset=utf-8");
            }

            var english = DashboardPages.PrefersEnglish(http.Request.Headers.AcceptLanguage);
            return Results.Content(DashboardPages.Placeholder(root, english), "text/html; charset=utf-8");
        }

        async Task<IResult> HandleLoginAsync(
            HttpContext http, DashboardOptions opt, DashboardSessionService sessions,
            LoginRateLimiter limiter, ILoggerFactory? loggerFactory)
        {
            if (opt.Access?.FixedLogin is not { } login)
            {
                return Results.NotFound();
            }

            var logger = loggerFactory?.CreateLogger("Guara.Dashboard") ?? NullLogger.Instance;
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var retorno = SafeReturn(form["retorno"], root);

            if (limiter.IsLocked(ip))
            {
                logger.LogWarning("Login do dashboard bloqueado por rate limit para {RemoteIp}", ip);
                return Results.Redirect($"{root}/login?erro=bloqueado&retorno={Uri.EscapeDataString(retorno)}");
            }

            var usuario = form["usuario"].ToString();
            var senha = form["senha"].ToString();
            if (!login.Validate(usuario, senha))
            {
                limiter.RegisterFailure(ip);
                logger.LogWarning("Tentativa de login inválida no dashboard para {RemoteIp}", ip);
                return Results.Redirect($"{root}/login?erro=credenciais&retorno={Uri.EscapeDataString(retorno)}");
            }

            limiter.RegisterSuccess(ip);
            http.Response.Cookies.Append(
                DashboardSessionService.CookieName, sessions.Issue(usuario), new CookieOptions
                {
                    Path = root,
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = http.Request.IsHttps,
                    MaxAge = opt.SessionTtl,
                });
            return Results.Redirect(retorno);
        }

        static string SafeReturn(string? retorno, string root)
            => retorno is { Length: > 0 } destino && destino.StartsWith(root, StringComparison.Ordinal)
                ? destino
                : root;
    }
}
