using Guara.Abstractions;
using Guara.Core;
using Guara.Storage;
using Microsoft.Extensions.ObjectPool;

namespace Guara.Executor;

/// <summary>
/// Implementação default de <see cref="IExecutor"/>: obtém o job, roda o
/// pipeline (middlewares custom + invocação) e persiste o desfecho com token
/// <b>não-cancelável</b> — efeito já ocorrido nunca é revertido por cancelamento
/// tardio. A retentativa é <b>persistente</b>: falha com tentativas restantes grava
/// <c>Retrying</c> reagendado com back-off (sobrevive a restart do nó); esgotou,
/// grava <c>Failed</c>. <c>JobContext</c> é pooled (alocação amortizada ~zero).
/// </summary>
public sealed class GuaraExecutor : IExecutor
{
    private readonly IStorage _storage;
    private readonly IEventPublisher _events;
    private readonly RetryOptions _retryOptions;
    private readonly TimeProvider _time;
    private readonly ObjectPool<JobContext> _contextPool;
    private readonly JobDelegate _pipeline;

    /// <summary>Cria o executor compondo o pipeline canônico.</summary>
    /// <param name="storage">Storage de jobs.</param>
    /// <param name="events">Publicador de eventos.</param>
    /// <param name="invoker">Invocador do método do job (sem reflection).</param>
    /// <param name="retryOptions">Política de retentativa persistente.</param>
    /// <param name="time">Relógio (testável).</param>
    /// <param name="middlewares">Middlewares custom (slot Custom).</param>
    public GuaraExecutor(
        IStorage storage,
        IEventPublisher events,
        IJobInvoker invoker,
        RetryOptions retryOptions,
        TimeProvider time,
        IEnumerable<IJobMiddleware> middlewares)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(retryOptions);
        ArgumentNullException.ThrowIfNull(time);

        _storage = storage;
        _events = events;
        _retryOptions = retryOptions;
        _time = time;
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

        var context = _contextPool.Get();
        context.Initialize(record.Id, record.Descriptor, record.Attempt);
        context.State = JobState.Processing;

        try
        {
            await _pipeline(context, ct);

            // Persistência do estado final com token não-cancelável: um efeito já concluído não deve reverter por cancelamento tardio.
            await _storage.Jobs.UpdateStateAsync(id, JobState.Succeeded, null, CancellationToken.None);
            await _events.PublishAsync(new JobCompleted(id, _time.GetUtcNow()), CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown ou posse perdida: estado permanece Processing e a tentativa não
            // conta; o lease expira e o job volta a ser elegível. Nunca marca Failed aqui.
        }
        catch (Exception ex)
        {
            if (record.Attempt < _retryOptions.MaxAttempts)
            {
                // Retentativa persistente: o reagendamento sobrevive a restart e a
                // contagem real de tentativas fica visível no storage.
                var retryAt = _time.GetUtcNow() + _retryOptions.Backoff(record.Attempt);
                await _storage.Jobs.ScheduleRetryAsync(id, ex.Message, retryAt, CancellationToken.None);
                await _events.PublishAsync(
                    new JobRetryScheduled(id, _time.GetUtcNow(), record.Attempt + 1, retryAt), CancellationToken.None);
            }
            else
            {
                await _storage.Jobs.UpdateStateAsync(id, JobState.Failed, ex.Message, CancellationToken.None);
                await _events.PublishAsync(new JobFailed(id, _time.GetUtcNow(), ex.Message), CancellationToken.None);
            }
        }
        finally
        {
            _contextPool.Return(context);
        }
    }
}
