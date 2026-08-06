import { ChangeDetectionStrategy, Component, effect, inject, resource } from '@angular/core';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

/** Nós servidores registrados e seus heartbeats (leitura, ao vivo). */
@Component({
  selector: 'app-servers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InstantPipe],
  template: `
    @if (servers.error()) {
      <p class="erro-box">{{ describe(servers.error()) }}</p>
    } @else {
      <div class="card tabela-scroll">
        <table>
          <thead>
            <tr>
              <th>{{ i18n.t('id') }}</th>
              <th>{{ i18n.t('machine') }}</th>
              <th>{{ i18n.t('queues') }}</th>
              <th>{{ i18n.t('concurrency') }}</th>
              <th>{{ i18n.t('roles') }}</th>
              <th>{{ i18n.t('startedAt') }}</th>
              <th>{{ i18n.t('heartbeat') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (s of servers.value() ?? []; track s.id) {
              <tr>
                <td class="mono">{{ s.id }}</td>
                <td>{{ s.machineName }}</td>
                <td class="mono">{{ s.queues.join(', ') }}</td>
                <td>{{ s.maxConcurrency }}</td>
                <td class="papeis">
                  @for (papel of s.roles; track papel) {
                    <span class="badge papel">{{ rotuloPapel(papel) }}</span>
                  } @empty {
                    <span class="suave" aria-hidden="true">—</span>
                  }
                </td>
                <td class="suave">{{ s.startedAt | instant }}</td>
                <td class="suave">{{ s.lastHeartbeat | instant }}</td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="vazio">{{ servers.isLoading() ? i18n.t('loading') : i18n.t('empty') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    .papeis { display: flex; flex-wrap: wrap; gap: 0.3rem; }
    .papel { background: var(--cartao-2); color: var(--marca); border-color: var(--borda); }
  `,
})
export class ServersComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;

  protected readonly servers = resource({ loader: () => this.api.servers() });

  /**
   * Traduz os papéis do framework. Papel de terceiro cai no próprio nome, que é melhor
   * do que esconder um papel que o painel não conhece.
   */
  protected rotuloPapel(papel: string): string {
    switch (papel) {
      case 'guara:recurring':
        return this.i18n.t('roleRecurring');
      case 'guara:maintenance':
        return this.i18n.t('roleMaintenance');
      default:
        return papel;
    }
  }

  constructor() {
    effect(() => {
      this.sse.refresh();
      this.servers.reload();
    });
  }
}
