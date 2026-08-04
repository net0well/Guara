import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  BulkResult, CalendarDetail, CalendarSummary, CalendarUpsert, JobDetail, JobState, JobSummary,
  Page, Queue, Recurring, RecurringSchedule, Series, SeriesWindow, Server, Stats,
} from './models';

/** Filtros da listagem de jobs. */
export interface JobsQuery {
  state?: JobState | null;
  queue?: string | null;
  page?: number;
  pageSize?: number;
}

/** Filtros da busca: os de JobsQuery mais texto livre, tipo e intervalo de criação. */
export interface JobsSearchQuery extends JobsQuery {
  text?: string | null;
  type?: string | null;
  /** Instantes em ISO-8601; o fim é exclusivo. */
  from?: string | null;
  to?: string | null;
}

/**
 * Acesso tipado à API v1 do dashboard. URLs relativas resolvem contra o
 * <base href> (o BasePath real) — a SPA não conhece a rota base. Métodos devolvem
 * Promises para alimentar diretamente os <c>resource()</c> das telas.
 */
@Injectable({ providedIn: 'root' })
export class GuaraApi {
  private readonly http = inject(HttpClient);

  stats(): Promise<Stats> {
    return firstValueFrom(this.http.get<Stats>('api/v1/stats'));
  }

  queues(): Promise<Queue[]> {
    return firstValueFrom(this.http.get<Queue[]>('api/v1/queues'));
  }

  jobs(query: JobsQuery): Promise<Page<JobSummary>> {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 20));
    if (query.state) {
      params = params.set('state', query.state);
    }

    if (query.queue) {
      params = params.set('queue', query.queue);
    }

    return firstValueFrom(this.http.get<Page<JobSummary>>('api/v1/jobs', { params }));
  }

  job(id: string): Promise<JobDetail> {
    return firstValueFrom(this.http.get<JobDetail>(`api/v1/jobs/${encodeURIComponent(id)}`));
  }

  servers(): Promise<Server[]> {
    return firstValueFrom(this.http.get<Server[]>('api/v1/servers'));
  }

  recurring(): Promise<Recurring[]> {
    return firstValueFrom(this.http.get<Recurring[]>('api/v1/recurring'));
  }

  retry(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`api/v1/jobs/${encodeURIComponent(id)}/retry`, null));
  }

  trigger(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`api/v1/jobs/${encodeURIComponent(id)}/trigger`, null));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`api/v1/jobs/${encodeURIComponent(id)}`));
  }

  searchJobs(query: JobsSearchQuery): Promise<Page<JobSummary>> {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 20));

    // Só o que tem valor vira parâmetro: enviar chave vazia faria a API filtrar por vazio.
    const optional: Record<string, string | null | undefined> = {
      q: query.text,
      type: query.type,
      queue: query.queue,
      state: query.state,
      from: query.from,
      to: query.to,
    };
    for (const [key, value] of Object.entries(optional)) {
      if (value) {
        params = params.set(key, value);
      }
    }

    return firstValueFrom(this.http.get<Page<JobSummary>>('api/v1/jobs/search', { params }));
  }

  series(window: SeriesWindow, queue?: string | null): Promise<Series> {
    let params = new HttpParams().set('window', window);
    if (queue) {
      params = params.set('queue', queue);
    }

    return firstValueFrom(this.http.get<Series>('api/v1/stats/series', { params }));
  }

  pauseRecurring(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`api/v1/recurring/${encodeURIComponent(id)}/pause`, null));
  }

  resumeRecurring(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`api/v1/recurring/${encodeURIComponent(id)}/resume`, null));
  }

  triggerRecurring(id: string): Promise<JobSummary> {
    return firstValueFrom(
      this.http.post<JobSummary>(`api/v1/recurring/${encodeURIComponent(id)}/trigger`, null));
  }

  updateRecurring(id: string, schedule: RecurringSchedule): Promise<void> {
    return firstValueFrom(this.http.patch<void>(`api/v1/recurring/${encodeURIComponent(id)}`, schedule));
  }

  calendars(): Promise<CalendarSummary[]> {
    return firstValueFrom(this.http.get<CalendarSummary[]>('api/v1/calendars'));
  }

  calendar(name: string): Promise<CalendarDetail> {
    return firstValueFrom(this.http.get<CalendarDetail>(`api/v1/calendars/${encodeURIComponent(name)}`));
  }

  saveCalendar(name: string, calendar: CalendarUpsert): Promise<CalendarDetail> {
    return firstValueFrom(
      this.http.put<CalendarDetail>(`api/v1/calendars/${encodeURIComponent(name)}`, calendar));
  }

  deleteCalendar(name: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`api/v1/calendars/${encodeURIComponent(name)}`));
  }

  bulkRetry(ids: string[]): Promise<BulkResult> {
    return firstValueFrom(this.http.post<BulkResult>('api/v1/jobs/bulk/retry', { ids }));
  }

  bulkDelete(ids: string[]): Promise<BulkResult> {
    return firstValueFrom(this.http.post<BulkResult>('api/v1/jobs/bulk/delete', { ids }));
  }
}
