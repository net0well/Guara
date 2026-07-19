using Guara.Abstractions;

namespace Guara.Host;

/// <summary>
/// Popula o Guará com dados de demonstração no boot (um recorrente, um lote inicial
/// variado e uma continuação) e mantém uma atividade leve e contínua — assim o
/// dashboard exibe estados diversos e atualiza ao vivo pelo stream SSE.
/// </summary>
public sealed class DemoSeeder(IGuaraClient jobs, ILogger<DemoSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            // Deixa o servidor subir antes de enfileirar.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            // Recorrente a cada 10s (aparece em "Recorrentes" e dispara ocorrências ao vivo).
            await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
                .ComId("resumo-diario")
                .Executa(DemoJobsGuara.ResumoDiarioAsync())
                .ACada(TimeSpan.FromSeconds(10))
                .ComDescricao("Resumo diário de exemplo"), ct);

            // Lote inicial variado: sucessos, falha definitiva, retentativas, agendado e tempo limite.
            for (var cliente = 1; cliente <= 6; cliente++)
            {
                await jobs.EnfileirarAsync(DemoJobsGuara.GerarRelatorioAsync(cliente), ct);
            }

            for (var pedido = 1; pedido <= 4; pedido++)
            {
                await jobs.EnfileirarAsync(DemoJobsGuara.ProcessarPagamentoAsync(pedido), ct);
            }

            await jobs.EnfileirarAsync(DemoJobsGuara.ConciliarLegadoAsync(), ct);   // → Failed
            await jobs.EnfileirarAsync(DemoJobsGuara.SincronizarAsync(), ct);        // → Succeeded (dentro do limite)
            await jobs.AgendarAsync(DemoJobsGuara.EnviarEmailAsync("ana@exemplo.com"), TimeSpan.FromSeconds(45), ct);

            // Continuação: o e-mail dispara quando o relatório do cliente 99 conclui.
            var relatorio = await jobs.EnfileirarAsync(DemoJobsGuara.GerarRelatorioAsync(99), ct);
            await jobs.ContinuarComAsync(relatorio, DemoJobsGuara.EnviarEmailAsync("gestor@exemplo.com"), ct: ct);

            logger.LogInformation("Dados de demonstração semeados.");

            // Atividade contínua para o painel refletir mudanças em tempo real.
            var proximoCliente = 100;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(6), ct);
                await jobs.EnfileirarAsync(DemoJobsGuara.GerarRelatorioAsync(proximoCliente++), ct);
                if (proximoCliente % 3 == 0)
                {
                    await jobs.EnfileirarAsync(DemoJobsGuara.ProcessarPagamentoAsync(proximoCliente), ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Desligamento normal.
        }
    }
}
