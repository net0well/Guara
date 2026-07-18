using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Scheduler;

/// <summary>
/// Implementação default de <see cref="IGuaraClient"/>: persiste jobs e definições no
/// storage ("o storage é a fila") e emite os eventos do fluxo (<see cref="JobCreated"/>,
/// <see cref="JobScheduled"/>) — nunca chama outro componente diretamente.
/// </summary>
public sealed class GuaraClient(
    IStorage storage,
    IEventPublisher events,
    RecurrenceCalculator calculator,
    TimeProvider time,
    ILogger<GuaraClient> logger) : IGuaraClient
{
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
        return id;
    }

    /// <inheritdoc />
    public ValueTask<bool> ExcluirAsync(JobId id, CancellationToken ct = default)
        => storage.Jobs.DeleteAsync(id, ct);

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

    private static JobId NewId() => new(Guid.NewGuid().ToString("n"));
}
