using Guara.Abstractions;
using Guara.Host.Jobs;

namespace Guara.Host.Services;

/// <summary>
/// Gera movimento no exemplo para que o painel tenha o que mostrar sem ninguém precisar
/// chamar a API na mão: registra o recorrente do relatório diário e cria pedidos em
/// intervalos curtos, pelo mesmo caminho que o controller usa.
/// <para>
/// Existe só para a demonstração. Numa aplicação real, quem cria pedido é o usuário.
/// </para>
/// </summary>
public sealed class GeradorDeTrafego(
    IServiceScopeFactory escopos,
    IGuaraClient jobs,
    ILogger<GeradorDeTrafego> logger) : BackgroundService
{
    private static readonly string[] Clientes =
    [
        "ana@exemplo.com", "bruno@exemplo.com", "carla@exemplo.com", "diego@exemplo.com",
    ];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            // Deixa o servidor subir antes de enfileirar o primeiro trabalho.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            // Recorrente curto para o painel exibir ocorrências ao vivo. Num projeto real
            // isto seria "0 3 * * *" e ficaria no boot da aplicação, exatamente assim.
            await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
                .ComId("relatorio-diario")
                .Executa(RelatorioJobsGuara.ConsolidarDiarioAsync())
                .ACada(TimeSpan.FromSeconds(20))
                .ComDescricao("Consolida os pedidos do dia")
                .NaFila("relatorios"), ct);

            logger.LogInformation("Recorrente do relatório diário registrado.");

            var proximo = 0;
            while (!ct.IsCancellationRequested)
            {
                // O gerador é singleton e o serviço é scoped: cada pedido roda no próprio
                // escopo, como aconteceria numa requisição HTTP.
                await using (var escopo = escopos.CreateAsyncScope())
                {
                    var pedidos = escopo.ServiceProvider.GetRequiredService<PedidoService>();
                    await pedidos.RegistrarAsync(
                        Clientes[proximo % Clientes.Length],
                        Math.Round((decimal)(Random.Shared.NextDouble() * 400 + 20), 2),
                        ct);
                }

                proximo++;
                if (proximo % 5 == 0)
                {
                    await jobs.EnfileirarAsync(
                        RelatorioJobsGuara.ExportarDoClienteAsync(Clientes[0]), ct);
                }

                await Task.Delay(TimeSpan.FromSeconds(6), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Desligamento normal.
        }
    }
}
