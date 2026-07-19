import { ChangeDetectionStrategy, Component, inject, resource } from '@angular/core';

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
                <td class="suave">{{ s.startedAt | instant }}</td>
                <td class="suave">{{ s.lastHeartbeat | instant }}</td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="vazio">{{ servers.isLoading() ? i18n.t('loading') : i18n.t('empty') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class ServersComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;

  protected readonly servers = resource({
    request: () => this.sse.refresh(),
    loader: () => this.api.servers(),
  });
}
