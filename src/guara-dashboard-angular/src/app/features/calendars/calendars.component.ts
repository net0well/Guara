import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';

import { GuaraApi } from '../../core/guara-api';
import { I18nService } from '../../core/i18n.service';
import { CalendarDetail, CalendarRange, DAYS_OF_WEEK } from '../../core/models';
import { describeError } from '../../core/problem-details.interceptor';
import { InstantPipe } from '../../shared/instant.pipe';

/** Uma célula do calendário mensal. */
interface Dia {
  /** Chave no formato yyyy-MM-dd, igual ao que a API troca. */
  iso: string;
  numero: number;
  doMes: boolean;
  excluido: boolean;
  /** Excluído por regra de intervalo ou dia da semana, e não por data avulsa. */
  porRegra: boolean;
}

/** Rascunho editável do calendário aberto. */
interface Rascunho {
  dates: string[];
  ranges: CalendarRange[];
  daysOfWeek: string[];
  cronWindows: string[];
}

/** Gestão de calendários de exclusão: visão mensal clicável e as regras em lista. */
@Component({
  selector: 'app-calendars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InstantPipe],
  template: `
    @if (erro()) {
      <p class="erro-box" role="alert">{{ erro() }}</p>
    }

    <div class="calendarios">
      <aside class="card">
        <h2>{{ i18n.t('calendars') }}</h2>
        <ul class="lista-calendarios">
          @for (c of calendarios.value() ?? []; track c.name) {
            <li>
              <button
                type="button"
                class="item"
                [class.ativo]="selecionado() === c.name"
                [attr.aria-current]="selecionado() === c.name"
                (click)="selecionar(c.name)">
                <span class="mono">{{ c.name }}</span>
                <span class="suave">{{ c.ruleCount }} {{ i18n.t('rules') }}</span>
              </button>
            </li>
          } @empty {
            <li class="vazio">{{ calendarios.isLoading() ? i18n.t('loading') : i18n.t('empty') }}</li>
          }
        </ul>

        <form class="novo" (submit)="criar($event)">
          <label>
            {{ i18n.t('newCalendar') }}
            <input
              class="mono"
              [value]="nomeNovo()"
              (input)="nomeNovo.set($any($event.target).value)"
              [attr.placeholder]="i18n.t('calendarName')" />
          </label>
          <button type="submit" class="primaria" [disabled]="!nomeNovo().trim()">{{ i18n.t('save') }}</button>
        </form>
      </aside>

      <section class="card">
        @if (!selecionado()) {
          <p class="vazio">{{ i18n.t('noCalendarSelected') }}</p>
        } @else if (detalhe.error()) {
          <p class="erro-box" role="alert">{{ describe(detalhe.error()) }}</p>
        } @else {
          @if (rascunho(); as draft) {
          <header class="cabecalho-calendario">
            <h2 class="mono">{{ selecionado() }}</h2>
            <div class="acoes">
              <button type="button" class="primaria" [disabled]="salvando()" (click)="salvar()">
                {{ i18n.t('save') }}
              </button>
              <button type="button" class="perigo" [disabled]="salvando()" (click)="excluir()">
                {{ i18n.t('remove') }}
              </button>
            </div>
          </header>

          <div class="mes">
            <div class="toolbar">
              <button type="button" (click)="mudarMes(-1)" aria-label="mês anterior">‹</button>
              <strong>{{ rotuloMes() }}</strong>
              <button type="button" (click)="mudarMes(1)" aria-label="próximo mês">›</button>
              <span class="espaco"></span>
              <span class="suave">{{ i18n.t('clickDayToToggle') }}</span>
            </div>

            <div class="grade-semana" aria-hidden="true">
              @for (d of diasCurtos; track d) { <span>{{ d }}</span> }
            </div>

            <div class="grade-dias" role="grid" [attr.aria-label]="i18n.t('month')">
              @for (dia of dias(); track dia.iso) {
                <button
                  type="button"
                  role="gridcell"
                  class="dia"
                  [class.fora]="!dia.doMes"
                  [class.excluido]="dia.excluido"
                  [class.por-regra]="dia.porRegra"
                  [disabled]="dia.porRegra"
                  [attr.aria-pressed]="dia.excluido"
                  [attr.aria-label]="dia.iso"
                  (click)="alternarData(dia)">
                  {{ dia.numero }}
                </button>
              }
            </div>
          </div>

          <div class="regras">
            <div>
              <h3>{{ i18n.t('excludedDays') }}</h3>
              <div class="chips">
                @for (d of diasDaSemana; track d) {
                  <label class="chip">
                    <input
                      type="checkbox"
                      [checked]="draft.daysOfWeek.includes(d)"
                      (change)="alternarDiaDaSemana(d)" />
                    {{ d }}
                  </label>
                }
              </div>
            </div>

            <div>
              <h3>{{ i18n.t('excludedRanges') }}</h3>
              <ul class="lista-regras">
                @for (r of draft.ranges; track r.start + r.end; let i = $index) {
                  <li>
                    <span class="mono">{{ r.start }} → {{ r.end }}</span>
                    <button type="button" (click)="removerIntervalo(i)" aria-label="remover">×</button>
                  </li>
                } @empty {
                  <li class="suave">{{ i18n.t('none') }}</li>
                }
              </ul>
              <form class="linha-regra" (submit)="adicionarIntervalo($event)">
                <input type="date" [value]="novoInicio()" (input)="novoInicio.set($any($event.target).value)" />
                <input type="date" [value]="novoFim()" (input)="novoFim.set($any($event.target).value)" />
                <button type="submit" [disabled]="!novoInicio() || !novoFim()">{{ i18n.t('addRange') }}</button>
              </form>
            </div>

            <div>
              <h3>{{ i18n.t('excludedCron') }}</h3>
              <ul class="lista-regras">
                @for (c of draft.cronWindows; track c; let i = $index) {
                  <li>
                    <span class="mono">{{ c }}</span>
                    <button type="button" (click)="removerCron(i)" aria-label="remover">×</button>
                  </li>
                } @empty {
                  <li class="suave">{{ i18n.t('none') }}</li>
                }
              </ul>
              <form class="linha-regra" (submit)="adicionarCron($event)">
                <input
                  class="mono"
                  [value]="novoCron()"
                  (input)="novoCron.set($any($event.target).value)"
                  placeholder="0 0-7 * * *" />
                <button type="submit" [disabled]="!novoCron().trim()">{{ i18n.t('addCron') }}</button>
              </form>
            </div>

            <div>
              <h3>{{ i18n.t('usedBy') }}</h3>
              <ul class="lista-regras">
                @for (u of detalhe.value()?.usedBy ?? []; track u.recurringId) {
                  <li>
                    <span class="mono">{{ u.recurringId }}</span>
                    <span class="suave">{{ i18n.t('nextRun') }}: {{ u.nextRunAt | instant }}</span>
                  </li>
                } @empty {
                  <li class="suave">{{ i18n.t('none') }}</li>
                }
              </ul>
            </div>
          </div>
          }
        }
      </section>
    </div>
  `,
  styles: [`
    .calendarios { display: grid; gap: 1rem; grid-template-columns: minmax(200px, 260px) 1fr; align-items: start; }
    @media (max-width: 820px) { .calendarios { grid-template-columns: 1fr; } }

    h2 { margin: 0 0 0.75rem; font-size: 1.05rem; }
    h3 { margin: 0 0 0.4rem; font-size: 0.82rem; text-transform: uppercase; letter-spacing: 0.03em; color: var(--suave); }

    .lista-calendarios { list-style: none; margin: 0 0 1rem; padding: 0; display: grid; gap: 0.25rem; }
    .item { display: flex; justify-content: space-between; gap: 0.5rem; width: 100%; text-align: left; }
    .item.ativo { border-color: var(--marca); color: var(--marca); }

    .novo { display: grid; gap: 0.5rem; }
    .novo label { display: grid; gap: 0.25rem; font-size: 0.85rem; color: var(--suave); }

    .cabecalho-calendario { display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .cabecalho-calendario h2 { margin: 0; flex: 1; }

    .mes { margin: 1rem 0 1.25rem; }
    .grade-semana, .grade-dias { display: grid; grid-template-columns: repeat(7, 1fr); gap: 0.25rem; }
    .grade-semana span { text-align: center; font-size: 0.72rem; color: var(--suave); text-transform: uppercase; }
    .dia { padding: 0.5rem 0; text-align: center; }
    .dia.fora { opacity: 0.35; }
    /* Excluído por data avulsa: clicável, é o que a visão mensal edita. */
    .dia.excluido { border-color: var(--erro); color: var(--erro); background: color-mix(in srgb, var(--erro) 12%, transparent); }
    /* Excluído por intervalo ou dia da semana: some o cursor, a regra se edita na lista. */
    .dia.por-regra { border-style: dashed; opacity: 0.75; }

    .regras { display: grid; gap: 1.25rem; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); }
    .chips { display: flex; flex-wrap: wrap; gap: 0.4rem; }
    .chip { display: inline-flex; align-items: center; gap: 0.3rem; font-size: 0.82rem;
            border: 1px solid var(--borda); border-radius: 999px; padding: 0.2rem 0.6rem; }
    .lista-regras { list-style: none; margin: 0 0 0.5rem; padding: 0; display: grid; gap: 0.3rem; font-size: 0.88rem; }
    .lista-regras li { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; }
    .linha-regra { display: flex; flex-wrap: wrap; gap: 0.35rem; }
  `],
})
export class CalendarsComponent {
  private readonly api = inject(GuaraApi);
  protected readonly i18n = inject(I18nService);
  protected readonly describe = describeError;
  protected readonly diasDaSemana = DAYS_OF_WEEK;
  protected readonly diasCurtos = ['D', 'S', 'T', 'Q', 'Q', 'S', 'S'];

  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected readonly selecionado = signal<string | null>(null);
  protected readonly nomeNovo = signal('');
  protected readonly rascunho = signal<Rascunho | null>(null);

