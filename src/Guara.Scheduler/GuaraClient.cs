using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Scheduler;

/// <summary>
/// Implementação default de <see cref="IGuaraClient"/>: persiste jobs e definições no
/// storage ("o storage é a fila") e emite os eventos do fluxo (<see cref="JobCreated"/>,
/// <see cref="JobScheduled"/>) — nunca chama outro componente diretamente.
/// </summary>
internal sealed class GuaraClient(
    IStorage storage,
    IEventPublisher events,
    IQueueSignal signal,
    RecurrenceCalculator calculator,
    ContinuationPromoter continuations,
    TimeProvider time,
    ILogger<GuaraClient> logger) : IGuaraClient
{
    // Teto da cadeia de continuações: barra registro desgovernado (ex.: laço registrando
    // filho do filho indefinidamente) com folga ampla para fluxos reais.
    private const int MaxContinuationDepth = 100;
    /// <inheritdoc />
    public async ValueTask<JobId> EnfileirarAsync(JobDescriptor job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var now = time.GetUtcNow();
        var id = NewId();

        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = id,
            Descriptor = job,
            State = JobState.Enqueued,
            Queue = job.Queue,
            CreatedAt = now,
        }, ct);

        await events.PublishAsync(new JobCreated(id, now), ct);
        // Depois de persistir: acordar o dispatcher antes do registro existir só o faria
        // olhar uma fila que ainda não tem o job.
        await SinalizarAsync(job.Queue, ct);
        return id;
    }

    /// <inheritdoc />
    public async ValueTask<JobId> AgendarAsync(JobDescriptor job, TimeSpan atraso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfLessThan(atraso, TimeSpan.Zero);

        var now = time.GetUtcNow();
        var id = NewId();

        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = id,
            Descriptor = job,
            State = JobState.Scheduled,
            Queue = job.Queue,
            CreatedAt = now,
            ScheduledFor = now + atraso,
        }, ct);

        await events.PublishAsync(new JobCreated(id, now), ct);
        await events.PublishAsync(new JobScheduled(id, now), ct);

        // Só avisa o que já é elegível: com atraso, o job tem data futura e o aviso faria
        // o dispatcher acordar para não achar nada.
        if (atraso == TimeSpan.Zero)
        {
            await SinalizarAsync(job.Queue, ct);
        }

        return id;
    }

    /// <inheritdoc />
    public ValueTask<JobId> EnfileirarAsync(
        JobDescriptor job, IGuaraTransaction transacao, CancellationToken ct = default)
        => CriarNaTransacaoAsync(job, JobState.Enqueued, scheduledFor: null, transacao, ct);

    /// <inheritdoc />
    public ValueTask<JobId> AgendarAsync(
        JobDescriptor job, TimeSpan atraso, IGuaraTransaction transacao, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(atraso, TimeSpan.Zero);
        return CriarNaTransacaoAsync(
            job, JobState.Scheduled, time.GetUtcNow() + atraso, transacao, ct);
    }

    /// <summary>
    /// Cria o job dentro da transação do chamador. Nada observável escapa antes da
    /// confirmação dele: sem evento e sem aviso de fila, porque o Guará não enxerga esse
    /// commit e anunciaria um job que ainda não existe — ou que vai sumir no rollback.
    /// O job aparece no próximo ciclo de busca do dispatcher.
    /// </summary>
    private async ValueTask<JobId> CriarNaTransacaoAsync(
        JobDescriptor job, JobState state, DateTimeOffset? scheduledFor,
        IGuaraTransaction transacao, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(transacao);

        var id = NewId();
        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = id,
            Descriptor = job,
            State = state,
            Queue = job.Queue,
            CreatedAt = time.GetUtcNow(),
            ScheduledFor = scheduledFor,
        }, transacao, ct);

        return id;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExcluirAsync(JobId id, CancellationToken ct = default)
    {
        if (!await storage.Jobs.DeleteAsync(id, ct))
        {
            return false;
        }

        await continuations.DiscardPendingAsync(id, "O job pai foi excluído antes de finalizar.", ct);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<JobId> ContinuarComAsync(
        JobId paiId, JobDescriptor filho, ContinuationOptions? opcoes = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filho);
        if (paiId.IsEmpty)
        {
            throw new ArgumentException("O id do job pai é obrigatório.", nameof(paiId));
        }

        var parent = await storage.Jobs.GetAsync(paiId, ct)
            ?? throw new InvalidOperationException(
                $"Job pai '{paiId.Value}' não existe (pode ter sido excluído ou purgado pela retenção).");

        var parentLink = await storage.Continuations.GetByChildAsync(paiId, ct);
        var depth = (parentLink?.Depth ?? -1) + 1;
        if (depth >= MaxContinuationDepth)
        {
            throw new InvalidOperationException(
                $"A cadeia de continuações a partir de '{paiId.Value}' excederia a profundidade máxima " +
                $"({MaxContinuationDepth}). Revise o fluxo — cadeias tão longas normalmente indicam um laço de registro.");
        }

        var now = time.GetUtcNow();
        var childId = NewId();

        // O filho aguarda como Scheduled sem ScheduledFor: nunca elegível até o gatilho.
        await storage.Jobs.CreateAsync(new JobRecord
        {
            Id = childId,
            Descriptor = filho,
            State = JobState.Scheduled,
            Queue = filho.Queue,
            CreatedAt = now,
        }, ct);
        var link = new ContinuationRecord
        {
            ChildId = childId,
            ParentId = paiId,
            Trigger = (opcoes ?? new ContinuationOptions()).Trigger,
            Depth = depth,
            CreatedAt = now,
        };
        await storage.Continuations.AddAsync(link, ct);
        await events.PublishAsync(new JobCreated(childId, now), ct);

        // O pai pode ter finalizado (ou sumido) entre a leitura e o vínculo: reavalia
        // agora — a resolução única garante que o disparo não duplica.
        var current = await storage.Jobs.GetAsync(paiId, ct);
        if (current is null)
        {
            await continuations.DiscardPendingAsync(paiId, "O job pai foi excluído durante o registro.", ct);
        }
        else if (current.State is JobState.Succeeded or JobState.Failed)
        {
            await continuations.EvaluateAsync(link, current.State, ct);
        }

        return childId;
    }

    /// <inheritdoc />
    public async ValueTask AdicionarOuAtualizarRecorrenteAsync(
        Action<IRecurringJobBuilder> configurar, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configurar);

        var builder = new RecurringJobBuilder();
        configurar(builder);

        var now = time.GetUtcNow();
        var draft = builder.Build(now);

        CalendarRecord? calendar = null;
        if (draft.CalendarName is not null)
        {
            calendar = await storage.Recurring.GetCalendarAsync(draft.CalendarName, ct)
                ?? throw new InvalidOperationException(
                    $"Calendário '{draft.CalendarName}' não existe. " +
                    "Crie-o com AdicionarOuAtualizarCalendarioAsync antes de referenciá-lo.");
        }

        // Upsert: preserva a história da definição; o próximo disparo é sempre
        // recalculado a partir de agora com a agenda recém-configurada.
        var existing = await storage.Recurring.GetAsync(draft.Id, ct);
        var record = draft with
        {
            CreatedAt = existing?.CreatedAt ?? now,
            LastRunAt = existing?.LastRunAt,
            LastRunJobId = existing?.LastRunJobId,
            LastSkippedAt = existing?.LastSkippedAt,
            Paused = existing?.Paused ?? false,
        };
        record = record with { NextRunAt = calculator.GetNextOccurrence(record, calendar, now) };

        if (record.NextRunAt is null)
        {
            logger.LogWarning(
                "Recorrente {RecurringId} ficou sem próxima ocorrência (vigência encerrada ou calendário exclui tudo)",
                record.Id);
        }

        await storage.Recurring.UpsertAsync(record, ct);
    }

    /// <inheritdoc />
    public ValueTask AdicionarOuAtualizarRecorrenteAsync(
        string id, JobDescriptor job, string cron, TimeZoneInfo fuso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fuso);
        return AdicionarOuAtualizarRecorrenteAsync(
            builder => builder.ComId(id).Executa(job).ComCron(cron).NoFusoHorario(fuso), ct);
    }

    /// <inheritdoc />
    public ValueTask<bool> ExcluirRecorrenteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return storage.Recurring.DeleteAsync(id, ct);
    }

    /// <inheritdoc />
    public async ValueTask<bool> PausarRecorrenteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (await storage.Recurring.GetAsync(id, ct) is not { } record)
        {
            return false;
        }

        if (!record.Paused)
        {
            // NextRunAt fica como está: o painel continua mostrando quando teria rodado, e
            // a busca de vencidos já ignora pausados, então um instante no passado é inócuo.
            await storage.Recurring.UpsertAsync(record with { Paused = true }, ct);
            logger.LogInformation("Recorrente {RecurringId} pausado", id);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RetomarRecorrenteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (await storage.Recurring.GetAsync(id, ct) is not { } record)
        {
            return false;
        }

        if (!record.Paused)
        {
            return true;
        }

        // O próximo disparo é recalculado a partir de agora, e não retomado de onde parou:
        // um NextRunAt vencido durante a pausa faria a definição disparar assim que
        // voltasse, recuperando um período que deliberadamente ficou de fora.
        var now = time.GetUtcNow();
        var calendar = record.CalendarName is null
            ? null
            : await storage.Recurring.GetCalendarAsync(record.CalendarName, ct);
        var resumed = record with { Paused = false };
        resumed = resumed with { NextRunAt = calculator.GetNextOccurrence(resumed, calendar, now) };

        await storage.Recurring.UpsertAsync(resumed, ct);

        if (resumed.NextRunAt is null)
        {
            logger.LogWarning(
                "Recorrente {RecurringId} retomado sem próxima ocorrência (vigência encerrada ou calendário exclui tudo)",
                id);
        }
        else
        {
            logger.LogInformation(
                "Recorrente {RecurringId} retomado; próximo disparo em {NextRunAt}", id, resumed.NextRunAt);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<JobId?> DispararRecorrenteAgoraAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (await storage.Recurring.GetAsync(id, ct) is not { } record)
        {
            return null;
        }

        var jobId = await EnfileirarAsync(record.ToOccurrence(), ct);

        // A agenda não se move: só o histórico da última execução, que é a base da
        // checagem de sobreposição — um disparo manual conta como ocorrência anterior.
        await storage.Recurring.UpsertAsync(
            record with { LastRunAt = time.GetUtcNow(), LastRunJobId = jobId }, ct);

        logger.LogInformation(
            "Recorrente {RecurringId} disparado manualmente como o job {JobId}", id, jobId.Value);
        return jobId;
    }

    /// <inheritdoc />
    public async ValueTask AdicionarOuAtualizarCalendarioAsync(
        string nome, Action<ICalendarBuilder> configurar, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentNullException.ThrowIfNull(configurar);

        var builder = new CalendarBuilder();
        configurar(builder);
        var calendar = builder.Build(nome);

        await storage.Recurring.UpsertCalendarAsync(calendar, ct);

        // Substituir um calendário recalcula o próximo disparo de quem o usa
        // (na criação não há usuários: ComCalendario exige o calendário já existente).
        var now = time.GetUtcNow();
        foreach (var recurring in await storage.Recurring.ListAsync(ct))
        {
            if (!string.Equals(recurring.CalendarName, nome, StringComparison.Ordinal))
            {
                continue;
            }

            var next = calculator.GetNextOccurrence(recurring, calendar, now);
            if (next != recurring.NextRunAt)
            {
                await storage.Recurring.UpsertAsync(recurring with { NextRunAt = next }, ct);
            }

            if (next is null)
            {
                logger.LogWarning(
                    "Recorrente {RecurringId} ficou sem próxima ocorrência após a atualização do calendário {CalendarName}",
                    recurring.Id, nome);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExcluirCalendarioAsync(string nome, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);

        var users = (await storage.Recurring.ListAsync(ct))
            .Where(r => string.Equals(r.CalendarName, nome, StringComparison.Ordinal))
            .Select(r => r.Id)
            .ToList();
        if (users.Count > 0)
        {
            throw new InvalidOperationException(
                $"O calendário '{nome}' não pode ser excluído: está em uso pelos recorrentes " +
                $"[{string.Join(", ", users)}]. Atualize-os ou exclua-os antes.");
        }

        return await storage.Recurring.DeleteCalendarAsync(nome, ct);
    }

    private ValueTask SinalizarAsync(string queue, CancellationToken ct)
        => signal.SignalSafelyAsync(queue, logger, ct);

    private static JobId NewId() => new(Guid.NewGuid().ToString("n"));
}
