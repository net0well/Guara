using Guara.Abstractions;
using Guara.Executor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddGuara(options => options.ApplicationName = "guara-host")
    .UseMemoryStorage()
    .AddGuaraServer();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Guara.Host.Demo");

host.Services.GetRequiredService<JobHandlerRegistry>()
    .Register("Demo", "Saudacao", (contexto, _) =>
    {
        logger.LogInformation("Olá do Guará! Job {JobId} executado", contexto.Id.Value);
        return ValueTask.CompletedTask;
    });

var jobs = host.Services.GetRequiredService<IGuaraClient>();
await jobs.EnfileirarAsync(new JobDescriptor("Demo", "Saudacao", default));
await jobs.AgendarAsync(new JobDescriptor("Demo", "Saudacao", default), TimeSpan.FromSeconds(5));
logger.LogInformation("Dois jobs de demonstração criados: um imediato e um agendado para daqui a 5s");

await host.RunAsync();
