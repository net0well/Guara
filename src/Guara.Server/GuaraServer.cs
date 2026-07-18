using Guara.Abstractions;
using Guara.Dispatcher;
using Guara.Scheduler;
using Guara.Storage;
using Guara.Worker;
using Microsoft.Extensions.Logging;

namespace Guara.Server;

/// <summary>
/// Implementação default de <see cref="IGuaraServer"/>: anuncia o nó no storage,
/// inicia Worker e Dispatcher, mantém o heartbeat (reanunciando-se se o registro
/// sumir), promove ocorrências de recorrentes vencidos e roda a manutenção
/// periódica — os laços coordenados rodam sob lock, então em múltiplos nós apenas
/// um executa cada ciclo.
/// </summary>
public sealed class GuaraServer : IGuaraServer
{
    private const string MaintenanceLockKey = "guara:maintenance";
    private const string RecurringLockKey = "guara:recurring";
    private const string RecurringMetadataKey = "guara-recorrente";

    private readonly IStorage _storage;
    private readonly IDispatcher _dispatcher;
    private readonly IWorker _worker;
    private readonly IGuaraClient _client;
    private readonly RecurrenceCalculator _calculator;
    private readonly ServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GuaraServer> _logger;
    private readonly ServerNode _node;

    private CancellationTokenSource? _loopsCts;
    private Task[] _loops = [];

    /// <summary>Cria o servidor com a identidade do nó derivada da máquina e do processo.</summary>
    /// <param name="storage">Storage (registro de nós, recorrentes, purga e locks).</param>
    /// <param name="dispatcher">Motor de busca de jobs.</param>
    /// <param name="worker">Motor de capacidade/execução.</param>
    /// <param name="client">Enfileiramento das ocorrências promovidas.</param>
    /// <param name="calculator">Cálculo do próximo disparo dos recorrentes.</param>
    /// <param name="options">Opções do servidor.</param>
    /// <param name="dispatcherOptions">Filas consumidas (exibidas na identidade do nó).</param>
    /// <param name="workerOptions">Concorrência máxima (exibida na identidade do nó).</param>
    /// <param name="time">Relógio.</param>
    /// <param name="logger">Logger estruturado.</param>
    public GuaraServer(
        IStorage storage,
        IDispatcher dispatcher,
        IWorker worker,
        IGuaraClient client,
        RecurrenceCalculator calculator,
        ServerOptions options,
        DispatcherOptions dispatcherOptions,
        WorkerOptions workerOptions,
        TimeProvider time,
        ILogger<GuaraServer> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcherOptions);
        ArgumentNullException.ThrowIfNull(workerOptions);

        _storage = storage;
        _dispatcher = dispatcher;
        _worker = worker;
        _client = client;
        _calculator = calculator;
        _options = options;
        _time = time;
        _logger = logger;

