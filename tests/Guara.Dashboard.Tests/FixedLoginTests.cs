using System.Net;
using Guara.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Dashboard.Tests;

public sealed class FixedLoginTests : IAsyncDisposable
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

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGuara(options => options.ApplicationName = "login-teste")
            .UseMemoryStorage()
            .AddGuaraScheduler()
            .AddGuaraDashboard(dash => dash.UseGuaraAuthentication(auth => auth
                .ComLoginFixo("guara_admin", "s3nh4-forte!")));

        _app = builder.Build();
        _app.MapGuaraDashboard();
        await _app.StartAsync(Ct);

        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");
        return client;
    }

    private static HttpRequestMessage LoginPost(string usuario, string senha) => new(
        HttpMethod.Post, "/guara/login")
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = usuario,
            ["senha"] = senha,
            ["retorno"] = "/guara",
        }),
    };

    [Fact]
    public async Task AnonymousBrowser_IsRedirectedToLoginPage_WithLogoAndForm()
    {
        var client = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/guara");
        request.Headers.Add("Accept", "text/html");
        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/guara/login", response.Headers.Location!.ToString());

        var page = await client.GetStringAsync(response.Headers.Location, Ct);
        Assert.Contains("assets/logo.png", page);       // identidade do Guará
        Assert.Contains("name=\"senha\"", page);        // formulário
        Assert.Contains("Entrar", page);                // pt-BR
    }

    [Fact]
    public async Task LoginPage_SpeaksEnglish_WhenPreferred()
    {
        var client = await StartAsync();
        client.DefaultRequestHeaders.Remove("Accept-Language");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

        var page = await client.GetStringAsync("/guara/login", Ct);

        Assert.Contains("Sign in", page);
    }

    [Fact]
    public async Task ValidCredentials_IssueSessionCookie_AndGrantAccess()
    {
        var client = await StartAsync();

        var login = await client.SendAsync(LoginPost("guara_admin", "s3nh4-forte!"), Ct);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/guara", login.Headers.Location!.ToString());
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("guara.dashboard=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        var cookieValue = cookie.Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Get, "/guara/api/v1/stats");
        request.Headers.Add("Cookie", cookieValue);
        var stats = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
    }

    [Fact]
    public async Task WrongPassword_RedirectsWithError_AndLockoutAfterRepeats()
    {
        var client = await StartAsync();

        var wrong = await client.SendAsync(LoginPost("guara_admin", "errada"), Ct);
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.Contains("erro=credenciais", wrong.Headers.Location!.ToString());

        // Rate limit: falhas repetidas bloqueiam até quem sabe a senha.
        for (var i = 0; i < 4; i++)
        {
            await client.SendAsync(LoginPost("guara_admin", "errada"), Ct);
        }

        var locked = await client.SendAsync(LoginPost("guara_admin", "s3nh4-forte!"), Ct);
        Assert.Contains("erro=bloqueado", locked.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = await StartAsync();
        var login = await client.SendAsync(LoginPost("guara_admin", "s3nh4-forte!"), Ct);
        var cookieValue = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var logout = new HttpRequestMessage(HttpMethod.Get, "/guara/logout");
        logout.Headers.Add("Cookie", cookieValue);
        var response = await client.SendAsync(logout, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var expired = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("guara.dashboard=;", expired); // cookie derrubado
    }

    [Fact]
    public async Task ApiWithoutSession_Gets401Json_NotRedirect()
    {
        var client = await StartAsync();

        var response = await client.GetAsync("/guara/api/v1/stats", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task OpenRedirect_IsNeutralized()
    {
        var client = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/guara/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["usuario"] = "guara_admin",
                ["senha"] = "s3nh4-forte!",
                ["retorno"] = "https://malicioso.example",
            }),
        };
        var response = await client.SendAsync(request, Ct);

        Assert.Equal("/guara", response.Headers.Location!.ToString()); // destino externo ignorado
    }
}
