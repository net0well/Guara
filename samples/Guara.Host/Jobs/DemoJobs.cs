using Guara.Abstractions;

namespace Guara.Host;

/// <summary>Serviço injetado nos jobs de exemplo (prova a resolução por DI).</summary>
public sealed class DemoService(ILogger<DemoService> logger)
{
    public void Registrar(string mensagem) => logger.LogInformation("[demo] {Mensagem}", mensagem);
}

/// <summary>
/// Jobs de demonstração descobertos pelo source generator ([GuaraJob]). Cada um mostra
/// um recurso: fila dedicada, retentativas por job, tempo limite, e falhas para o painel
/// exibir estados variados (Succeeded/Failed/Retrying). As factories tipadas
/// (<c>DemoJobsGuara.*</c>) são geradas em compilação — assinatura errada não compila.
/// </summary>
public sealed class DemoJobs(DemoService demo)
{
    [GuaraJob]
    [GuaraFila("relatorios")]
    public async Task GerarRelatorioAsync(int clienteId, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        demo.Registrar($"Relatório do cliente {clienteId} gerado");
    }

    [GuaraJob]
    [GuaraFila("emails")]
    public Task EnviarEmailAsync(string destino, CancellationToken ct)
    {
        demo.Registrar($"E-mail enviado para {destino}");
        return Task.CompletedTask;
    }

    [GuaraJob]
    [GuaraRetentativas(3)]
    public Task ProcessarPagamentoAsync(int pedido, CancellationToken ct)
    {
        // Falha intermitente: exercita a retentativa persistente (Retrying → Succeeded/Failed).
        if (Random.Shared.Next(2) == 0)
        {
            throw new InvalidOperationException($"Gateway instável ao cobrar o pedido {pedido}");
        }

        demo.Registrar($"Pagamento do pedido {pedido} aprovado");
        return Task.CompletedTask;
    }

    [GuaraJob]
    [GuaraRetentativas(0)]
    public Task ConciliarLegadoAsync(CancellationToken ct)
        => throw new InvalidOperationException("Sistema legado indisponível (falha de demonstração, sem retentativa)");

    [GuaraJob]
    [GuaraTempoLimite(2)]
    public async Task SincronizarAsync(CancellationToken ct)
    {
        demo.Registrar("Sincronização iniciada…");
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        demo.Registrar("Sincronização concluída");
    }

    [GuaraJob]
    [GuaraFila("relatorios")]
    public Task ResumoDiarioAsync(CancellationToken ct)
    {
        demo.Registrar("Resumo diário emitido (recorrente)");
        return Task.CompletedTask;
    }
}
