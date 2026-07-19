using Guara.Abstractions;
using Guara.Core;
using Guara.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Guara.Executor;

/// <summary>
/// Implementação default de <see cref="IExecutor"/>: obtém o job e seus metadados
/// declarados, aplica o gate de exclusão mútua e o tempo limite, roda o pipeline
/// (middlewares custom + invocação) e persiste o desfecho com token
/// <b>não-cancelável</b> — efeito já ocorrido nunca é revertido por cancelamento
/// tardio. A retentativa é <b>persistente</b>: falha com tentativas restantes grava
/// <c>Retrying</c> reagendado com back-off (o máximo por job vem dos metadados);
/// esgotou, grava <c>Failed</c>. <c>JobContext</c> é pooled.
/// </summary>
public sealed class GuaraExecutor : IExecutor
{
    // TTL generoso do mutex: crash do nó libera a chave por expiração; execuções mais
    // longas que isto perdem a garantia de exclusão (documentado).
    private static readonly TimeSpan MutexTtl = TimeSpan.FromMinutes(10);

    // Atraso da devolução à fila quando a chave está ocupada e o job não espera.
    private static readonly TimeSpan MutexRequeueDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan MutexPollInterval = TimeSpan.FromMilliseconds(250);

    private const string MutexKeyPrefix = "guara:mutex:";

    private readonly IStorage _storage;
    private readonly IEventPublisher _events;
    private readonly IJobMetadataProvider _metadata;
    private readonly RetryOptions _retryOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<GuaraExecutor> _logger;
    private readonly ObjectPool<JobContext> _contextPool;
    private readonly JobDelegate _pipeline;

    /// <summary>Cria o executor compondo o pipeline canônico.</summary>
    /// <param name="storage">Storage de jobs e locks.</param>
    /// <param name="events">Publicador de eventos.</param>
    /// <param name="invoker">Invocador do método do job (sem reflection).</param>
    /// <param name="metadata">Metadados declarados por job (atributos, lidos em compilação).</param>
    /// <param name="retryOptions">Política de retentativa global.</param>
    /// <param name="time">Relógio (testável).</param>
    /// <param name="middlewares">Middlewares custom (slot Custom).</param>
    /// <param name="logger">Logger estruturado.</param>
    public GuaraExecutor(
        IStorage storage,
        IEventPublisher events,
        IJobInvoker invoker,
        IJobMetadataProvider metadata,
        RetryOptions retryOptions,
        TimeProvider time,
        IEnumerable<IJobMiddleware> middlewares,
        ILogger<GuaraExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(retryOptions);
        ArgumentNullException.ThrowIfNull(time);

        _storage = storage;
        _events = events;
        _metadata = metadata;
        _retryOptions = retryOptions;
        _time = time;
        _logger = logger;
        _contextPool = new DefaultObjectPool<JobContext>(new JobContextPoolPolicy());

        var builder = new JobPipelineBuilder();
        foreach (var middleware in middlewares)
        {
            builder.Use(PipelineSlot.Custom, middleware);
        }

        _pipeline = builder.Build((ctx, ct) => invoker.InvokeAsync(ctx, ct));
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobId id, CancellationToken ct)
    {
        var record = await _storage.Jobs.GetAsync(id, ct);
        if (record is null)
        {
            return; // excluído entre a aquisição e a execução — nada a fazer
        }

        var metadata = _metadata.GetMetadata(record.Descriptor.TypeName, record.Descriptor.MethodName);
        var context = _contextPool.Get();
        context.Initialize(record.Id, record.Descriptor, record.Attempt);
        context.State = JobState.Processing;

        try
        {
            ILockHandle? mutex = null;
            if (metadata?.ConcurrencyKey is { } concurrencyKey)
            {
                var key = MutexKeyPrefix + concurrencyKey(context);
                mutex = await TryAcquireMutexAsync(key, metadata.ConcurrencyWaitSeconds, ct);
                if (mutex is null)
                {
                    // Chave ocupada: devolve à fila sem consumir tentativa — o worker
                    // segue livre e o job nunca executa em dobro.
                    await _storage.Jobs.RescheduleAsync(
                        id, _time.GetUtcNow() + MutexRequeueDelay, CancellationToken.None);
                    _logger.LogInformation(
                        "Job {JobId} devolvido à fila: chave de exclusão mútua '{MutexKey}' ocupada",
                        id.Value, key);
                    return;
                }
            }

            await using var mutexHolder = mutex; // liberado após o desfecho persistido

            var timeoutSeconds = metadata?.TimeoutSeconds;
            using var timeoutCts = timeoutSeconds is > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            timeoutCts?.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds!.Value));
            var runToken = timeoutCts?.Token ?? ct;

