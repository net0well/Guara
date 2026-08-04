import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';

import { I18nService } from '../core/i18n.service';
import { Series, SeriesPoint } from '../core/models';

// Sistema de coordenadas do desenho. O SVG escala por viewBox, então o gráfico
// acompanha a largura do cartão sem recalcular nada em JavaScript.
const LARGURA = 720;
const ALTURA = 180;
const MARGEM_ESQ = 44;
const MARGEM_DIR = 8;
const MARGEM_TOPO = 8;
const MARGEM_BASE = 22;
const AREA_LARGURA = LARGURA - MARGEM_ESQ - MARGEM_DIR;
const AREA_ALTURA = ALTURA - MARGEM_TOPO - MARGEM_BASE;

/** Barra empilhada de um balde, já em coordenadas do SVG. */
interface Barra {
  indice: number;
  x: number;
  largura: number;
  sucessoY: number;
  sucessoAltura: number;
  falhaY: number;
  falhaAltura: number;
}

interface Marca {
  valor: number;
  y: number;
}

/**
 * Vazão por balde: sucessos e falhas empilhados. São estados, não categorias
 * quaisquer, então usam as cores de estado do painel — e cada um aparece na legenda,
 * porque identidade nunca depende só de cor.
 */
@Component({
  selector: 'app-throughput-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="gr">
      <figcaption>
        <span class="titulo">{{ i18n.t('throughput') }}</span>
        <span class="legenda">
          <span class="chave"><i class="marca ok"></i>{{ i18n.t('succeeded') }}</span>
          <span class="chave"><i class="marca erro"></i>{{ i18n.t('failed') }}</span>
        </span>
      </figcaption>

      @if (vazio()) {
        <p class="vazio">{{ i18n.t('noData') }}</p>
      } @else {
        <svg
          [attr.viewBox]="'0 0 ' + L + ' ' + A"
          role="img"
          [attr.aria-label]="resumo()"
          (pointermove)="apontar($event)"
          (pointerleave)="focado.set(null)">
          <g class="eixo">
            @for (m of marcas(); track m.valor) {
              <line [attr.x1]="ME" [attr.x2]="L - MD" [attr.y1]="m.y" [attr.y2]="m.y" />
              <text [attr.x]="ME - 6" [attr.y]="m.y + 3.5" text-anchor="end">{{ m.valor }}</text>
            }
          </g>

          @for (b of barras(); track b.indice) {
            <g [class.focada]="focado() === b.indice">
              @if (b.sucessoAltura > 0) {
                <rect class="ok" [attr.x]="b.x" [attr.y]="b.sucessoY"
                      [attr.width]="b.largura" [attr.height]="b.sucessoAltura" rx="2" />
              }
              @if (b.falhaAltura > 0) {
                <rect class="erro" [attr.x]="b.x" [attr.y]="b.falhaY"
                      [attr.width]="b.largura" [attr.height]="b.falhaAltura" rx="2" />
              }
            </g>
          }

          <g class="rotulos-x">
            <text [attr.x]="ME" [attr.y]="A - 6">{{ inicio() }}</text>
            <text [attr.x]="L - MD" [attr.y]="A - 6" text-anchor="end">{{ fim() }}</text>
          </g>
        </svg>

        @if (pontoFocado(); as p) {
          <p class="dica" aria-live="polite">
            <span class="mono">{{ hora(p.timestamp) }}</span>
            · {{ i18n.t('succeeded') }} <strong>{{ p.succeeded }}</strong>
            · {{ i18n.t('failed') }} <strong>{{ p.failed }}</strong>
          </p>
        }
      }
    </figure>
  `,
  styleUrl: './series-charts.component.scss',
})
export class ThroughputChartComponent {
  protected readonly i18n = inject(I18nService);

  readonly series = input.required<Series | null | undefined>();

  protected readonly L = LARGURA;
  protected readonly A = ALTURA;
  protected readonly ME = MARGEM_ESQ;
  protected readonly MD = MARGEM_DIR;

  protected readonly focado = signal<number | null>(null);

  private readonly pontos = computed<SeriesPoint[]>(() => this.series()?.points ?? []);

  protected readonly vazio = computed(() => this.pontos().every((p) => p.total === 0));

  private readonly maximo = computed(() => Math.max(1, ...this.pontos().map((p) => p.total)));

  protected readonly marcas = computed<Marca[]>(() => {
    const max = this.maximo();
    return [0, Math.round(max / 2), max]
      .filter((valor, i, todos) => todos.indexOf(valor) === i)
      .map((valor) => ({ valor, y: MARGEM_TOPO + AREA_ALTURA - (valor / max) * AREA_ALTURA }));
  });

  protected readonly barras = computed<Barra[]>(() => {
    const pontos = this.pontos();
    const max = this.maximo();
    if (pontos.length === 0) {
      return [];
    }

    const passo = AREA_LARGURA / pontos.length;
    // 2px de respiro entre barras vizinhas; abaixo de 1px a barra sumiria.
    const largura = Math.max(1, passo - 2);
    const base = MARGEM_TOPO + AREA_ALTURA;

    return pontos.map((p, indice) => {
      const alturaSucesso = (p.succeeded / max) * AREA_ALTURA;
      const alturaFalha = (p.failed / max) * AREA_ALTURA;
      // A falha fica embaixo (ancorada na linha de base) e o sucesso empilha por cima,
      // com 2px de separação para os dois blocos não virarem um só.
      const falhaY = base - alturaFalha;
      const sucessoY = falhaY - alturaSucesso - (alturaFalha > 0 && alturaSucesso > 0 ? 2 : 0);

      return {
        indice,
        x: MARGEM_ESQ + indice * passo,
        largura,
        falhaY,
        falhaAltura: alturaFalha,
        sucessoY,
        sucessoAltura: alturaSucesso,
      };
    });
  });

  protected readonly pontoFocado = computed(() => {
    const indice = this.focado();
    return indice === null ? null : this.pontos()[indice] ?? null;
  });

  protected readonly inicio = computed(() => this.hora(this.pontos()[0]?.timestamp));

  protected readonly fim = computed(() => this.hora(this.pontos().at(-1)?.timestamp));

  protected readonly resumo = computed(() => {
    const pontos = this.pontos();
    const sucesso = pontos.reduce((soma, p) => soma + p.succeeded, 0);
    const falha = pontos.reduce((soma, p) => soma + p.failed, 0);
    return `${this.i18n.t('throughput')}: ${sucesso} ${this.i18n.t('succeeded')}, ${falha} ${this.i18n.t('failed')}`;
  });

  protected apontar(event: PointerEvent): void {
    this.focado.set(indiceSob(event, this.pontos().length));
  }

  protected hora(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) : '';
  }
}

/**
 * Latência p50 e p95 em milissegundos. É gráfico separado da vazão de propósito:
 * contagem e duração têm escalas diferentes, e um segundo eixo y no mesmo desenho
 * deixaria as duas curvas comparáveis só por acidente da escala escolhida.
 */
@Component({
  selector: 'app-latency-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="gr">
      <figcaption>
        <span class="titulo">{{ i18n.t('latency') }}</span>
        <span class="legenda">
          <span class="chave"><i class="marca p50"></i>p50</span>
          <span class="chave"><i class="marca p95"></i>p95</span>
        </span>
      </figcaption>

      @if (vazio()) {
        <p class="vazio">{{ i18n.t('noData') }}</p>
      } @else {
        <svg
          [attr.viewBox]="'0 0 ' + L + ' ' + A"
          role="img"
          [attr.aria-label]="resumo()"
          (pointermove)="apontar($event)"
          (pointerleave)="focado.set(null)">
          <g class="eixo">
            @for (m of marcas(); track m.valor) {
              <line [attr.x1]="ME" [attr.x2]="L - MD" [attr.y1]="m.y" [attr.y2]="m.y" />
              <text [attr.x]="ME - 6" [attr.y]="m.y + 3.5" text-anchor="end">{{ m.valor }}ms</text>
            }
          </g>

          <path class="linha p95" [attr.d]="caminhoP95()" />
          <path class="linha p50" [attr.d]="caminhoP50()" />

          @if (xFocado(); as x) {
            <line class="cursor" [attr.x1]="x" [attr.x2]="x" [attr.y1]="MT" [attr.y2]="A - MB" />
          }
        </svg>

        @if (pontoFocado(); as p) {
          <p class="dica" aria-live="polite">
            <span class="mono">{{ hora(p.timestamp) }}</span>
            · p50 <strong>{{ ms(p.latencyP50Ms) }}</strong>
            · p95 <strong>{{ ms(p.latencyP95Ms) }}</strong>
          </p>
        }
      }
    </figure>
  `,
  styleUrl: './series-charts.component.scss',
})
export class LatencyChartComponent {
  protected readonly i18n = inject(I18nService);

