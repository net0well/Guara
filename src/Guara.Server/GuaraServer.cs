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
/// periódica — os laços coordenados rodam sob liderança, então em múltiplos nós
/// apenas um executa cada ciclo.
/// <para>
/// A liderança é <b>mantida entre ciclos</b>, não retomada a cada um: quem assume um
/// papel continua com ele até parar ou perder a posse. Assumir e devolver a cada
/// ciclo funcionaria igual para a exclusão mútua, mas deixaria o papel sem dono na
/// maior parte do tempo — o registro do nó não teria o que informar, e cada ciclo
/// pagaria uma disputa de lock que a renovação já resolve de graça.
/// </para>
/// </summary>
internal sealed class GuaraServer : IGuaraServer
{
    private const string MaintenanceRole = Guara.Cluster.ClusterRoles.Maintenance;
    private const string RecurringRole = Guara.Cluster.ClusterRoles.Recurring;

    private readonly IStorage _storage;
    private readonly IDispatcher _dispatcher;
    private readonly IWorker _worker;
    private readonly IGuaraClient _client;
    private readonly RecurrenceCalculator _calculator;
    private readonly ContinuationPromoter _continuations;
    private readonly ILeaderElection _election;
    private readonly ServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GuaraServer> _logger;
    private readonly ServerNode _node;

    private readonly object _papeisPortao = new();
    private readonly SortedSet<string> _papeisDetidos = new(StringComparer.Ordinal);

    private CancellationTokenSource? _loopsCts;
    private Task[] _loops = [];

    /// <summary>Cria o servidor com a identidade do nó derivada da máquina e do processo.</summary>
    /// <param name="storage">Storage (registro de nós, recorrentes, purga e locks).</param>
    /// <param name="dispatcher">Motor de busca de jobs.</param>
    /// <param name="worker">Motor de capacidade/execução.</param>
    /// <param name="client">Enfileiramento das ocorrências promovidas.</param>
    /// <param name="calculator">Cálculo do próximo disparo dos recorrentes.</param>
    /// <param name="continuations">Varredura de recuperação das continuações.</param>
    /// <param name="election">Coordenação entre nós dos laços que não se dividem.</param>
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
        ContinuationPromoter continuations,
        ILeaderElection election,
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
        ArgumentNullException.ThrowIfNull(continuations);
        ArgumentNullException.ThrowIfNull(election);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcherOptions);
        ArgumentNullException.ThrowIfNull(workerOptions);

        _storage = storage;
        _dispatcher = dispatcher;
        _worker = worker;
        _client = client;
        _calculator = calculator;
        _continuations = continuations;
        _election = election;
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

        await AnunciarAsync(ct);

        // O worker inicia antes do dispatcher para já haver consumidores quando a busca começar.
        await _worker.StartAsync(ct);
        await _dispatcher.StartAsync(ct);

        _loopsCts = new CancellationTokenSource();
        var token = _loopsCts.Token;
        _loops =
        [
            Task.Run(() => HeartbeatLoopAsync(token), CancellationToken.None),
            Task.Run(
                () => CoordinatedLoopAsync(RecurringRole, _options.RecurringPollInterval, RecurringCycleAsync, token),
                CancellationToken.None),
            Task.Run(
                () => CoordinatedLoopAsync(MaintenanceRole, _options.MaintenanceInterval, MaintenanceCycleAsync, token),
                CancellationToken.None),
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
                    await AnunciarAsync(ct);
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

    /// <summary>
    /// Roda um trabalho que só pode acontecer em um nó por vez. Assume a liderança do papel
    /// na primeira oportunidade e a mantém, retomando a disputa apenas depois de perdê-la.
    /// </summary>
    private async Task CoordinatedLoopAsync(
        string role,
        TimeSpan interval,
        Func<CancellationToken, Task> ciclo,
        CancellationToken ct)
    {
        ILeadership? lideranca = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, _time, ct);

                    // Descarta a liderança perdida antes de disputar de novo: enquanto o objeto
                    // vive, a renovação em segundo plano continua tentando ressuscitá-la.
                    if (lideranca is { Lost.IsCancellationRequested: true })
                    {
                        await DevolverAsync(lideranca, anunciar: true);
                        lideranca = null;
                    }

                    lideranca ??= await AssumirAsync(role, ct);
                    if (lideranca is null)
                    {
                        continue; // outro nó lidera o papel
                    }

                    // Perder a liderança no meio do ciclo interrompe o trabalho: quem deixou
                    // de ser líder não pode continuar agindo como se fosse.
                    using var cicloCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lideranca.Lost);
                    await ciclo(cicloCts.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Posse perdida durante o ciclo: o trabalho restante é de quem assumir.
                    _logger.LogWarning("Liderança de {Role} perdida durante o ciclo", role);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha no ciclo do papel {Role}", role);
                }
            }
        }
        finally
        {
            if (lideranca is not null)
            {
                // No desligamento o registro do nó é removido logo em seguida: devolver o papel
                // basta, anunciar seria uma escrita a mais no caminho de saída.
                await DevolverAsync(lideranca, anunciar: false);
            }
        }
    }

    private async Task RecurringCycleAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        foreach (var recurring in await _storage.Recurring.ListDueAsync(now, ct))
        {
            await PromoteAsync(recurring, now, ct);
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
        var jobId = await _client.EnfileirarAsync(recurring.ToOccurrence(), ct);

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

    private async Task MaintenanceCycleAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        // Continuações primeiro: pendências de pais já finalizados disparam antes
        // de qualquer purga poder remover o pai.
        await _continuations.SweepAsync(ct);

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

    /// <summary>
    /// Disputa o papel e, ao vencer, registra a posse no storage para que o painel possa
    /// dizer qual nó responde por ele.
    /// </summary>
    private async ValueTask<ILeadership?> AssumirAsync(string role, CancellationToken ct)
    {
        var lideranca = await _election.TryAcquireAsync(role, ct);
        if (lideranca is null)
        {
            return null;
        }

        lock (_papeisPortao)
        {
            _papeisDetidos.Add(role);
        }

        // Anúncio best-effort: a posse do papel é o lock, não esta linha. Falhar aqui deixa o
        // painel desatualizado até o próximo reanúncio, e não pode custar a liderança.
        try
        {
            await AnunciarAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao publicar a posse do papel {Role} no registro do nó", role);
        }

        _logger.LogInformation("Nó {ServerId} assumiu o papel {Role}", _node.Id, role);
        return lideranca;
    }

    private async ValueTask DevolverAsync(ILeadership lideranca, bool anunciar)
    {
        var role = lideranca.Role;
        await lideranca.DisposeAsync();

        lock (_papeisPortao)
        {
            _papeisDetidos.Remove(role);
        }

        if (anunciar)
        {
            try
            {
                await AnunciarAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao publicar a devolução do papel {Role} no registro do nó", role);
            }
        }

        _logger.LogInformation("Nó {ServerId} devolveu o papel {Role}", _node.Id, role);
    }

    private ValueTask AnunciarAsync(CancellationToken ct)
    {
        string[] papeis;
        lock (_papeisPortao)
        {
            papeis = [.. _papeisDetidos];
        }

        return _storage.Servers.AnnounceAsync(
            _node with { LastHeartbeat = _time.GetUtcNow(), Roles = papeis }, ct);
    }
}