  protected readonly novoInicio = signal('');
  protected readonly novoFim = signal('');
  protected readonly novoCron = signal('');

  /** Primeiro dia do mês exibido na grade. */
  private readonly mes = signal(inicioDoMes(new Date()));

  protected readonly calendarios = resource({ loader: () => this.api.calendars() });

  protected readonly detalhe = resource({
    request: () => this.selecionado(),
    loader: async ({ request }) => {
      if (!request) {
        return null;
      }

      const detail = await this.api.calendar(request);
      this.rascunho.set(paraRascunho(detail));
      return detail;
    },
  });

  protected readonly rotuloMes = computed(() =>
    this.mes().toLocaleDateString(this.i18n.lang() === 'pt' ? 'pt-BR' : 'en-US', {
      month: 'long',
      year: 'numeric',
    }));

  /** Grade completa de 6 semanas: o mês nunca muda de altura ao navegar. */
  protected readonly dias = computed<Dia[]>(() => {
    const draft = this.rascunho();
    const primeiro = this.mes();
    const inicioGrade = new Date(primeiro);
    inicioGrade.setDate(1 - primeiro.getDay());

    const celulas: Dia[] = [];
    for (let i = 0; i < 42; i++) {
      const data = new Date(inicioGrade);
      data.setDate(inicioGrade.getDate() + i);
      const iso = paraIso(data);
      const porRegra = !!draft && (cobertoPorIntervalo(draft, iso)
        || draft.daysOfWeek.includes(DAYS_OF_WEEK[data.getDay()]));

      celulas.push({
        iso,
        numero: data.getDate(),
        doMes: data.getMonth() === primeiro.getMonth(),
        excluido: porRegra || !!draft?.dates.includes(iso),
        porRegra,
      });
    }

    return celulas;
  });

