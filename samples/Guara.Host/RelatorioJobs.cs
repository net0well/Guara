using Guara.Abstractions;
using Microsoft.Extensions.Logging;

namespace Guara.Host;

/// <summary>
/// Job descoberto pelo source generator: [GuaraJob] gera o registro em DI, a
/// invocação sem reflection e a factory tipada <c>RelatorioJobsGuara.GerarAsync(...)</c>.
/// </summary>
public sealed class RelatorioJobs(ILogger<RelatorioJobs> logger)
{
    [GuaraJob]
    [GuaraFila("relatorios")]
    [GuaraRetentativas(2)]
    public Task GerarAsync(int clienteId, CancellationToken ct)
    {
        logger.LogInformation(
            "Relatório do cliente {ClienteId} gerado (job tipado do source generator)", clienteId);
        return Task.CompletedTask;
    }
}
