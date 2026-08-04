import { ChangeDetectionStrategy, Component, computed, effect, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { BulkResult, JOB_STATES, JobState, MAX_BULK_ITEMS } from '../../core/models';
import { SseService } from '../../core/sse.service';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

const PAGE_SIZE = 20;

/** Meia-noite local do dia seguinte ao informado, em ISO-8601. */
function diaSeguinte(data: string): string {
  const limite = new Date(`${data}T00:00:00`);
  limite.setDate(limite.getDate() + 1);
  return limite.toISOString();
}

/** Busca de jobs com filtros compostos, seleção múltipla e ações em massa. */
@Component({
  selector: 'app-jobs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, InstantPipe],
  template: `
    <form class="toolbar" (submit)="buscar($event)">
      <label>
        {{ i18n.t('search') }}
        <input
          type="search"
          [value]="textoDigitado()"
          (input)="textoDigitado.set($any($event.target).value)"
          [attr.placeholder]="i18n.t('searchPlaceholder')" />
      </label>

      <label>
        {{ i18n.t('filterType') }}
        <input
          class="mono"
          [value]="tipoDigitado()"
          (input)="tipoDigitado.set($any($event.target).value)" />
      </label>

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

      <label>
        {{ i18n.t('from') }}
        <input type="date" [value]="deDigitado()" (input)="deDigitado.set($any($event.target).value)" />
      </label>

      <label>
        {{ i18n.t('to') }}
        <input type="date" [value]="ateDigitado()" (input)="ateDigitado.set($any($event.target).value)" />
      </label>

      <button type="submit" class="primaria">{{ i18n.t('search') }}</button>
      <button type="button" (click)="limpar()">{{ i18n.t('clearFilters') }}</button>
    </form>

    <div class="toolbar">
      <span class="suave">{{ jobs.value()?.total ?? 0 }} {{ i18n.t('results') }}</span>

      @if (selecao().size > 0) {
        <span class="suave">· {{ selecao().size }} {{ i18n.t('selected') }}</span>
        <button type="button" [disabled]="emMassa()" (click)="retentarSelecionados()">
          {{ i18n.t('bulkRetry') }}
        </button>
        <button type="button" class="perigo" [disabled]="emMassa()" (click)="excluirSelecionados()">
          {{ i18n.t('bulkRemove') }}
        </button>
        <button type="button" (click)="limparSelecao()">{{ i18n.t('clearSelection') }}</button>
      }

      <span class="espaco"></span>

      <button type="button" (click)="anterior()" [disabled]="pagina() <= 1">‹ {{ i18n.t('prev') }}</button>
      <span class="suave">{{ i18n.t('page') }} {{ pagina() }} / {{ totalPaginas() }}</span>
      <button type="button" (click)="proxima()" [disabled]="pagina() >= totalPaginas()">{{ i18n.t('next') }} ›</button>
    </div>

    @if (erro()) {
      <p class="erro-box" role="alert">{{ erro() }}</p>
    }

    @if (resultadoMassa(); as r) {
      <p class="card resultado-massa" aria-live="polite">
        <strong>{{ r.succeeded }}</strong> {{ i18n.t('bulkOutcome') }} <strong>{{ r.requested }}</strong>
        {{ i18n.t('bulkApplied') }}.
        @if (r.failures.length > 0) {
          <ul>
            @for (f of r.failures; track f.jobId) {
              <li><span class="mono">{{ f.jobId }}</span> — {{ f.reason }}</li>
            }
          </ul>
        }
      </p>
    }

    @if (jobs.error()) {
      <p class="erro-box" role="alert">{{ describe(jobs.error()) }}</p>
    } @else {
      <div class="card tabela-scroll">
        <table>
          <thead>
            <tr>
              <th class="col-check">
                <input
                  type="checkbox"
                  [checked]="paginaToda()"
                  [attr.aria-label]="i18n.t('selectAll')"
                  (change)="alternarPagina()" />
              </th>
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
              <tr [class.selecionada]="selecao().has(job.id)">
                <td class="col-check">
                  <input
                    type="checkbox"
                    [checked]="selecao().has(job.id)"
                    [attr.aria-label]="job.id"
                    (change)="alternar(job.id)" />
                </td>
                <td><a class="mono" [routerLink]="['/jobs', job.id]">{{ job.id.slice(0, 8) }}</a></td>
                <td class="mono">{{ job.typeName }}.{{ job.methodName }}</td>
                <td>{{ job.queue }}</td>
                <td><span [class]="'badge estado-' + job.state">{{ job.state }}</span></td>
                <td>{{ job.attempt }}</td>
                <td class="suave">{{ job.createdAt | instant }}</td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="vazio">{{ jobs.isLoading() ? i18n.t('loading') : i18n.t('empty') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: [`
    .col-check { width: 2.2rem; }
    tr.selecionada { background: color-mix(in srgb, var(--marca) 8%, transparent); }
    .toolbar label { display: flex; flex-direction: column; gap: 0.2rem; font-size: 0.8rem; color: var(--suave); }
    .toolbar input[type='date'], .toolbar input[type='search'] { min-width: 9rem; }
    .resultado-massa { margin-bottom: 1rem; font-size: 0.9rem; }
    .resultado-massa ul { margin: 0.5rem 0 0; padding-left: 1.1rem; color: var(--suave); }
  `],
})
export class JobsComponent {
  private readonly api = inject(GuaraApi);
  private readonly sse = inject(SseService);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;
  protected readonly estados = JOB_STATES;

  // Digitados ficam separados dos aplicados: a busca só dispara ao submeter, senão
  // cada tecla viraria uma consulta.
  protected readonly textoDigitado = signal('');
  protected readonly tipoDigitado = signal('');
  protected readonly deDigitado = signal('');
  protected readonly ateDigitado = signal('');

  protected readonly texto = signal('');
  protected readonly tipo = signal('');
  protected readonly de = signal('');
  protected readonly ate = signal('');

  protected readonly estado = signal<JobState | null>(null);
  protected readonly fila = signal<string | null>(null);
  protected readonly pagina = signal(1);

  protected readonly selecao = signal<ReadonlySet<string>>(new Set());
  protected readonly emMassa = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly resultadoMassa = signal<BulkResult | null>(null);

  protected readonly filas = resource({ loader: () => this.api.queues() });

  // request só com os filtros aplicados: mudar filtro re-consulta (mostra carregando);
  // o pulso do SSE recarrega via reload() preservando a lista (sem piscar).
  protected readonly jobs = resource({
    request: () => ({
      state: this.estado(),
      queue: this.fila(),
      page: this.pagina(),
      text: this.texto(),
      type: this.tipo(),
      from: this.de(),
      to: this.ate(),
    }),
    loader: ({ request }) =>
      this.api.searchJobs({
        state: request.state,
        queue: request.queue,
        page: request.page,
        pageSize: PAGE_SIZE,
        text: request.text || null,
        type: request.type || null,
        // O campo de data escolhe um dia inteiro no fuso local. O limite superior da
        // API é exclusivo, então o fim vira a meia-noite do dia seguinte — sem isso,
        // escolher hoje devolveria zero resultados.
        from: request.from ? new Date(`${request.from}T00:00:00`).toISOString() : null,
        to: request.to ? diaSeguinte(request.to) : null,
      }),
  });

  constructor() {
    effect(() => {
      this.sse.refresh();
      this.jobs.reload();
      this.filas.reload();
    });
  }

  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil((this.jobs.value()?.total ?? 0) / PAGE_SIZE)));

  protected readonly paginaToda = computed(() => {
    const itens = this.jobs.value()?.items ?? [];
    const selecionados = this.selecao();
    return itens.length > 0 && itens.every((job) => selecionados.has(job.id));
  });

  protected buscar(event: Event): void {
    event.preventDefault();
    this.texto.set(this.textoDigitado().trim());
    this.tipo.set(this.tipoDigitado().trim());
    this.de.set(this.deDigitado());
    this.ate.set(this.ateDigitado());
    this.pagina.set(1);
  }

  protected limpar(): void {
    for (const campo of [this.textoDigitado, this.tipoDigitado, this.deDigitado, this.ateDigitado,
      this.texto, this.tipo, this.de, this.ate]) {
      campo.set('');
    }

    this.estado.set(null);
    this.fila.set(null);
    this.pagina.set(1);
  }

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
    this.pagina.update((p) => Math.min(this.totalPaginas(), p + 1));
  }

  protected alternar(id: string): void {
    this.selecao.update((atual) => {
      const proxima = new Set(atual);
      if (!proxima.delete(id)) {
        proxima.add(id);
      }

      return proxima;
    });
  }

  protected alternarPagina(): void {
    const itens = this.jobs.value()?.items ?? [];
    const todaSelecionada = this.paginaToda();
    this.selecao.update((atual) => {
      const proxima = new Set(atual);
      for (const job of itens) {
        if (todaSelecionada) {
          proxima.delete(job.id);
        } else {
          proxima.add(job.id);
        }
      }

      return proxima;
    });
  }

  protected limparSelecao(): void {
    this.selecao.set(new Set());
    this.resultadoMassa.set(null);
  }

  protected retentarSelecionados(): Promise<void> {
    return this.aplicarEmMassa((ids) => this.api.bulkRetry(ids));
  }

  protected async excluirSelecionados(): Promise<void> {
    if (!confirm(this.i18n.t('confirmBulkRemove'))) {
      return;
    }

    await this.aplicarEmMassa((ids) => this.api.bulkDelete(ids));
  }

  private async aplicarEmMassa(acao: (ids: string[]) => Promise<BulkResult>): Promise<void> {
    // A seleção pode atravessar páginas; o teto da API vale para a chamada inteira.
    const ids = [...this.selecao()].slice(0, MAX_BULK_ITEMS);
    if (ids.length === 0) {
      return;
    }

    this.erro.set(null);
    this.emMassa.set(true);
    try {
      const resultado = await acao(ids);
      this.resultadoMassa.set(resultado);
      // Só os aplicados saem da seleção: o que falhou continua marcado para o
      // operador ver o motivo e decidir, em vez de sumir sem explicação.
      const falhos = new Set(resultado.failures.map((f) => f.jobId));
      this.selecao.update((atual) => new Set([...atual].filter((id) => falhos.has(id))));
      this.jobs.reload();
    } catch (falha) {
      this.erro.set(describeError(falha));
    } finally {
      this.emMassa.set(false);
    }
  }
}
