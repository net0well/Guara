namespace Guara.Dashboard.Api;

/// <summary>Contadores agregados do painel.</summary>
/// <param name="ByState">Contagem por estado (nome do estado → total).</param>
/// <param name="Total">Total de jobs conhecidos.</param>
internal sealed record StatsDto(IReadOnlyDictionary<string, long> ByState, long Total);

/// <summary>Uma fila e seu tamanho atual (jobs aguardando).</summary>
/// <param name="Name">Nome da fila.</param>
/// <param name="Length">Jobs enfileirados aguardando.</param>
internal sealed record QueueDto(string Name, long Length);

/// <summary>Resumo de job para listagens.</summary>
internal sealed record JobSummaryDto(
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
internal sealed record JobDetailDto(
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
internal sealed record ServerDto(
    string Id,
    string MachineName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeat,
    string[] Queues,
    int MaxConcurrency);

/// <summary>Definição recorrente para o painel.</summary>
internal sealed record RecurringDto(
    string Id,
    string? Description,
    string Queue,
    string? CronExpression,
    string? Interval,
    string? TimeZoneId,
    string? CalendarName,
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
/// <param name="Total">Total de itens que casam com os filtros, ignorando a paginação.</param>
internal sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

/// <summary>
/// Um ponto da série temporal. As latências vão em milissegundos porque é o que os
/// gráficos plotam; ficam nulas quando nenhum job terminou no balde.
/// </summary>
/// <param name="Timestamp">Início do balde.</param>
/// <param name="Succeeded">Concluídos com sucesso.</param>
/// <param name="Failed">Falhos definitivos.</param>
/// <param name="Total">Throughput do balde.</param>
/// <param name="LatencyP50Ms">Mediana do tempo de vida dos jobs finalizados.</param>
/// <param name="LatencyP95Ms">Percentil 95 do mesmo tempo de vida.</param>
internal sealed record SeriesPointDto(
    DateTimeOffset Timestamp,
    long Succeeded,
    long Failed,
    long Total,
    double? LatencyP50Ms,
    double? LatencyP95Ms);

/// <summary>Série temporal com a janela que a originou, para o gráfico rotular o eixo.</summary>
/// <param name="Window">Janela pedida (<c>1h</c>, <c>24h</c> ou <c>7d</c>).</param>
/// <param name="BucketSeconds">Largura de cada ponto.</param>
/// <param name="Points">Pontos em ordem cronológica, sem lacunas.</param>
internal sealed record SeriesDto(string Window, int BucketSeconds, IReadOnlyList<SeriesPointDto> Points);

/// <summary>Intervalo de datas excluído, com pontas inclusivas.</summary>
/// <param name="Start">Primeira data excluída.</param>
/// <param name="End">Última data excluída.</param>
internal sealed record CalendarRangeDto(DateOnly Start, DateOnly End);

/// <summary>Calendário na listagem: só o que cabe numa linha da tabela.</summary>
/// <param name="Name">Nome do calendário.</param>
/// <param name="RuleCount">Total de regras de exclusão.</param>
/// <param name="UsedBy">Recorrentes que o referenciam.</param>
internal sealed record CalendarSummaryDto(string Name, int RuleCount, IReadOnlyList<string> UsedBy);

/// <summary>
/// Calendário completo, com quem o usa e o próximo disparo de cada um — que é o efeito
/// visível de qualquer edição, já que salvar recalcula as ocorrências afetadas.
/// </summary>
/// <param name="Name">Nome do calendário.</param>
/// <param name="Dates">Datas excluídas.</param>
/// <param name="Ranges">Intervalos excluídos.</param>
/// <param name="DaysOfWeek">Dias da semana excluídos.</param>
/// <param name="CronWindows">Janelas cron excluídas.</param>
/// <param name="UsedBy">Recorrentes que o referenciam e seu próximo disparo.</param>
internal sealed record CalendarDetailDto(
    string Name,
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<CalendarRangeDto> Ranges,
    IReadOnlyList<string> DaysOfWeek,
    IReadOnlyList<string> CronWindows,
    IReadOnlyList<CalendarUsageDto> UsedBy);

/// <summary>Um recorrente que usa o calendário e quando dispara em seguida.</summary>
/// <param name="RecurringId">Id da definição.</param>
/// <param name="NextRunAt">Próximo disparo já considerando as exclusões, ou nulo.</param>
internal sealed record CalendarUsageDto(string RecurringId, DateTimeOffset? NextRunAt);

/// <summary>Exclusões enviadas ao criar ou atualizar um calendário.</summary>
/// <param name="Dates">Datas excluídas.</param>
/// <param name="Ranges">Intervalos excluídos.</param>
/// <param name="DaysOfWeek">Dias da semana excluídos (nomes em inglês, como no .NET).</param>
/// <param name="CronWindows">Janelas cron excluídas.</param>
internal sealed record CalendarUpsertRequest(
    IReadOnlyList<DateOnly>? Dates = null,
    IReadOnlyList<CalendarRangeDto>? Ranges = null,
    IReadOnlyList<string>? DaysOfWeek = null,
    IReadOnlyList<string>? CronWindows = null);

/// <summary>Campos editáveis da agenda de um recorrente; os nulos ficam como estão.</summary>
/// <param name="Cron">Nova expressão cron (exclui <paramref name="Interval"/>).</param>
/// <param name="Interval">Novo intervalo, no formato <c>d.hh:mm:ss</c>.</param>
/// <param name="TimeZoneId">Fuso da agenda (IANA ou Windows).</param>
/// <param name="Queue">Fila das ocorrências.</param>
/// <param name="Description">Descrição exibida no painel.</param>
/// <param name="CalendarName">Calendário de exclusões aplicado.</param>
internal sealed record RecurringScheduleRequest(
    string? Cron = null,
    string? Interval = null,
    string? TimeZoneId = null,
    string? Queue = null,
    string? Description = null,
    string? CalendarName = null);

/// <summary>Jobs alvo de uma ação em massa.</summary>
/// <param name="Ids">Ids selecionados.</param>
internal sealed record BulkJobsRequest(IReadOnlyList<string> Ids);

/// <summary>
/// Resultado de uma ação em massa. Nunca é tudo-ou-nada: cada item tem seu desfecho, e
/// os que falharam vêm com o motivo.
/// </summary>
/// <param name="Requested">Itens recebidos.</param>
/// <param name="Succeeded">Itens aplicados com sucesso.</param>
/// <param name="Failures">Itens que não puderam ser aplicados.</param>
internal sealed record BulkResultDto(int Requested, int Succeeded, IReadOnlyList<BulkFailureDto> Failures);

/// <summary>Um item de ação em massa que não pôde ser aplicado.</summary>
/// <param name="JobId">Job afetado.</param>
/// <param name="Reason">Por que não foi aplicado.</param>
internal sealed record BulkFailureDto(string JobId, string Reason);

/// <summary>Evento de job transmitido pelo stream SSE.</summary>
/// <param name="Kind">O que aconteceu (<c>created</c>/<c>scheduled</c>/<c>completed</c>/<c>failed</c>/<c>retry-scheduled</c>).</param>
/// <param name="JobId">Job afetado.</param>
/// <param name="OccurredAt">Instante do evento (UTC).</param>
/// <param name="Attempt">Tentativa, quando aplicável.</param>
/// <param name="Reason">Motivo, quando falha.</param>
internal sealed record JobEventDto(
    string Kind,
    string JobId,
    DateTimeOffset OccurredAt,
    int? Attempt = null,
    string? Reason = null);
