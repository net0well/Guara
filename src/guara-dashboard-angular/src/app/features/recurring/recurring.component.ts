import { ChangeDetectionStrategy, Component, effect, inject, resource, signal } from '@angular/core';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { Recurring, RecurringSchedule } from '../../core/models';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

/** Rascunho da edição de agenda, preenchido a partir da definição atual. */
interface Rascunho {
  agenda: 'cron' | 'interval';
  cron: string;
  interval: string;
  timeZoneId: string;
  queue: string;
  description: string;
  calendarName: string;
}

/** Definições recorrentes: agenda, histórico e as ações de operação. */
@Component({
  selector: 'app-recurring',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InstantPipe],
  template: `
    @if (erro()) {
      <p class="erro-box" role="alert">{{ erro() }}</p>
    }

    @if (recurring.error()) {
      <p class="erro-box" role="alert">{{ describe(recurring.error()) }}</p>
    } @else {
      <div class="card tabela-scroll">
        <table>
          <thead>
            <tr>
              <th>{{ i18n.t('id') }}</th>
              <th>{{ i18n.t('schedule') }}</th>
              <th>{{ i18n.t('filterQueue') }}</th>
              <th>{{ i18n.t('nextRun') }}</th>
              <th>{{ i18n.t('lastRun') }}</th>
              <th>{{ i18n.t('state') }}</th>
              <th>{{ i18n.t('actions') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of recurring.value() ?? []; track r.id) {
              <tr>
                <td>
                  <div class="mono">{{ r.id }}</div>
                  @if (r.description) { <div class="suave">{{ r.description }}</div> }
                </td>
                <td class="mono">
                  @if (r.cronExpression) {
                    {{ i18n.t('cron') }}: {{ r.cronExpression }}
                  } @else if (r.interval) {
                    {{ i18n.t('interval') }}: {{ r.interval }}
                  } @else { — }
                  @if (r.timeZoneId) { <div class="suave">{{ r.timeZoneId }}</div> }
                </td>
                <td>{{ r.queue }}</td>
                <td class="suave">{{ r.nextRunAt | instant }}</td>
                <td class="suave">{{ r.lastRunAt | instant }}</td>
                <td>
                  @if (r.paused) {
                    <span class="badge estado-Created">{{ i18n.t('paused') }}</span>
                  } @else {
                    <span class="badge estado-Scheduled">{{ i18n.t('active') }}</span>
                  }
                </td>
                <td class="acoes">
                  @if (r.paused) {
                    <button
                      type="button"
                      [disabled]="ocupado() === r.id"
                      [title]="i18n.t('resumeNoBackfill')"
                      (click)="retomar(r)">
                      {{ i18n.t('resume') }}
                    </button>
                  } @else {
                    <button type="button" [disabled]="ocupado() === r.id" (click)="pausar(r)">
                      {{ i18n.t('pause') }}
                    </button>
                  }
                  <button type="button" [disabled]="ocupado() === r.id" (click)="disparar(r)">
                    {{ i18n.t('triggerNow') }}
                  </button>
                  <button
                    type="button"
                    [attr.aria-expanded]="editando() === r.id"
                    (click)="alternarEdicao(r)">
                    {{ i18n.t('editSchedule') }}
                  </button>
                </td>
              </tr>

              @if (editando() === r.id && rascunho(); as draft) {
                <tr class="linha-edicao">
                  <td colspan="7">
                    <form class="form-agenda" (submit)="salvar($event, r)">
                      <label>
                        {{ i18n.t('schedule') }}
                        <select
                          [value]="draft.agenda"
                          (change)="editarRascunho('agenda', $event)"
                          [attr.aria-label]="i18n.t('schedule')">
                          <option value="cron">{{ i18n.t('cron') }}</option>
                          <option value="interval">{{ i18n.t('interval') }}</option>
                        </select>
                      </label>

                      @if (draft.agenda === 'cron') {
                        <label>
                          {{ i18n.t('cron') }}
                          <input
                            class="mono"
                            [value]="draft.cron"
                            (input)="editarRascunho('cron', $event)"
                            placeholder="0 3 * * *" />
                        </label>
                      } @else {
                        <label>
                          {{ i18n.t('interval') }}
                          <input
                            class="mono"
                            [value]="draft.interval"
                            (input)="editarRascunho('interval', $event)"
                            placeholder="00:05:00" />
                        </label>
                      }

                      <label>
                        {{ i18n.t('timezone') }}
                        <input [value]="draft.timeZoneId" (input)="editarRascunho('timeZoneId', $event)" />
                      </label>

                      <label>
                        {{ i18n.t('filterQueue') }}
                        <input [value]="draft.queue" (input)="editarRascunho('queue', $event)" />
                      </label>

                      <label>
                        {{ i18n.t('description') }}
                        <input [value]="draft.description" (input)="editarRascunho('description', $event)" />
                      </label>

                      <label>
                        {{ i18n.t('calendar') }}
                        <input [value]="draft.calendarName" (input)="editarRascunho('calendarName', $event)" />
                      </label>

                      <div class="acoes">
                        <button type="submit" [disabled]="ocupado() === r.id">{{ i18n.t('save') }}</button>
                        <button type="button" (click)="cancelarEdicao()">{{ i18n.t('cancel') }}</button>
                      </div>
                    </form>
                  </td>
                </tr>
              }
            } @empty {
              <tr>
                <td colspan="7" class="vazio">
                  {{ recurring.isLoading() ? i18n.t('loading') : i18n.t('empty') }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class RecurringComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;

  protected readonly recurring = resource({ loader: () => this.api.recurring() });

  protected readonly erro = signal<string | null>(null);
  protected readonly editando = signal<string | null>(null);
  protected readonly rascunho = signal<Rascunho | null>(null);

  /** Id da definição com ação em voo, para não disparar duas vezes no mesmo item. */
  protected readonly ocupado = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.sse.refresh();
      this.recurring.reload();
    });
  }

  protected alternarEdicao(r: Recurring): void {
    if (this.editando() === r.id) {
      this.cancelarEdicao();
      return;
    }

    this.editando.set(r.id);
    this.rascunho.set({
      agenda: r.interval ? 'interval' : 'cron',
      cron: r.cronExpression ?? '',
      interval: r.interval ?? '',
      timeZoneId: r.timeZoneId ?? '',
      queue: r.queue,
      description: r.description ?? '',
      calendarName: r.calendarName ?? '',
    });
  }

  protected cancelarEdicao(): void {
    this.editando.set(null);
    this.rascunho.set(null);
  }

  protected editarRascunho(campo: keyof Rascunho, event: Event): void {
    const valor = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.rascunho.update((atual) => (atual ? { ...atual, [campo]: valor } : atual));
  }

  protected async salvar(event: Event, r: Recurring): Promise<void> {
    event.preventDefault();
    const draft = this.rascunho();
    if (!draft) {
      return;
    }

    // Só um dos dois vai no corpo: mandar cron e intervalo juntos é recusado pela API.
    const schedule: RecurringSchedule = {
      cron: draft.agenda === 'cron' ? draft.cron.trim() || null : null,
      interval: draft.agenda === 'interval' ? draft.interval.trim() || null : null,
      timeZoneId: draft.timeZoneId.trim() || null,
      queue: draft.queue.trim() || null,
      description: draft.description.trim() || null,
      calendarName: draft.calendarName.trim() || null,
    };

    await this.executar(r.id, () => this.api.updateRecurring(r.id, schedule));
    if (!this.erro()) {
      this.cancelarEdicao();
    }
  }

  protected pausar(r: Recurring): Promise<void> {
    return this.executar(r.id, () => this.api.pauseRecurring(r.id));
  }

  protected retomar(r: Recurring): Promise<void> {
    return this.executar(r.id, () => this.api.resumeRecurring(r.id));
  }

  protected async disparar(r: Recurring): Promise<void> {
    await this.executar(r.id, async () => {
      await this.api.triggerRecurring(r.id);
    });
  }

  private async executar(id: string, acao: () => Promise<unknown>): Promise<void> {
    this.erro.set(null);
    this.ocupado.set(id);
    try {
      await acao();
      this.recurring.reload();
    } catch (falha) {
      this.erro.set(describeError(falha));
    } finally {
      this.ocupado.set(null);
    }
  }
}
