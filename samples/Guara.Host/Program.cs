using Guara.Core;
using Guara.Host;
using Microsoft.Extensions.DependencyInjection;

// App de exemplo do Guará: um único processo que executa os jobs (servidor) e serve o
// dashboard em tempo real. É a referência de "como montar o Guará" ponta a ponta.
//
// Rode:  dotnet run --project samples/Guara.Host
// Abra:  http://localhost:5080/guara   (login de exemplo: admin / guara)

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

// Dashboard em /guara com login fixo de exemplo (troque a senha por env/secret em produção).
guara.AddGuaraDashboard(dash => dash
    .UseGuaraAuthentication(auth => auth.ComLoginFixo(
        builder.Configuration["Guara:Dashboard:User"] ?? "admin",
        builder.Configuration["Guara:Dashboard:Password"] ?? "guara")));

// Serviço e semeador dos dados de demonstração (jobs variados + atividade contínua).
builder.Services.AddSingleton<DemoService>();
builder.Services.AddHostedService<DemoSeeder>();

var app = builder.Build();

app.MapGuaraDashboard();

app.Logger.LogInformation(
    "Guará de exemplo no ar — dashboard em http://localhost:5080/guara (login: admin / guara)");

app.Run("http://localhost:5080");
