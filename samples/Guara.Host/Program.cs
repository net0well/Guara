using Guara.Abstractions;
using Guara.Executor;
using Guara.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Content root fixo no diretório do binário: o appsettings.json do sample é achado
// mesmo com `dotnet run` disparado da raiz do repositório.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// As opções (filas, polling, retenção...) vêm da seção "Guara" do appsettings;
// storage: PostgreSQL quando a connection string está preenchida, memória caso contrário.
var guara = builder.Services
    .AddGuara()
    .UseConfiguration(builder.Configuration)
    .UseGuaraDiagnostics()
    .AddGuaraJobs(); // jobs [GuaraJob] deste assembly (gerado em compilação)

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

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Guara.Host.Demo");

var registry = host.Services.GetRequiredService<JobHandlerRegistry>();
registry.Register("Demo", "Saudacao", (contexto, _) =>
{
    logger.LogInformation("Olá do Guará! Job {JobId} executado", contexto.Id.Value);
    return ValueTask.CompletedTask;
});
registry.Register("Demo", "Despedida", (contexto, _) =>
{
    logger.LogInformation("Até logo! Continuação {JobId} executada depois do pai", contexto.Id.Value);
    return ValueTask.CompletedTask;
});

var jobs = host.Services.GetRequiredService<IGuaraClient>();
var saudacaoId = await jobs.EnfileirarAsync(new JobDescriptor("Demo", "Saudacao", default));
await jobs.ContinuarComAsync(saudacaoId, new JobDescriptor("Demo", "Despedida", default));
await jobs.AgendarAsync(new JobDescriptor("Demo", "Saudacao", default), TimeSpan.FromSeconds(5));
await jobs.EnfileirarAsync(RelatorioJobsGuara.GerarAsync(42)); // descritor tipado gerado
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("saudacao-recorrente")
    .Executa(new JobDescriptor("Demo", "Saudacao", default))
    .ACada(TimeSpan.FromSeconds(15))
    .ComDescricao("Saudação recorrente de demonstração"));
logger.LogInformation(
    "Jobs de demonstração criados: um imediato com continuação, um agendado para daqui a 5s e um recorrente a cada 15s");

await host.RunAsync();
