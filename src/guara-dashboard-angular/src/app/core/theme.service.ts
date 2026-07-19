import { effect, Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark' | 'system';

/** Tema claro/escuro/sistema, persistido e aplicado via <c>data-theme</c> no root. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private static readonly Key = 'guara.theme';

  private readonly _theme = signal<Theme>(this.load());
  readonly theme = this._theme.asReadonly();

  constructor() {
    effect(() => {
      const value = this._theme();
      const root = document.documentElement;
      if (value === 'system') {
        root.removeAttribute('data-theme');
      } else {
        root.dataset['theme'] = value;
      }

      localStorage.setItem(ThemeService.Key, value);
    });
  }

  /** Cicla claro → escuro → sistema. */
  cycle(): void {
    this._theme.update((current) =>
      current === 'light' ? 'dark' : current === 'dark' ? 'system' : 'light');
  }

  private load(): Theme {
    const stored = localStorage.getItem(ThemeService.Key);
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  }
}
