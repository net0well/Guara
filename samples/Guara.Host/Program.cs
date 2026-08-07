using System.Security.Cryptography;
using Guara.Core;
using Guara.Host;
using Guara.Host.Endpoints;
using Guara.Host.Repositories;
using Guara.Host.Services;
using Microsoft.Extensions.DependencyInjection;

// App de exemplo do Guará: uma API de pedidos que executa os jobs no próprio processo e
// serve o dashboard em tempo real. É a referência de "como montar o Guará" ponta a ponta,
// no layout que um projeto real teria — Controllers, Services, Repositories e Jobs.
//
// O fluxo que o exemplo demonstra:
//   POST /api/pedidos  →  PedidosController  →  PedidoService  →  IPedidoRepository
//                                             └→ enfileira confirmação e cobrança
//                                                (a cobrança encadeia o aviso de recusa)
//
// Rode:  dotnet run --project samples/Guara.Host
// Abra:  http://localhost:5080/guara   (a senha do login sai no log do boot)

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Content root no diretório do binário: o appsettings.json é achado mesmo com
    // `dotnet run` disparado da raiz do repositório.
    ContentRootPath = AppContext.BaseDirectory,
});

var guara = builder.Services
    .AddGuara()
    .UseConfiguration(builder.Configuration)          // opções via seção "Guara" do appsettings
    .UseGuaraDiagnostics()                             // logs estruturados + métricas + tracing
    .AddGuaraJobs()                                    // jobs [GuaraJob] deste assembly (gerados em compilação)
    .AddGuaraExecutor(retry =>
        retry.Backoff = static tentativa => TimeSpan.FromSeconds(2 + tentativa)); // back-off curto p/ ver as retentativas

// Storage: PostgreSQL quando a connection string está preenchida; memória caso contrário.
var postgres = builder.Configuration["Guara:Storage:PostgreSql:ConnectionString"];
if (string.IsNullOrWhiteSpace(postgres))
{
    guara.UseMemoryStorage();
}
else
{
    guara.UsePostgreSqlStorage();
}

guara.AddGuaraServer();

// Dashboard em /guara com login fixo. A senha nunca fica no repositório: vem de
// user-secrets ou variável de ambiente. Sem nenhuma das duas, o exemplo sorteia uma
// senha por execução — continua "clone e rode", mas sem credencial fixa versionada.
//   dotnet user-secrets --project samples/Guara.Host set "Guara:Dashboard:Password" "<senha>"
var dashboardUser = builder.Configuration["Guara:Dashboard:User"] is { Length: > 0 } usuario
    ? usuario
    : "admin";
var senhaConfigurada = builder.Configuration["Guara:Dashboard:Password"];
var dashboardPassword = string.IsNullOrWhiteSpace(senhaConfigurada)
    // Alfabeto sem caracteres ambíguos (0/O, 1/l/I) porque a senha é lida do console.
    ? RandomNumberGenerator.GetString("abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789", 16)
    : senhaConfigurada;

guara.AddGuaraDashboard(dash => dash
    .UseGuaraAuthentication(auth => auth.ComLoginFixo(dashboardUser, dashboardPassword)));

// A aplicação de exemplo. As classes de job NÃO são registradas aqui: AddGuaraJobs() já
// registrou cada uma a partir do [GuaraJob], com o código gerado em compilação.
builder.Services.AddSingleton<IPedidoRepository, PedidoRepository>();
builder.Services.AddScoped<PedidoService>();

// Serialização dos contratos HTTP pelo contexto gerado, sem reflection: é o que permite ao
// exemplo publicar em Native AOT como o resto do Guará.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, PedidosJsonContext.Default));

// Só para a demonstração ter movimento sem ninguém chamar a API na mão.
builder.Services.AddHostedService<GeradorDeTrafego>();

var app = builder.Build();

app.MapPedidos();
app.MapGuaraDashboard();

app.Logger.LogInformation(
    "Guará de exemplo no ar — dashboard em http://localhost:5080/guara (usuário: {Usuario})",
    dashboardUser);

if (string.IsNullOrWhiteSpace(senhaConfigurada))
{
    // Sem senha configurada não há como entrar sem imprimir a gerada. Ela vale só para
    // este processo: reiniciar sorteia outra.
    app.Logger.LogWarning(
        "Senha do dashboard não configurada; gerada para esta execução: {Senha}. "
            + "Fixe com: dotnet user-secrets --project samples/Guara.Host set \"Guara:Dashboard:Password\" \"<senha>\"",
        dashboardPassword);
}

app.Run("http://localhost:5080");
