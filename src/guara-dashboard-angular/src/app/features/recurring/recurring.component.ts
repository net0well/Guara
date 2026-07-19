import { ChangeDetectionStrategy, Component, inject, resource } from '@angular/core';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

/** Definições recorrentes (leitura): agenda, fila, próximo/último disparo, pausa. */
@Component({
  selector: 'app-recurring',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InstantPipe],
  template: `
    @if (recurring.error()) {
      <p class="erro-box">{{ describe(recurring.error()) }}</p>
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
                    <span class="badge estado-Scheduled">{{ i18n.t('scheduled') }}</span>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="vazio">{{ recurring.isLoading() ? i18n.t('loading') : i18n.t('empty') }}</td></tr>
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

  protected readonly recurring = resource({
    request: () => this.sse.refresh(),
    loader: () => this.api.recurring(),
  });
}