            try
            {
                await _pipeline(context, runToken);

                if (timeoutCts is { IsCancellationRequested: true } && !ct.IsCancellationRequested)
                {
                    // O efeito já aconteceu: o estado reflete a realidade, com aviso.
                    _logger.LogWarning(
                        "Job {JobId} excedeu o tempo limite de {TimeoutSeconds}s mas completou (o token foi ignorado)",
                        id.Value, timeoutSeconds);
                }

                // Persistência do estado final com token não-cancelável: um efeito já concluído não deve reverter por cancelamento tardio.
                await _storage.Jobs.UpdateStateAsync(id, JobState.Succeeded, null, CancellationToken.None);
                await _events.PublishAsync(new JobCompleted(id, _time.GetUtcNow()), CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown ou posse perdida: estado permanece Processing e a tentativa não
                // conta; o lease expira e o job volta a ser elegível. Nunca marca Failed aqui.
            }
            catch (OperationCanceledException) when (timeoutCts is { IsCancellationRequested: true })
            {
                // Tempo limite honrado pelo job: é falha — segue a política de retentativa.
                await HandleFailureAsync(record, metadata, $"Tempo limite de {timeoutSeconds}s excedido.");
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(record, metadata, ex.Message);
            }
        }
        finally
        {
            _contextPool.Return(context);
        }
    }

    private async ValueTask HandleFailureAsync(JobRecord record, JobExecutionMetadata? metadata, string error)
    {
        var maxAttempts = metadata?.MaxAttempts ?? _retryOptions.MaxAttempts;
        if (record.Attempt < maxAttempts)
        {
            // Retentativa persistente: o reagendamento sobrevive a restart e a
            // contagem real de tentativas fica visível no storage.
            var retryAt = _time.GetUtcNow() + _retryOptions.Backoff(record.Attempt);
            await _storage.Jobs.ScheduleRetryAsync(record.Id, error, retryAt, CancellationToken.None);
            await _events.PublishAsync(
                new JobRetryScheduled(record.Id, _time.GetUtcNow(), record.Attempt + 1, retryAt),
                CancellationToken.None);
        }
        else
        {
            await _storage.Jobs.UpdateStateAsync(record.Id, JobState.Failed, error, CancellationToken.None);
            await _events.PublishAsync(new JobFailed(record.Id, _time.GetUtcNow(), error), CancellationToken.None);
        }
    }

    private async ValueTask<ILockHandle?> TryAcquireMutexAsync(string key, int waitSeconds, CancellationToken ct)
    {
        var handle = await _storage.Locks.TryAcquireAsync(key, MutexTtl, ct);
        if (handle is not null || waitSeconds <= 0)
        {
            return handle;
        }

        // Espera limitada e cooperativa pela chave antes de devolver o job à fila.
        var deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(waitSeconds);
        while (_time.GetUtcNow() < deadline)
        {
            await Task.Delay(MutexPollInterval, _time, ct);
            handle = await _storage.Locks.TryAcquireAsync(key, MutexTtl, ct);
            if (handle is not null)
            {
                return handle;
            }
        }

        return null;
    }
}
