import { ChangeDetectionStrategy, Component, computed, effect, inject, resource } from '@angular/core';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { stateColorVar } from '../../shared/state';

interface Slice {
  state: string;
  count: number;
  color: string;
  dash: number;
  offset: number;
}

/** Visão geral: total, distribuição por estado (donut SVG) e tamanho das filas. */
@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (stats.value(); as s) {
      <section class="topo">
        <div class="card total">
          <span class="rotulo suave">{{ i18n.t('total') }}</span>
          <strong>{{ s.total }}</strong>
        </div>

        <div class="card grafico" role="img" [attr.aria-label]="i18n.t('byState')">
          <svg viewBox="0 0 42 42" width="130" height="130" aria-hidden="true">
            <circle cx="21" cy="21" r="15.9" fill="none" stroke="var(--borda)" stroke-width="4" />
            @for (slice of slices(); track slice.state) {
              <circle cx="21" cy="21" r="15.9" fill="none" [attr.stroke]="slice.color" stroke-width="4"
                      [attr.stroke-dasharray]="slice.dash + ' ' + (100 - slice.dash)"
                      [attr.stroke-dashoffset]="slice.offset" transform="rotate(-90 21 21)" />
            }
          </svg>
          <ul class="legenda">
            @for (slice of slices(); track slice.state) {
              <li>
                <span class="ponto" [style.background]="slice.color"></span>
                {{ slice.state }} <strong>{{ slice.count }}</strong>
              </li>
            } @empty {
              <li class="suave">{{ i18n.t('empty') }}</li>
            }
          </ul>
        </div>
      </section>

      <h2>{{ i18n.t('byState') }}</h2>
      <div class="grid">
        @for (item of estados(); track item.state) {
          <div class="card">
            <span [class]="'badge estado-' + item.state">{{ item.state }}</span>
            <strong class="numero">{{ item.count }}</strong>
          </div>
        }
      </div>

      <h2>{{ i18n.t('queues') }}</h2>
      <div class="card tabela-scroll">
        @if (queues.value(); as qs) {
          <table>
            <thead><tr><th>{{ i18n.t('filterQueue') }}</th><th>{{ i18n.t('jobs') }}</th></tr></thead>
            <tbody>
              @for (q of qs; track q.name) {
                <tr><td class="mono">{{ q.name }}</td><td>{{ q.length }}</td></tr>
              } @empty {
                <tr><td colspan="2" class="vazio">{{ i18n.t('empty') }}</td></tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="suave">{{ i18n.t('loading') }}</p>
        }
      </div>
    } @else if (stats.error()) {
      <p class="erro-box">{{ describe(stats.error()) }}</p>
    } @else {
      <p class="suave">{{ i18n.t('loading') }}</p>
    }
  `,
  styles: [`
    .topo { display: flex; flex-wrap: wrap; gap: 1rem; margin-bottom: 1.5rem; }
    .total { display: flex; flex-direction: column; justify-content: center; min-width: 160px; }
    .total strong { font-size: 2.6rem; line-height: 1; }
    .rotulo { text-transform: uppercase; font-size: 0.8rem; letter-spacing: 0.04em; }
    .grafico { display: flex; align-items: center; gap: 1.25rem; flex: 1; min-width: 280px; }
    .grafico circle { transition: stroke-dasharray .5s ease, stroke-dashoffset .5s ease; }
    .total strong, .numero { transition: color .3s ease; }
    .legenda { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.3rem; }
    .legenda li { display: flex; align-items: center; gap: 0.5rem; }
    .legenda .ponto { width: 11px; height: 11px; border-radius: 3px; display: inline-block; }
    h2 { font-size: 1rem; color: var(--suave); text-transform: uppercase; letter-spacing: 0.04em; margin: 1.5rem 0 0.75rem; }
    .numero { display: block; font-size: 1.8rem; margin-top: 0.4rem; }
  `],
})
export class OverviewComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;

  protected readonly stats = resource({ loader: () => this.api.stats() });
  protected readonly queues = resource({ loader: () => this.api.queues() });

  constructor() {
    // Atualização ao vivo suave: no pulso do SSE recarrega mantendo os dados na tela
    // (reload preserva o value; recriar o request piscaria a UI).
    effect(() => {
      this.sse.refresh();
      this.stats.reload();
      this.queues.reload();
    });
  }

  protected readonly estados = computed(() => {
    const value = this.stats.value();
    if (!value) {
      return [];
    }

    return Object.entries(value.byState)
      .map(([state, count]) => ({ state, count }))
      .sort((a, b) => b.count - a.count);
  });

  protected readonly slices = computed<Slice[]>(() => {
    const value = this.stats.value();
    if (!value || value.total === 0) {
      return [];
    }

    let cumulative = 0;
    return Object.entries(value.byState).map(([state, count]) => {
      const dash = (count / value.total) * 100;
      const slice: Slice = {
        state,
        count,
        color: stateColorVar(state),
        dash,
        // dashoffset negativo posiciona a fatia após as anteriores (SVG começa às 3h).
        offset: 25 - cumulative,
      };
      cumulative += dash;
      return slice;
    });
  });
}