  readonly series = input.required<Series | null | undefined>();

  protected readonly L = LARGURA;
  protected readonly A = ALTURA;
  protected readonly ME = MARGEM_ESQ;
  protected readonly MD = MARGEM_DIR;
  protected readonly MT = MARGEM_TOPO;
  protected readonly MB = MARGEM_BASE;

  protected readonly focado = signal<number | null>(null);

  private readonly pontos = computed<SeriesPoint[]>(() => this.series()?.points ?? []);

  protected readonly vazio = computed(() => this.pontos().every((p) => p.latencyP95Ms == null));

  private readonly maximo = computed(() =>
    Math.max(1, ...this.pontos().map((p) => p.latencyP95Ms ?? 0)));

  protected readonly marcas = computed<Marca[]>(() => {
    const max = Math.ceil(this.maximo());
    return [0, Math.round(max / 2), max]
      .filter((valor, i, todos) => todos.indexOf(valor) === i)
      .map((valor) => ({ valor, y: MARGEM_TOPO + AREA_ALTURA - (valor / max) * AREA_ALTURA }));
  });

  protected readonly caminhoP50 = computed(() => this.caminho((p) => p.latencyP50Ms));

  protected readonly caminhoP95 = computed(() => this.caminho((p) => p.latencyP95Ms));

