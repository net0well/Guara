import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { I18nService } from './core/i18n.service';
import { SseService } from './core/sse.service';
import { ThemeService } from './core/theme.service';

/** Shell do dashboard: topbar com a marca, navegação, indicador ao vivo e toggles. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a class="marca" routerLink="/">
        <img src="assets/logo.png" alt="Guará" width="34" height="34" />
        <span>Guará</span>
      </a>

      <nav aria-label="Seções do dashboard">
        <a routerLink="/" routerLinkActive="ativo" [routerLinkActiveOptions]="{ exact: true }">{{ i18n.t('overview') }}</a>
        <a routerLink="/jobs" routerLinkActive="ativo">{{ i18n.t('jobs') }}</a>
        <a routerLink="/recurring" routerLinkActive="ativo">{{ i18n.t('recurring') }}</a>
        <a routerLink="/servers" routerLinkActive="ativo">{{ i18n.t('servers') }}</a>
      </nav>

      <span class="espaco"></span>

      <span class="live" [class.on]="sse.connected()" [title]="ao()">
        <span class="ponto" aria-hidden="true"></span>{{ ao() }}
      </span>
      <button type="button" (click)="i18n.toggle()" [attr.aria-label]="'idioma'">{{ i18n.lang() === 'pt' ? 'PT' : 'EN' }}</button>
      <button type="button" (click)="theme.cycle()" aria-label="tema">{{ temaIcone() }}</button>
    </header>

    <main><router-outlet /></main>
  `,
  styles: [`
    :host { display: block; }
    .topbar {
      position: sticky; top: 0; z-index: 10;
      display: flex; align-items: center; gap: 1rem;
      padding: 0.6rem 1.25rem;
      background: color-mix(in srgb, var(--cartao) 88%, transparent);
      backdrop-filter: blur(8px);
      border-bottom: 1px solid var(--borda);
    }
    .marca { display: flex; align-items: center; gap: 0.5rem; font-weight: 800; font-size: 1.15rem; color: var(--texto); }
    .marca:hover { text-decoration: none; }
    .marca img { border-radius: 8px; }
    nav { display: flex; gap: 0.25rem; flex-wrap: wrap; }
    nav a { color: var(--suave); padding: 0.35rem 0.7rem; border-radius: 8px; font-weight: 600; }
    nav a:hover { color: var(--texto); text-decoration: none; background: var(--cartao-2); }
    nav a.ativo { color: var(--marca); background: color-mix(in srgb, var(--marca) 12%, transparent); }
    .live { display: inline-flex; align-items: center; gap: 0.4rem; color: var(--suave); font-size: 0.85rem; }
    .live .ponto { width: 9px; height: 9px; border-radius: 50%; background: var(--suave); }
    .live.on .ponto { background: var(--ok); box-shadow: 0 0 0 3px color-mix(in srgb, var(--ok) 25%, transparent); }
    main { padding: 1.5rem 1.25rem; max-width: 1200px; margin: 0 auto; }
    @media (max-width: 620px) { .marca span { display: none; } }
  `],
})
export class AppComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly theme = inject(ThemeService);
  protected readonly sse = inject(SseService);

  protected readonly ao = computed(() =>
    this.sse.connected() ? this.i18n.t('live') : this.i18n.t('reconnecting'));

  protected readonly temaIcone = computed(() => {
    switch (this.theme.theme()) {
      case 'light': return '☀';
      case 'dark': return '☾';
      default: return '◐';
    }
  });
}
