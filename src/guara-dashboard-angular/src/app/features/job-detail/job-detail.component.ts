import { ChangeDetectionStrategy, Component, computed, effect, inject, input, resource, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

/** Detalhe de um job com as ações disponíveis conforme o estado. */
@Component({
  selector: 'app-job-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, InstantPipe],
  template: `
    <div class="toolbar">
      <a routerLink="/jobs">‹ {{ i18n.t('back') }}</a>
      <span class="espaco"></span>
      @if (job.value(); as j) {
        <button type="button" (click)="retry()" [disabled]="ocupado() || j.state !== 'Failed'">{{ i18n.t('retry') }}</button>
        <button type="button" (click)="trigger()" [disabled]="ocupado() || !podeDisparar(j.state)">{{ i18n.t('trigger') }}</button>
        <button type="button" class="perigo" (click)="excluir()" [disabled]="ocupado() || j.state === 'Processing'">{{ i18n.t('remove') }}</button>
      }
    </div>

    @if (acao(); as msg) {
      <p class="erro-box">{{ msg }}</p>
    }

    @if (job.value(); as j) {
      <div class="card">
        <h1 class="mono">{{ j.id }}</h1>
        <dl>
          <dt>{{ i18n.t('state') }}</dt><dd><span [class]="'badge estado-' + j.state">{{ j.state }}</span></dd>
          <dt>{{ i18n.t('type') }}</dt><dd class="mono">{{ j.typeName }}.{{ j.methodName }}</dd>
          <dt>{{ i18n.t('filterQueue') }}</dt><dd>{{ j.queue }}</dd>
          <dt>{{ i18n.t('attempt') }}</dt><dd>{{ j.attempt }}</dd>
          <dt>{{ i18n.t('created') }}</dt><dd>{{ j.createdAt | instant }}</dd>
          <dt>{{ i18n.t('scheduled') }}</dt><dd>{{ j.scheduledFor | instant }}</dd>
          <dt>{{ i18n.t('lease') }}</dt><dd>{{ j.leaseUntil | instant }}</dd>
          <dt>{{ i18n.t('finished') }}</dt><dd>{{ j.finishedAt | instant }}</dd>
          @if (j.result) { <dt>{{ i18n.t('result') }}</dt><dd class="mono">{{ j.result }}</dd> }
          @if (j.error) { <dt>{{ i18n.t('error') }}</dt><dd class="mono erro-texto">{{ j.error }}</dd> }
        </dl>

        @if (metadados().length) {
          <h2>{{ i18n.t('metadata') }}</h2>
          <dl>
            @for (m of metadados(); track m.key) {
              <dt class="mono">{{ m.key }}</dt><dd class="mono">{{ m.value }}</dd>
            }
          </dl>
        }
      </div>
    } @else if (job.error()) {
      <p class="erro-box">{{ describe(job.error()) }}</p>
    } @else {
      <p class="suave">{{ i18n.t('loading') }}</p>
    }
  `,
  styles: [`
    h1 { font-size: 1.1rem; margin: 0 0 1rem; word-break: break-all; }
    h2 { font-size: 0.9rem; color: var(--suave); text-transform: uppercase; letter-spacing: 0.04em; margin: 1.25rem 0 0.5rem; }
    dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.4rem 1.25rem; margin: 0; }
    dt { color: var(--suave); font-weight: 600; }
    dd { margin: 0; word-break: break-word; }
    .erro-texto { color: var(--erro); white-space: pre-wrap; }
  `],
})
export class JobDetailComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  private readonly router = inject(Router);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;

  // Vinculado ao parâmetro de rota :id (withComponentInputBinding).
  readonly id = input.required<string>();

  protected readonly ocupado = signal(false);
  protected readonly acao = signal<string | null>(null);

  protected readonly job = resource({
    request: () => this.id(),
    loader: ({ request }) => this.api.job(request),
  });

  constructor() {
    // Pulso do SSE recarrega o detalhe sem piscar (reload preserva o value).
    effect(() => {
      this.sse.refresh();
      this.job.reload();
    });
  }

  protected readonly metadados = computed(() => {
    const meta = this.job.value()?.metadata;
    return meta ? Object.entries(meta).map(([key, value]) => ({ key, value })) : [];
  });

  protected podeDisparar(state: string): boolean {
    return state === 'Scheduled' || state === 'Retrying';
  }

  protected async retry(): Promise<void> {
    await this.executar(() => this.api.retry(this.id()));
  }

  protected async trigger(): Promise<void> {
    await this.executar(() => this.api.trigger(this.id()));
  }

  protected async excluir(): Promise<void> {
    if (!confirm(this.i18n.t('confirmRemove'))) {
      return;
    }

    const ok = await this.executar(() => this.api.remove(this.id()));
    if (ok) {
      await this.router.navigate(['/jobs']);
    }
  }

  private async executar(acao: () => Promise<void>): Promise<boolean> {
    this.ocupado.set(true);
    this.acao.set(null);
    try {
      await acao();
      this.job.reload();
      return true;
    } catch (error) {
      this.acao.set(describeError(error));
      return false;
    } finally {
      this.ocupado.set(false);
    }
  }
}