  protected readonly pontoFocado = computed(() => {
    const indice = this.focado();
    return indice === null ? null : this.pontos()[indice] ?? null;
  });

  protected readonly xFocado = computed(() => {
    const indice = this.focado();
    return indice === null ? null : this.x(indice, this.pontos().length);
  });

  protected readonly resumo = computed(() => {
    const p95 = Math.max(0, ...this.pontos().map((p) => p.latencyP95Ms ?? 0));
    return `${this.i18n.t('latency')} p95: ${Math.round(p95)}ms`;
  });

  protected apontar(event: PointerEvent): void {
    this.focado.set(indiceSob(event, this.pontos().length));
  }

  protected hora(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) : '';
  }

  protected ms(valor?: number | null): string {
    return valor == null ? '—' : `${Math.round(valor)}ms`;
  }

  /**
   * Baldes sem job finalizado não têm latência. A linha é cortada neles (novo M no
   * caminho) em vez de ligar os vizinhos: interpolar sobre o vazio desenharia uma
   * medição que não existiu.
   */
  private caminho(seletor: (p: SeriesPoint) => number | null | undefined): string {
    const pontos = this.pontos();
    const max = this.maximo();
    const base = MARGEM_TOPO + AREA_ALTURA;

    let d = '';
    let desenhando = false;
    pontos.forEach((p, indice) => {
      const valor = seletor(p);
      if (valor == null) {
        desenhando = false;
        return;
      }

      const x = this.x(indice, pontos.length);
      const y = base - (valor / max) * AREA_ALTURA;
      d += `${desenhando ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)} `;
      desenhando = true;
    });

    return d.trim();
  }

  private x(indice: number, total: number): number {
    const passo = AREA_LARGURA / Math.max(1, total);
    return MARGEM_ESQ + indice * passo + passo / 2;
  }
}

/** Converte a posição do ponteiro no índice do balde sob ele. */
function indiceSob(event: PointerEvent, total: number): number | null {
  if (total === 0) {
    return null;
  }

  const alvo = event.currentTarget as SVGSVGElement;
  const caixa = alvo.getBoundingClientRect();
  // A caixa está em pixels de tela e o desenho em unidades do viewBox: a razão
  // converte uma na outra sem depender do tamanho renderizado.
  const escala = LARGURA / caixa.width;
  const x = (event.clientX - caixa.left) * escala - MARGEM_ESQ;
  if (x < 0 || x > AREA_LARGURA) {
    return null;
  }

  return Math.min(total - 1, Math.floor((x / AREA_LARGURA) * total));
}
