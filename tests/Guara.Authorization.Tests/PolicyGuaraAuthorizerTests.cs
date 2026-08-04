using System.Security.Claims;
using Guara.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guara.Authorization.Tests;

public class PolicyGuaraAuthorizerTests
{
    private const string PolicyDoTi = "SomenteTi";

    private static IGuaraAuthorizer Create(Action<GuaraAuthorizationOptions>? configure = null)
    {
        var services = new ServiceCollection();

        // O avaliador de policies exige logging; o nulo basta e evita arrastar o pacote.
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAuthorizationCore(options => options.AddPolicy(
            PolicyDoTi, policy => policy.RequireClaim("departamento", "ti")));

        var options = new GuaraAuthorizationOptions();
        configure?.Invoke(options);
        options.Validate();

        return new PolicyGuaraAuthorizer(
            services.BuildServiceProvider().GetRequiredService<IAuthorizationService>(),
            options,
            NullLogger<PolicyGuaraAuthorizer>.Instance);
    }

    private static ValueTask<bool> Pode(IGuaraAuthorizer authorizer, ClaimsPrincipal usuario, string acao)
        => authorizer.AuthorizeAsync(usuario, acao, TestContext.Current.CancellationToken);

    private static ClaimsPrincipal Anonimo() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Logado(params Claim[] claims)
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operador"), .. claims],
            "TesteDeAutenticacao",
            ClaimTypes.Name,
            ClaimTypes.Role));

    private static Claim Permissao(string acao) => new(GuaraClaimTypes.Permission, acao);

    [Fact]
    public async Task Anonimo_NuncaPassa()
    {
        var authorizer = Create();

        foreach (var acao in GuaraActions.All)
        {
            Assert.False(await Pode(authorizer, Anonimo(), acao));
        }
    }

    [Fact]
    public async Task Anonimo_NuncaPassa_MesmoComPolicyQueAsClaimsSatisfariam()
    {
        var authorizer = Create(options => options.AllowAll(PolicyDoTi));

        // Identidade sem authenticationType não é autenticada, mesmo carregando a claim.
        var semIdentidade = new ClaimsPrincipal(new ClaimsIdentity([new Claim("departamento", "ti")]));

        Assert.False(await Pode(authorizer, semIdentidade, GuaraActions.View));
    }

    [Fact]
    public async Task SemConcessao_NegaPorOmissao()
    {
        var authorizer = Create();

        foreach (var acao in GuaraActions.All)
        {
            Assert.False(await Pode(authorizer, Logado(), acao));
        }
    }

    [Fact]
    public async Task ClaimDePermissao_ConcedeSomenteAAcaoConcedida()
    {
        var authorizer = Create();
        var usuario = Logado(Permissao(GuaraActions.View));

        Assert.True(await Pode(authorizer, usuario, GuaraActions.View));
        Assert.False(await Pode(authorizer, usuario, GuaraActions.Delete));
        Assert.False(await Pode(authorizer, usuario, GuaraActions.Trigger));
        Assert.False(await Pode(authorizer, usuario, GuaraActions.Retry));
        Assert.False(await Pode(authorizer, usuario, GuaraActions.Calendars));
        Assert.False(await Pode(authorizer, usuario, GuaraActions.ViewPayload));
    }

    [Fact]
    public async Task PolicyPorAcao_DecideApenasAAcaoMapeada()
    {
        var authorizer = Create(options => options.Require(GuaraActions.Delete, PolicyDoTi));

        var doTi = Logado(new Claim("departamento", "ti"));
        var deOutraArea = Logado(new Claim("departamento", "vendas"));

        Assert.True(await Pode(authorizer, doTi, GuaraActions.Delete));
        Assert.False(await Pode(authorizer, deOutraArea, GuaraActions.Delete));

        // A policy mapeada vale só para a ação mapeada; as demais seguem no critério padrão.
        Assert.False(await Pode(authorizer, doTi, GuaraActions.View));
    }

    [Fact]
    public async Task DefaultPolicy_ValeParaAsAcoesSemMapeamento()
    {
        var authorizer = Create(options => options.DefaultPolicy = PolicyDoTi);
        var doTi = Logado(new Claim("departamento", "ti"));

        foreach (var acao in GuaraActions.All)
        {
            Assert.True(await Pode(authorizer, doTi, acao));
        }

        Assert.False(await Pode(authorizer, Logado(), GuaraActions.View));
    }

    [Fact]
    public async Task PapelDeAdministrador_ConcedeTudo()
    {
        var authorizer = Create(options => options.AdminRoles.Add("Sustentacao"));
        var usuario = Logado(new Claim(ClaimTypes.Role, "Sustentacao"));

        foreach (var acao in GuaraActions.All)
        {
            Assert.True(await Pode(authorizer, usuario, acao));
        }
    }

    [Fact]
    public async Task Cancelamento_Propaga()
    {
        var authorizer = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await authorizer.AuthorizeAsync(Logado(Permissao(GuaraActions.View)), GuaraActions.View, cts.Token));
    }
}

public class GuaraAuthorizationOptionsTests
{
    [Fact]
    public void AcaoDesconhecida_FalhaListandoAsValidas()
    {
        var options = new GuaraAuthorizationOptions();
        options.ActionPolicies["guara:apagar-tudo"] = "Admin";

        var erro = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("guara:apagar-tudo", erro.Message, StringComparison.Ordinal);
        Assert.Contains(GuaraActions.Delete, erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyVazia_Falha()
    {
        var options = new GuaraAuthorizationOptions();
        options.ActionPolicies[GuaraActions.View] = "  ";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void PapelVazio_Falha()
    {
        var options = new GuaraAuthorizationOptions();
        options.AdminRoles.Add("");

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void AllowAll_MapeiaTodasAsAcoes()
    {
        var options = new GuaraAuthorizationOptions().AllowAll("Admin");

        options.Validate();
        Assert.Equal(GuaraActions.All.Count, options.ActionPolicies.Count);
        Assert.All(GuaraActions.All, acao => Assert.Equal("Admin", options.ActionPolicies[acao]));
    }
}
