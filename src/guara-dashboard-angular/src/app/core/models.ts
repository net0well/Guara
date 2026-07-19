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
