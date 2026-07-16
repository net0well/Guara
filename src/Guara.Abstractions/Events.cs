namespace Guara.Abstractions;

/// <summary>Marcador de eventos de domínio do Guará (nomeados no passado).</summary>
public interface IGuaraEvent
{
    /// <summary>Instante em que o evento ocorreu (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Um job foi criado.</summary>
public sealed record JobCreated(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;

/// <summary>Um job foi agendado (<c>NextRun</c> calculado).</summary>
public sealed record JobScheduled(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;

/// <summary>Um worker foi solicitado para um job elegível.</summary>
public sealed record WorkerRequested(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;

/// <summary>A execução de um job foi iniciada.</summary>
public sealed record ExecutorStarted(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;

/// <summary>Um job foi concluído com sucesso.</summary>
public sealed record JobCompleted(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;

/// <summary>Um job falhou.</summary>
/// <param name="Id">Identificador do job.</param>
/// <param name="OccurredAt">Instante do evento (UTC).</param>
/// <param name="Reason">Motivo da falha, quando conhecido.</param>
public sealed record JobFailed(JobId Id, DateTimeOffset OccurredAt, string? Reason = null) : IGuaraEvent;
