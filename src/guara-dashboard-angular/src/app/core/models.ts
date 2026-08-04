// Espelho dos DTOs de Guara.Dashboard.Api (camelCase no JSON) — o contrato HTTP é a
// única dependência da SPA em relação ao backend.

export type JobState =
  | 'Created' | 'Enqueued' | 'Scheduled' | 'Processing' | 'Succeeded' | 'Failed' | 'Retrying';

export interface Stats {
  byState: Record<string, number>;
  total: number;
}

export interface Queue {
  name: string;
  length: number;
}

export interface JobSummary {
  id: string;
  typeName: string;
  methodName: string;
  queue: string;
  state: JobState;
  attempt: number;
  createdAt: string;
  scheduledFor?: string | null;
  finishedAt?: string | null;
}

export interface JobDetail extends JobSummary {
  leaseUntil?: string | null;
  result?: string | null;
  error?: string | null;
  metadata?: Record<string, string> | null;
}

export interface Server {
  id: string;
  machineName: string;
  startedAt: string;
  lastHeartbeat: string;
  queues: string[];
  maxConcurrency: number;
}

export interface Recurring {
  id: string;
  description?: string | null;
  queue: string;
  cronExpression?: string | null;
  interval?: string | null;
  timeZoneId?: string | null;
  paused: boolean;
  skipIfPreviousRunning: boolean;
  nextRunAt?: string | null;
  lastRunAt?: string | null;
  lastSkippedAt?: string | null;
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  /** Total que casa com os filtros, ignorando a paginação. */
  total: number;
}

export type SeriesWindow = '1h' | '24h' | '7d';

export interface SeriesPoint {
  timestamp: string;
  succeeded: number;
  failed: number;
  total: number;
  latencyP50Ms?: number | null;
  latencyP95Ms?: number | null;
}

export interface Series {
  window: SeriesWindow;
  bucketSeconds: number;
  points: SeriesPoint[];
}

/** Datas chegam como 'yyyy-MM-dd' (DateOnly), sem hora nem fuso. */
export interface CalendarRange {
  start: string;
  end: string;
}

export interface CalendarSummary {
  name: string;
  ruleCount: number;
  usedBy: string[];
}

export interface CalendarUsage {
  recurringId: string;
  nextRunAt?: string | null;
}

export interface CalendarDetail {
  name: string;
  dates: string[];
  ranges: CalendarRange[];
  daysOfWeek: string[];
  cronWindows: string[];
  usedBy: CalendarUsage[];
}

export interface CalendarUpsert {
  dates?: string[];
  ranges?: CalendarRange[];
  daysOfWeek?: string[];
  cronWindows?: string[];
}

/** Campos editáveis da agenda; os omitidos ficam como estão. */
export interface RecurringSchedule {
  cron?: string | null;
  interval?: string | null;
  timeZoneId?: string | null;
  queue?: string | null;
  description?: string | null;
  calendarName?: string | null;
}

export interface BulkFailure {
  jobId: string;
  reason: string;
}

export interface BulkResult {
  requested: number;
  succeeded: number;
  failures: BulkFailure[];
}

export type JobEventKind = 'created' | 'scheduled' | 'completed' | 'failed' | 'retry-scheduled';

export interface JobEvent {
  kind: JobEventKind;
  jobId: string;
  occurredAt: string;
  attempt?: number | null;
  reason?: string | null;
}

export const JOB_STATES: readonly JobState[] =
  ['Enqueued', 'Scheduled', 'Processing', 'Succeeded', 'Failed', 'Retrying'];

export const SERIES_WINDOWS: readonly SeriesWindow[] = ['1h', '24h', '7d'];

/** Nomes em inglês porque é o que a API aceita (DayOfWeek do .NET). */
export const DAYS_OF_WEEK = [
  'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday',
] as const;

/** Teto de itens por ação em massa, espelhando o limite da API. */
export const MAX_BULK_ITEMS = 200;
