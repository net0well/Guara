using Guara.Abstractions;
using Guara.Dispatcher;
using Guara.Storage;
using Guara.Worker;
using Microsoft.Extensions.Logging;

namespace Guara.Server;

/// <summary>
/// Implementação default de <see cref="IGuaraServer"/>: anuncia o nó no storage,
/// inicia Worker e Dispatcher, mantém o heartbeat (reanunciando-se se o registro
/// sumir) e roda a manutenção periódica sob lock — em múltiplos nós, apenas um
/// executa a limpeza por ciclo.
/// </summary>
public sealed class GuaraServer : IGuaraServer
{
    private const string MaintenanceLockKey = "guara:maintenance";

    private readonly IStorage _storage;
    private readonly IDispatcher _dispatcher;
    private readonly IWorker _worker;
    private readonly ServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GuaraServer> _logger;
    private readonly ServerNode _node;

    private CancellationTokenSource? _loopsCts;
    private Task[] _loops = [];

    /// <summary>Cria o servidor com a identidade do nó derivada da máquina e do processo.</summary>
    /// <param name="storage">Storage (registro de nós, purga e locks).</param>
    /// <param name="dispatcher">Motor de busca de jobs.</param>
    /// <param name="worker">Motor de capacidade/execução.</param>
    /// <param name="options">Opções do servidor.</param>
    /// <param name="dispatcherOptions">Filas consumidas (exibidas na identidade do nó).</param>
    /// <param name="workerOptions">Concorrência máxima (exibida na identidade do nó).</param>
    /// <param name="time">Relógio.</param>
    /// <param name="logger">Logger estruturado.</param>
    public GuaraServer(
        IStorage storage,
        IDispatcher dispatcher,
        IWorker worker,
        ServerOptions options,
        DispatcherOptions dispatcherOptions,
        WorkerOptions workerOptions,
        TimeProvider time,
        ILogger<GuaraServer> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcherOptions);
        ArgumentNullException.ThrowIfNull(workerOptions);

        _storage = storage;
        _dispatcher = dispatcher;
        _worker = worker;
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
