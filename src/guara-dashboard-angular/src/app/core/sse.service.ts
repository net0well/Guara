import { DestroyRef, inject, Injectable, signal } from '@angular/core';

import { JobEvent } from './models';

/**
 * Ponte do stream SSE (`api/v1/stream`) para signals. Entrega instantânea via
 * <c>EventSource</c> (reconexão nativa) e, se o stream cair, um poll de segurança
 * mantém as telas atualizadas — nunca ficam desatualizadas em silêncio.
 *
 * <c>lastEvent</c> reflete cada evento na hora (indicador de atividade); <c>refresh</c>
 * é coalescido (no máx. uma vez por segundo) para as telas recarregarem sem martelar
 * a API sob rajada de eventos.
 */
@Injectable({ providedIn: 'root' })
export class SseService {
  private static readonly CoalesceMs = 1000;
  private static readonly FallbackPollMs = 5000;

  private readonly _connected = signal(false);
  private readonly _lastEvent = signal<JobEvent | null>(null);
  private readonly _refresh = signal(0);

  readonly connected = this._connected.asReadonly();
  readonly lastEvent = this._lastEvent.asReadonly();
  /** Incrementa quando as telas devem recarregar (coalescido / poll de fallback). */
  readonly refresh = this._refresh.asReadonly();

  private source?: EventSource;
  private flushTimer?: ReturnType<typeof setTimeout>;
  private pollTimer?: ReturnType<typeof setInterval>;
  private pendingRefresh = false;

  constructor() {
    this.connect();
    this.pollTimer = setInterval(() => {
      if (!this._connected()) {
        this.bumpNow(); // sem SSE: mantém as telas frescas por polling
      }
    }, SseService.FallbackPollMs);

    inject(DestroyRef).onDestroy(() => this.dispose());
  }

  private connect(): void {
    this.source = new EventSource('api/v1/stream');
    this.source.onopen = () => this._connected.set(true);
    this.source.onerror = () => this._connected.set(false); // EventSource reconecta sozinho
    this.source.addEventListener('job', (event) => {
      try {
        this._lastEvent.set(JSON.parse((event as MessageEvent).data) as JobEvent);
      } catch {
        // Evento malformado é ignorado; o poll de fallback ainda reconcilia.
      }

      this.scheduleRefresh();
    });
  }

  private scheduleRefresh(): void {
    if (this.flushTimer) {
      this.pendingRefresh = true;
      return;
    }

    this.bumpNow();
    this.flushTimer = setTimeout(() => {
      this.flushTimer = undefined;
      if (this.pendingRefresh) {
        this.pendingRefresh = false;
        this.scheduleRefresh();
      }
    }, SseService.CoalesceMs);
  }

  private bumpNow(): void {
    this._refresh.update((value) => value + 1);
  }

  private dispose(): void {
    this.source?.close();
    if (this.flushTimer) {
      clearTimeout(this.flushTimer);
    }

    if (this.pollTimer) {
      clearInterval(this.pollTimer);
    }
  }
}
