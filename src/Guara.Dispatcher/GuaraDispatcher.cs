using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Dispatcher;

/// <summary>
/// Implementação default de <see cref="IDispatcher"/>: laço de polling que
/// adquire jobs elegíveis (aquisição atômica com lease — a fonte da verdade é o storage)
/// e emite <see cref="WorkerRequested"/>. O backpressure vem do canal limitado do worker:
/// publicar aguarda vaga, então o dispatcher nunca busca além da capacidade.
/// Falha de storage não derruba o laço — back-off exponencial e retomada.
/// </summary>
internal sealed class GuaraDispatcher(
    IStorage storage,
    IEventPublisher events,
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
        if (_cts is null || _loop is null)
        {
            return;
        }

        await _cts.CancelAsync();
        try
        {
            await _loop.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
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
                await DelayAsync(options.PollingInterval, ct); // fila vazia: dorme até o próximo ciclo, sem busy-loop
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
                var job = await storage.Jobs.AcquireNextDueAsync(
                    queue, options.LeaseDuration, time.GetUtcNow(), ct);
                if (job is null)
                {
                    break;
                }

                dispatched = true;
                // Backpressure: se o canal do worker está cheio, aguarda aqui.
                await events.PublishAsync(new WorkerRequested(job.Id, time.GetUtcNow()), ct);
            }
        }

        return dispatched;
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
