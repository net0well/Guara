namespace Guara.Dashboard.Api;

/// <summary>Contadores agregados do painel.</summary>
/// <param name="ByState">Contagem por estado (nome do estado → total).</param>
/// <param name="Total">Total de jobs conhecidos.</param>
public sealed record StatsDto(IReadOnlyDictionary<string, long> ByState, long Total);

/// <summary>Uma fila e seu tamanho atual (jobs aguardando).</summary>
/// <param name="Name">Nome da fila.</param>
/// <param name="Length">Jobs enfileirados aguardando.</param>
public sealed record QueueDto(string Name, long Length);

/// <summary>Resumo de job para listagens.</summary>
public sealed record JobSummaryDto(
    string Id,
    string TypeName,
    string MethodName,
    string Queue,
    string State,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? FinishedAt);

/// <summary>Detalhe de job (o payload de argumentos fica fora até a autorização granular).</summary>
public sealed record JobDetailDto(
    string Id,
    string TypeName,
    string MethodName,
    string Queue,
    string State,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? LeaseUntil,
    DateTimeOffset? FinishedAt,
    string? Result,
    string? Error,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>Nó servidor registrado.</summary>
public sealed record ServerDto(
    string Id,
    string MachineName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeat,
    string[] Queues,
    int MaxConcurrency);

/// <summary>Definição recorrente para o painel.</summary>
public sealed record RecurringDto(
    string Id,
    string? Description,
    string Queue,
    string? CronExpression,
    string? Interval,
    string? TimeZoneId,
    bool Paused,
    bool SkipIfPreviousRunning,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? LastSkippedAt);

/// <summary>Envelope de paginação das listagens.</summary>
/// <typeparam name="T">Tipo do item.</typeparam>
/// <param name="Items">Itens da página.</param>
/// <param name="Page">Página (1-based).</param>
/// <param name="PageSize">Tamanho efetivo aplicado (respeita o teto).</param>
public sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize);

/// <summary>Evento de job transmitido pelo stream SSE.</summary>
/// <param name="Kind">O que aconteceu (<c>created</c>/<c>scheduled</c>/<c>completed</c>/<c>failed</c>/<c>retry-scheduled</c>).</param>
/// <param name="JobId">Job afetado.</param>
/// <param name="OccurredAt">Instante do evento (UTC).</param>
/// <param name="Attempt">Tentativa, quando aplicável.</param>
/// <param name="Reason">Motivo, quando falha.</param>
public sealed record JobEventDto(
    string Kind,
    string JobId,
    DateTimeOffset OccurredAt,
    int? Attempt = null,
    string? Reason = null);