  protected selecionar(nome: string): void {
    this.erro.set(null);
    this.selecionado.set(nome);
  }

  protected mudarMes(delta: number): void {
    this.mes.update((atual) => new Date(atual.getFullYear(), atual.getMonth() + delta, 1));
  }

  protected alternarData(dia: Dia): void {
    this.rascunho.update((draft) => {
      if (!draft) {
        return draft;
      }

      const dates = draft.dates.includes(dia.iso)
        ? draft.dates.filter((d) => d !== dia.iso)
        : [...draft.dates, dia.iso].sort();
      return { ...draft, dates };
    });
  }

  protected alternarDiaDaSemana(dia: string): void {
    this.rascunho.update((draft) => {
      if (!draft) {
        return draft;
      }

      const daysOfWeek = draft.daysOfWeek.includes(dia)
        ? draft.daysOfWeek.filter((d) => d !== dia)
        : [...draft.daysOfWeek, dia];
      return { ...draft, daysOfWeek };
    });
  }

  protected adicionarIntervalo(event: Event): void {
    event.preventDefault();
    const start = this.novoInicio();
    const end = this.novoFim();
    if (!start || !end) {
      return;
    }

    // Pontas trocadas são normalizadas aqui: a API trata o intervalo como inclusivo
    // nas duas pontas e recusaria fim antes do início.
    const [inicio, fim] = start <= end ? [start, end] : [end, start];
    this.rascunho.update((draft) =>
      draft ? { ...draft, ranges: [...draft.ranges, { start: inicio, end: fim }] } : draft);
    this.novoInicio.set('');
    this.novoFim.set('');
  }

