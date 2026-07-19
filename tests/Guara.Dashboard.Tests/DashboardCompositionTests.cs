using System.Net;
using System.Security.Claims;
using Guara.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Dashboard.Tests;

public sealed class DashboardCompositionTests : IAsyncDisposable
{
    private WebApplication? _app;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task<HttpClient> StartAsync(
        Action<DashboardOptions>? configure = null,
        ClaimsPrincipal? hostUser = null,
        IPAddress? remoteIp = null,
        Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        services?.Invoke(builder.Services);
        builder.Services.AddGuara(options => options.ApplicationName = "dash-teste")
            .UseMemoryStorage()
            .AddGuaraScheduler()
            .AddGuaraDashboard(configure);

        _app = builder.Build();
        _app.Use(next => context =>
        {
            // Simula identidade do host e IP remoto (o TestServer não os define).
            if (hostUser is not null)
            {
                context.User = hostUser;
            }

            if (remoteIp is not null)
            {
                context.Connection.RemoteIpAddress = remoteIp;
            }

            return next(context);
        });
        _app.MapGuaraDashboard();
        await _app.StartAsync(Ct);

        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");
        return client;
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Teste", nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));

    [Fact]
    public async Task Composition_MountsApiAndUi_UnderBasePath()
    {
        var client = await StartAsync(dash => dash.RequireAuthorization = false);

        var stats = await client.GetAsync("/guara/api/v1/stats", Ct);
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);

        var ui = await client.GetAsync("/guara", Ct);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("Guará", await ui.Content.ReadAsStringAsync(Ct));

        var logo = await client.GetAsync("/guara/assets/logo.png", Ct);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task CustomBasePath_MovesEverything()
    {
        var client = await StartAsync(dash =>
        {
            dash.BasePath = "/painel";
            dash.RequireAuthorization = false;
        });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/painel/api/v1/stats", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    [Fact]
    public async Task SecureByDefault_WithoutRules_RequiresHostAuthentication()
    {
        var client = await StartAsync();

        var anonymous = await client.GetAsync("/guara/api/v1/stats", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task HostAuthenticatedUser_PassesDefaultGate()
    {
        var client = await StartAsync(hostUser: Principal(new Claim(ClaimTypes.Name, "ana")));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    [Fact]
    public async Task ExigirPapel_Returns403_WithoutRole_AndPassesWithIt()
    {
        var semPapel = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth.ExigirPapel("Admin")),
            hostUser: Principal(new Claim(ClaimTypes.Name, "ana")));
        Assert.Equal(HttpStatusCode.Forbidden, (await semPapel.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
        await DisposeAsync();

        var comPapel = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth.ExigirPapel("Admin")),
            hostUser: Principal(new Claim(ClaimTypes.Name, "ana"), new Claim(ClaimTypes.Role, "Admin")));
        Assert.Equal(HttpStatusCode.OK, (await comPapel.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    [Fact]
    public async Task QualquerUma_PassesWhenOneAlternativeMatches()
    {
        var client = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth
                .PermitirApenasLogados()
                .QualquerUma(grupo => grupo.ExigirPapel("Admin").ExigirClaim("guara", "admin"))),
            hostUser: Principal(new Claim(ClaimTypes.Name, "ana"), new Claim("guara", "admin")));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    [Fact]
    public async Task PermitirIps_BlocksOutsiders_EvenAuthenticated()
    {
        var client = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth
                .PermitirApenasLogados()
                .PermitirIps("10.0.0.0/8")),
            hostUser: Principal(new Claim(ClaimTypes.Name, "ana")),
            remoteIp: IPAddress.Parse("200.10.20.30"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    [Fact]
    public async Task PermitirApenasIpsInternos_AllowsPrivateRange()
    {
        var client = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth
                .PermitirApenasLogados()
                .PermitirApenasIpsInternos()),
            hostUser: Principal(new Claim(ClaimTypes.Name, "ana")),
            remoteIp: IPAddress.Parse("192.168.1.10"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    private sealed class RegraQueLanca : IDashboardAccessRule
    {
        public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
            => throw new InvalidOperationException("quebrou");
    }

    [Fact]
    public async Task CustomRuleThrowing_IsDenied_FailSafe()
    {
        var client = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth.ComRegra<RegraQueLanca>()),
            hostUser: Principal(new Claim(ClaimTypes.Name, "ana")));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }

    private sealed class RegraDeCaminho : IDashboardAccessRule
    {
        public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
            => ValueTask.FromResult(contexto.HttpContext.Request.Path.StartsWithSegments("/guara"));
    }

    [Fact]
    public async Task CustomRule_ReceivesFullHttpContext()
    {
        var client = await StartAsync(
            dash => dash.UseGuaraAuthentication(auth => auth.ComRegra(new RegraDeCaminho())));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/guara/api/v1/stats", Ct)).StatusCode);
    }
}