        var now = time.GetUtcNow();
        _node = new ServerNode
        {
            Id = $"{Environment.MachineName.ToLowerInvariant()}:{Environment.ProcessId}:{Guid.NewGuid().ToString("n")[..8]}",
            MachineName = Environment.MachineName,
            StartedAt = now,
            LastHeartbeat = now,
            Queues = dispatcherOptions.Queues,
            MaxConcurrency = workerOptions.MaxConcurrency,
        };
    }

    /// <inheritdoc />
    public async ValueTask StartAsync(CancellationToken ct)
    {
        if (_loops.Length > 0)
        {
            return; // idempotente
        }

        await _storage.Servers.AnnounceAsync(_node with { LastHeartbeat = _time.GetUtcNow() }, ct);

        // O worker inicia antes do dispatcher para já haver consumidores quando a busca começar.
        await _worker.StartAsync(ct);
        await _dispatcher.StartAsync(ct);

        _loopsCts = new CancellationTokenSource();
        var token = _loopsCts.Token;
        _loops =
        [
            Task.Run(() => HeartbeatLoopAsync(token), CancellationToken.None),
            Task.Run(() => RecurringLoopAsync(token), CancellationToken.None),
            Task.Run(() => MaintenanceLoopAsync(token), CancellationToken.None),
        ];

        _logger.LogInformation(
            "Servidor Guará iniciado: {ServerId} (filas: {Queues}, concorrência: {MaxConcurrency})",
            _node.Id, string.Join(",", _node.Queues), _node.MaxConcurrency);
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken ct)
    {
        if (_loopsCts is null)
        {
            return;
        }

        // Primeiro para de buscar; depois drena os em execução.
        await _dispatcher.StopAsync(ct);
        await _worker.StopAsync(ct);

        await _loopsCts.CancelAsync();
        try
        {
            await Task.WhenAll(_loops).WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }

        // Desregistro best-effort: se o storage estiver fora, a manutenção de outro nó limpa depois.
        try
        {
            await _storage.Servers.RemoveAsync(_node.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao remover o registro do servidor {ServerId} no desligamento", _node.Id);
        }

        _logger.LogInformation("Servidor Guará parado: {ServerId}", _node.Id);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, _time, ct);

                var now = _time.GetUtcNow();
                if (!await _storage.Servers.HeartbeatAsync(_node.Id, now, ct))
                {
                    // O registro sumiu (removido por manutenção após indisponibilidade): reanuncia.
                    _logger.LogWarning("Registro do servidor {ServerId} não encontrado; reanunciando", _node.Id);
                    await _storage.Servers.AnnounceAsync(_node with { LastHeartbeat = now }, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Storage indisponível: mantém o laço vivo e tenta no próximo intervalo.
                _logger.LogWarning(ex, "Falha ao enviar heartbeat do servidor {ServerId}", _node.Id);
            }
        }
    }

    private async Task RecurringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RecurringPollInterval, _time, ct);

                // Lock com TTL de um ciclo: entre vários nós, só um promove por vez.
                await using var recurringLock = await _storage.Locks.TryAcquireAsync(
                    RecurringLockKey, _options.RecurringPollInterval, ct);
                if (recurringLock is null)
                {
                    continue;
                }

                var now = _time.GetUtcNow();
                foreach (var recurring in await _storage.Recurring.ListDueAsync(now, ct))
                {
                    await PromoteAsync(recurring, now, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha no ciclo de promoção de recorrentes");
            }
        }
    }

    private async Task PromoteAsync(RecurringJobRecord recurring, DateTimeOffset now, CancellationToken ct)
    {
        var calendar = recurring.CalendarName is null
            ? null
            : await _storage.Recurring.GetCalendarAsync(recurring.CalendarName, ct);

        if (recurring.SkipIfPreviousRunning && recurring.LastRunJobId is { } lastRunJobId)
        {
            var lastRun = await _storage.Jobs.GetAsync(lastRunJobId, ct);
            if (lastRun is not null && lastRun.State is not (JobState.Succeeded or JobState.Failed))
            {
                // Ocorrência anterior ainda viva: pula esta, registra e reagenda.
                await _storage.Recurring.UpsertAsync(recurring with
                {
                    LastSkippedAt = now,
                    NextRunAt = _calculator.GetNextOccurrence(recurring, calendar, now),
                }, ct);
                _logger.LogInformation(
                    "Ocorrência do recorrente {RecurringId} pulada: a anterior ainda está em execução",
                    recurring.Id);
                return;
            }
        }

        // N disparos perdidos viram UMA compensação: enfileira agora e reagenda a
        // partir de agora — nunca backfill, nunca pular sem executar.
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RecurringMetadataKey] = recurring.Id,
        };
        if (recurring.Descriptor.Metadata is { } original)
        {
            foreach (var pair in original)
            {
                metadata.TryAdd(pair.Key, pair.Value);
            }
        }

        var occurrence = recurring.Descriptor with { Queue = recurring.Queue, Metadata = metadata };
        var jobId = await _client.EnfileirarAsync(occurrence, ct);

        var next = _calculator.GetNextOccurrence(recurring, calendar, now);
        await _storage.Recurring.UpsertAsync(recurring with
        {
            LastRunAt = now,
            LastRunJobId = jobId,
            NextRunAt = next,
        }, ct);

        if (next is null)
        {
            _logger.LogWarning(
                "Recorrente {RecurringId} ficou sem próxima ocorrência (vigência encerrada ou calendário exclui tudo)",
                recurring.Id);
        }
    }

    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.MaintenanceInterval, _time, ct);

                // Lock com TTL de um ciclo: entre vários nós, só um executa a manutenção por vez.
                await using var maintenanceLock = await _storage.Locks.TryAcquireAsync(
                    MaintenanceLockKey, _options.MaintenanceInterval, ct);
                if (maintenanceLock is null)
                {
                    continue;
                }

                var now = _time.GetUtcNow();

                var deadServers = await _storage.Servers.RemoveExpiredAsync(now - _options.ServerTimeout, ct);
                var purgedSucceeded = await _storage.Jobs.PurgeAsync(
                    JobState.Succeeded, now - _options.Retention.Succeeded, ct);
                var purgedFailed = await _storage.Jobs.PurgeAsync(
                    JobState.Failed, now - _options.Retention.Failed, ct);

                if (deadServers > 0 || purgedSucceeded > 0 || purgedFailed > 0)
                {
                    _logger.LogInformation(
                        "Manutenção: {DeadServers} servidores mortos removidos, {PurgedSucceeded} jobs concluídos e {PurgedFailed} falhos purgados",
                        deadServers, purgedSucceeded, purgedFailed);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha no ciclo de manutenção");
            }
        }
    }
}