  protected removerIntervalo(indice: number): void {
    this.rascunho.update((draft) =>
      draft ? { ...draft, ranges: draft.ranges.filter((_, i) => i !== indice) } : draft);
  }

  protected adicionarCron(event: Event): void {
    event.preventDefault();
    const cron = this.novoCron().trim();
    if (!cron) {
      return;
    }

    this.rascunho.update((draft) =>
      draft ? { ...draft, cronWindows: [...draft.cronWindows, cron] } : draft);
    this.novoCron.set('');
  }

  protected removerCron(indice: number): void {
    this.rascunho.update((draft) =>
      draft ? { ...draft, cronWindows: draft.cronWindows.filter((_, i) => i !== indice) } : draft);
  }

  protected async criar(event: Event): Promise<void> {
    event.preventDefault();
    const nome = this.nomeNovo().trim();
    if (!nome) {
      return;
    }

    await this.executar(async () => {
      await this.api.saveCalendar(nome, {});
      this.nomeNovo.set('');
      this.calendarios.reload();
      this.selecionado.set(nome);
    });
  }

  protected async salvar(): Promise<void> {
    const nome = this.selecionado();
    const draft = this.rascunho();
    if (!nome || !draft) {
      return;
    }

    await this.executar(async () => {
      await this.api.saveCalendar(nome, draft);
      // Recarrega para trazer o efeito já recalculado nos recorrentes que usam.
      this.detalhe.reload();
      this.calendarios.reload();
    });
  }

  protected async excluir(): Promise<void> {
    const nome = this.selecionado();
    if (!nome || !confirm(this.i18n.t('confirmRemoveCalendar'))) {
      return;
    }

    await this.executar(async () => {
      await this.api.deleteCalendar(nome);
      this.selecionado.set(null);
      this.rascunho.set(null);
      this.calendarios.reload();
    });
  }

  private async executar(acao: () => Promise<void>): Promise<void> {
    this.erro.set(null);
    this.salvando.set(true);
    try {
      await acao();
    } catch (falha) {
      this.erro.set(describeError(falha));
    } finally {
      this.salvando.set(false);
    }
  }
}

function inicioDoMes(data: Date): Date {
  return new Date(data.getFullYear(), data.getMonth(), 1);
}

/** Formata no fuso local, e não em UTC: a grade mostra o dia que o operador vê. */
function paraIso(data: Date): string {
  const mes = String(data.getMonth() + 1).padStart(2, '0');
  const dia = String(data.getDate()).padStart(2, '0');
  return `${data.getFullYear()}-${mes}-${dia}`;
}

function cobertoPorIntervalo(draft: Rascunho, iso: string): boolean {
  // Comparação textual funciona porque yyyy-MM-dd ordena como a data.
  return draft.ranges.some((r) => r.start <= iso && iso <= r.end);
}

function paraRascunho(detail: CalendarDetail): Rascunho {
  return {
    dates: [...detail.dates].sort(),
    ranges: [...detail.ranges],
    daysOfWeek: [...detail.daysOfWeek],
    cronWindows: [...detail.cronWindows],
  };
}
