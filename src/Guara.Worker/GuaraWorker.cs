using System.Threading.Channels;
using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Worker;

/// <summary>
/// Implementação default de <see cref="IWorker"/>: consome
/// <see cref="WorkerRequested"/> num canal limitado (backpressure natural até o
/// Dispatcher), executa em N slots concorrentes, renova a posse durante a
/// execução (posse perdida → aborta local, evitando execução dupla) e faz drain
/// gracioso no shutdown.
/// </summary>
internal sealed class GuaraWorker : IWorker, IEventHandler<WorkerRequested>
{
    private readonly IStorage _storage;
    private readonly IExecutor _executor;
    private readonly IEventPublisher _events;
    private readonly WorkerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GuaraWorker> _logger;
    private readonly Channel<JobId> _channel;

    private CancellationTokenSource? _acceptCts;
    private CancellationTokenSource? _executionCts;
    private Task[] _slots = [];

    /// <summary>Cria o worker.</summary>
    /// <param name="storage">Storage (renovação de lease).</param>
    /// <param name="executor">Executor de jobs.</param>
    /// <param name="events">Publicador de eventos (<see cref="ExecutorStarted"/>).</param>
    /// <param name="options">Opções de capacidade/lease/drain.</param>
    /// <param name="time">Relógio (testável).</param>
    /// <param name="logger">Logger estruturado.</param>
    public GuaraWorker(
        IStorage storage,
        IExecutor executor,
        IEventPublisher events,
        WorkerOptions options,
        TimeProvider time,
        ILogger<GuaraWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrency, 1);

        _storage = storage;
        _executor = executor;
        _events = events;
        _options = options;
        _time = time;
        _logger = logger;

        _channel = Channel.CreateBounded<JobId>(new BoundedChannelOptions(options.MaxConcurrency * 2)
        {
            FullMode = BoundedChannelFullMode.Wait, // cheio → o publisher (Dispatcher) aguarda
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public async ValueTask HandleAsync(WorkerRequested @event, CancellationToken ct)
        => await _channel.Writer.WriteAsync(@event.Id, ct); // backpressure natural

    /// <inheritdoc />
    public ValueTask StartAsync(CancellationToken ct)
    {
        if (_slots.Length > 0)
        {
            return ValueTask.CompletedTask; // idempotente
        }

        _acceptCts = new CancellationTokenSource();
        _executionCts = new CancellationTokenSource();

        var acceptToken = _acceptCts.Token;
        var executionToken = _executionCts.Token;
        _slots = [.. Enumerable.Range(0, _options.MaxConcurrency)
            .Select(_ => Task.Run(() => SlotAsync(acceptToken, executionToken), CancellationToken.None))];

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken ct)
    {
        if (_acceptCts is not { } acceptCts || _executionCts is not { } executionCts)
        {
            return;
        }

        // 1) para de aceitar novos; jobs na fila interna não iniciados são descartados
        //    (a posse expira e eles voltam a ser elegíveis — nada se perde).
        await acceptCts.CancelAsync();

        // 2) aguarda os em andamento até o timeout de drain…
        var drain = Task.WhenAll(_slots);
        var timeout = Task.Delay(_options.ShutdownDrainTimeout, _time, CancellationToken.None);
        if (await Task.WhenAny(drain, timeout) == timeout)
        {
            // 3) …e cancela cooperativamente os excedentes.
            await executionCts.CancelAsync();
        }

        try
        {
            await drain.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // O chamador desistiu de esperar, mas os slots ainda estão terminando. Limpar
            // o estado agora deixaria um StartAsync subir um segundo conjunto de slots em
            // paralelo com este, dobrando a concorrência configurada.
            return;
        }

        // Sem isto, os slots parados continuariam registrados e o StartAsync seguinte
        // cairia na guarda de idempotência: o worker nunca mais executaria job nenhum.
        _slots = [];
        acceptCts.Dispose();
        executionCts.Dispose();
        _acceptCts = null;
        _executionCts = null;
    }

    private async Task SlotAsync(CancellationToken acceptCt, CancellationToken executionCt)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(acceptCt))
            {
                while (_channel.Reader.TryRead(out var id))
                {
                    await ProcessAsync(id, executionCt);
                    if (acceptCt.IsCancellationRequested)
                    {
                        return; // drain: termina o job atual e não pega mais nenhum
                    }
                }
            }
        }
        catch (OperationCanceledException) when (acceptCt.IsCancellationRequested)
        {
            // shutdown normal
        }
    }

    private async Task ProcessAsync(JobId id, CancellationToken executionCt)
    {
        // jobCts: cancela a execução se a posse for perdida ou no timeout do drain.
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(executionCt);
        using var renewalCts = new CancellationTokenSource();
        var renewal = RenewLeaseLoopAsync(id, jobCts, renewalCts.Token);

        try
        {
            await _events.PublishAsync(new ExecutorStarted(id, _time.GetUtcNow()), CancellationToken.None);
            await _executor.ExecuteAsync(id, jobCts.Token);
        }
        catch (OperationCanceledException)
        {
            // shutdown ou posse perdida: o lease cobre o re-processamento
        }
        catch (Exception ex)
        {
            // o Executor trata falhas do job; chegar aqui é erro de infraestrutura
            _logger.LogError(ex, "Erro de infraestrutura ao executar o job {JobId}", id.Value);
        }
        finally
        {
            await renewalCts.CancelAsync();
            await renewal;
        }
    }

    private async Task RenewLeaseLoopAsync(JobId id, CancellationTokenSource jobCts, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.LeaseRenewInterval, _time, ct);
                if (!await _storage.Jobs.RenewLeaseAsync(id, _options.LeaseDuration, ct))
                {
                    // Posse perdida (outro nó pode assumir): aborta a execução local
                    // para nunca processar o job em dobro.
                    _logger.LogWarning(
                        "Posse do job {JobId} perdida; abortando a execução local", id.Value);
                    await jobCts.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // fim normal da execução
        }
    }
}

/// <summary>
/// Encaminha <see cref="WorkerRequested"/> ao <see cref="GuaraWorker"/> singleton.
/// Ter um tipo concreto próprio permite registro idempotente por tipo de implementação:
/// múltiplas chamadas de configuração nunca duplicam o handler (o que faria cada job
/// entrar duas vezes na fila interna).
/// </summary>
internal sealed class WorkerRequestedForwarder(GuaraWorker worker) : IEventHandler<WorkerRequested>
{
    public ValueTask HandleAsync(WorkerRequested @event, CancellationToken ct)
        => worker.HandleAsync(@event, ct);
}
