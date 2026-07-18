using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Guara.Server;

/// <summary>
/// Integra o <see cref="IGuaraServer"/> ao Generic Host: valida a configuração no boot
/// (falha cedo com mensagem acionável) e acompanha o ciclo de vida da aplicação.
/// </summary>
internal sealed class GuaraServerHostedService(IServiceProvider services) : IHostedService
{
    private IGuaraServer? _server;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IStorage>() is null)
        {
            throw new InvalidOperationException(
                "Nenhum storage configurado para o Guará. Encadeie um provider após AddGuara() — " +
                "por exemplo: services.AddGuara().UseMemoryStorage().AddGuaraServer() " +
                "ou .UsePostgreSqlStorage(connectionString).");
        }

        _server = services.GetRequiredService<IGuaraServer>();
        await _server.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            await _server.StopAsync(cancellationToken);
        }
    }
}
