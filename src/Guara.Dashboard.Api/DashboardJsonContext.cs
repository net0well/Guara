using System.Text.Json.Serialization;

namespace Guara.Dashboard.Api;

/// <summary>
/// Contexto de serialização dos DTOs do dashboard (gerado em compilação — zero
/// reflection, AOT-safe). Registrado na cadeia de resolvers do host pelo
/// <c>AddGuaraDashboardApi()</c>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(IReadOnlyList<QueueDto>))]
[JsonSerializable(typeof(PageDto<JobSummaryDto>))]
[JsonSerializable(typeof(JobDetailDto))]
[JsonSerializable(typeof(IReadOnlyList<ServerDto>))]
[JsonSerializable(typeof(IReadOnlyList<RecurringDto>))]
[JsonSerializable(typeof(JobEventDto))]
[JsonSerializable(typeof(SeriesDto))]
[JsonSerializable(typeof(IReadOnlyList<CalendarSummaryDto>))]
[JsonSerializable(typeof(CalendarDetailDto))]
[JsonSerializable(typeof(CalendarUpsertRequest))]
[JsonSerializable(typeof(RecurringScheduleRequest))]
[JsonSerializable(typeof(BulkJobsRequest))]
[JsonSerializable(typeof(BulkResultDto))]
internal sealed partial class DashboardJsonContext : JsonSerializerContext;
