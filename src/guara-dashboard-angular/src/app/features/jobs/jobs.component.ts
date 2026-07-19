import { ChangeDetectionStrategy, Component, computed, effect, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { JOB_STATES, JobState } from '../../core/models';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

const PAGE_SIZE = 20;

/** Lista paginada de jobs com filtros por estado e fila (spec 024 AC-2). */
@Component({
  selector: 'app-jobs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, InstantPipe],
  template: `
    <div class="toolbar">
      <label>
        {{ i18n.t('filterState') }}
        <select [value]="estado() ?? ''" (change)="mudarEstado($event)">
          <option value="">{{ i18n.t('all') }}</option>
          @for (s of estados; track s) {
            <option [value]="s">{{ s }}</option>
          }
        </select>
      </label>

      <label>
        {{ i18n.t('filterQueue') }}
        <select [value]="fila() ?? ''" (change)="mudarFila($event)">
          <option value="">{{ i18n.t('all') }}</option>
          @for (q of filas.value() ?? []; track q.name) {
            <option [value]="q.name">{{ q.name }}</option>
          }
        </select>
      </label>

      <span class="espaco"></span>

      <button type="button" (click)="anterior()" [disabled]="pagina() <= 1">‹ {{ i18n.t('prev') }}</button>
      <span class="suave">{{ i18n.t('page') }} {{ pagina() }}</span>
      <button type="button" (click)="proxima()" [disabled]="!temProxima()">{{ i18n.t('next') }} ›</button>
    </div>

    @if (jobs.error()) {
      <p class="erro-box">{{ describe(jobs.error()) }}</p>
    } @else {
      <div class="card tabela-scroll">
        <table>
          <thead>
            <tr>
              <th>{{ i18n.t('id') }}</th>
              <th>{{ i18n.t('type') }}</th>
              <th>{{ i18n.t('filterQueue') }}</th>
              <th>{{ i18n.t('state') }}</th>
              <th>{{ i18n.t('attempt') }}</th>
              <th>{{ i18n.t('created') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (job of jobs.value()?.items ?? []; track job.id) {
              <tr>
                <td><a class="mono" [routerLink]="['/jobs', job.id]">{{ job.id.slice(0, 8) }}</a></td>
                <td class="mono">{{ job.typeName }}.{{ job.methodName }}</td>
                <td>{{ job.queue }}</td>
                <td><span [class]="'badge estado-' + job.state">{{ job.state }}</span></td>
                <td>{{ job.attempt }}</td>
                <td class="suave">{{ job.createdAt | instant }}</td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="vazio">{{ jobs.isLoading() ? i18n.t('loading') : i18n.t('empty') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class JobsComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;
  protected readonly estados = JOB_STATES;

  protected readonly estado = signal<JobState | null>(null);
  protected readonly fila = signal<string | null>(null);
  protected readonly pagina = signal(1);

  protected readonly filas = resource({ loader: () => this.api.queues() });

  // request só com os filtros: mudar filtro re-consulta (mostra carregando, ok); o
  // pulso do SSE recarrega via reload() preservando a lista (sem piscar).
  protected readonly jobs = resource({
    request: () => ({ state: this.estado(), queue: this.fila(), page: this.pagina() }),
    loader: ({ request }) =>
      this.api.jobs({ state: request.state, queue: request.queue, page: request.page, pageSize: PAGE_SIZE }),
  });

  constructor() {
    effect(() => {
      this.sse.refresh();
      this.jobs.reload();
      this.filas.reload();
    });
  }

  // Página cheia sugere que há próxima (a API não devolve total — paginação simples).
  protected readonly temProxima = computed(() => (this.jobs.value()?.items.length ?? 0) === PAGE_SIZE);

  protected mudarEstado(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.estado.set(value ? (value as JobState) : null);
    this.pagina.set(1);
  }

  protected mudarFila(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.fila.set(value || null);
    this.pagina.set(1);
  }

  protected anterior(): void {
    this.pagina.update((p) => Math.max(1, p - 1));
  }

  protected proxima(): void {
    if (this.temProxima()) {
      this.pagina.update((p) => p + 1);
    }
  }
}
