using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Dispatcher;

/// <summary>
/// Implementação default de <see cref="IDispatcher"/>: laço que adquire jobs elegíveis
/// (aquisição atômica com lease — a fonte da verdade é o storage) e emite
/// <see cref="WorkerRequested"/>. O backpressure vem do canal limitado do worker:
/// publicar aguarda vaga, então o dispatcher nunca busca além da capacidade.
/// Falha de storage não derruba o laço — back-off exponencial e retomada.
/// <para>
/// Com a fila vazia, o laço aguarda o aviso de <see cref="IQueueSignal"/> em vez de dormir
/// um tempo fixo, e <see cref="DispatcherOptions.PollingInterval"/> passa a ser o teto
/// dessa espera. O aviso é best-effort, então o ciclo periódico continua sendo o piso:
/// jobs que se tornam elegíveis sozinhos (retentativa vencida, lease abandonado por um nó
/// que caiu) não geram aviso nenhum e aparecem no ciclo seguinte.
/// </para>
/// </summary>
internal sealed class GuaraDispatcher(
    IStorage storage,
    IEventPublisher events,
    IQueueSignal signal,
    IWorkerCapacity capacity,
    DispatcherOptions options,
    TimeProvider time,
    ILogger<GuaraDispatcher> logger) : IDispatcher
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <inheritdoc />
    public ValueTask StartAsync(CancellationToken ct)
    {
        if (_loop is not null)
        {
            return ValueTask.CompletedTask; // idempotente
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken ct)
    {
        if (_cts is not { } cts || _loop is not { } loop)
        {
            return;
        }

        await cts.CancelAsync();
        try
        {
            await loop.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // O chamador desistiu de esperar, mas o laço ainda está terminando. Limpar o
            // estado agora deixaria um StartAsync subir um segundo laço em paralelo com
            // este — dois dispatchers competindo pela mesma fila.
            return;
        }

        // Sem isto, o laço parado continuaria registrado e o StartAsync seguinte cairia na
        // guarda de idempotência: o dispatcher nunca mais voltaria a buscar job nenhum.
        cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = options.PollingInterval;

        while (!ct.IsCancellationRequested)
        {
            bool dispatched;
            try
            {
                dispatched = await DispatchPendingAsync(ct);
                backoff = options.PollingInterval; // storage saudável: zera o back-off
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Falha ao buscar jobs no storage; nova tentativa em {Backoff}", backoff);
                await DelayAsync(backoff, ct);
                var doubled = backoff * 2;
                backoff = doubled <= options.MaxBackoff ? doubled : options.MaxBackoff;
                continue;
            }

            if (!dispatched)
            {
                await WaitForWorkAsync(ct); // fila vazia: dorme até o aviso ou o teto, sem busy-loop
            }
        }
    }

    private async Task<bool> DispatchPendingAsync(CancellationToken ct)
    {
        var dispatched = false;

        // Filas em ordem de prioridade: drena a mais prioritária antes de olhar a próxima.
        foreach (var queue in options.Queues)
        {
            while (!ct.IsCancellationRequested)
            {
                // O lote é do tamanho do que cabe agora no worker. Pedir além disso não
                // acelera nada — encheria o canal e deixaria job com posse esperando slot,
                // que é justamente a janela que uma queda de nó transforma em espera pelo
                // vencimento do lease.
                var cabem = Math.Max(1, capacity.Available);
                var jobs = await storage.Jobs.AcquireNextDueAsync(
                    queue, cabem, options.LeaseDuration, time.GetUtcNow(), ct);
                if (jobs.Count == 0)
                {
                    break;
                }

                dispatched = true;
                for (var i = 0; i < jobs.Count; i++)
                {
                    // Backpressure: se o canal do worker está cheio, aguarda aqui.
                    await events.PublishAsync(new WorkerRequested(jobs[i].Id, time.GetUtcNow()), ct);
                }
            }
        }

        return dispatched;
    }

    private async Task WaitForWorkAsync(CancellationToken ct)
    {
        try
        {
            await signal.WaitAsync(options.Queues, options.PollingInterval, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // O aviso é uma otimização de latência: um transporte quebrado não pode
            // parar o dispatcher, que volta a depender apenas do ciclo periódico.
            logger.LogError(ex,
                "Falha ao aguardar aviso de trabalho; o laço segue no ciclo de {Interval}",
                options.PollingInterval);
            await DelayAsync(options.PollingInterval, ct);
        }
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, time, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}
