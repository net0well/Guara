import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { JobDetail, JobState, JobSummary, Page, Queue, Recurring, Server, Stats } from './models';

/** Filtros da listagem de jobs. */
export interface JobsQuery {
  state?: JobState | null;
  queue?: string | null;
  page?: number;
  pageSize?: number;
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
}
