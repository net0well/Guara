import { Injectable, signal } from '@angular/core';

export type Lang = 'pt' | 'en';

type Dictionary = Record<string, { pt: string; en: string }>;

const STRINGS: Dictionary = {
  overview: { pt: 'Visão geral', en: 'Overview' },
  jobs: { pt: 'Jobs', en: 'Jobs' },
  recurring: { pt: 'Recorrentes', en: 'Recurring' },
  servers: { pt: 'Servidores', en: 'Servers' },
  total: { pt: 'Total', en: 'Total' },
  byState: { pt: 'Por estado', en: 'By state' },
  queues: { pt: 'Filas', en: 'Queues' },
  live: { pt: 'Ao vivo', en: 'Live' },
  reconnecting: { pt: 'Reconectando…', en: 'Reconnecting…' },
  filterState: { pt: 'Estado', en: 'State' },
  filterQueue: { pt: 'Fila', en: 'Queue' },
  all: { pt: 'Todos', en: 'All' },
  id: { pt: 'Id', en: 'Id' },
  type: { pt: 'Tipo', en: 'Type' },
  method: { pt: 'Método', en: 'Method' },
  state: { pt: 'Estado', en: 'State' },
  attempt: { pt: 'Tentativa', en: 'Attempt' },
  created: { pt: 'Criado', en: 'Created' },
  scheduled: { pt: 'Agendado', en: 'Scheduled' },
  finished: { pt: 'Concluído', en: 'Finished' },
  actions: { pt: 'Ações', en: 'Actions' },
  retry: { pt: 'Retentar', en: 'Retry' },
  trigger: { pt: 'Disparar', en: 'Trigger' },
  remove: { pt: 'Excluir', en: 'Delete' },
  detail: { pt: 'Detalhe', en: 'Detail' },
  back: { pt: 'Voltar', en: 'Back' },
  result: { pt: 'Resultado', en: 'Result' },
  error: { pt: 'Erro', en: 'Error' },
  metadata: { pt: 'Metadados', en: 'Metadata' },
  lease: { pt: 'Posse até', en: 'Lease until' },
  description: { pt: 'Descrição', en: 'Description' },
  schedule: { pt: 'Agenda', en: 'Schedule' },
  nextRun: { pt: 'Próximo', en: 'Next run' },
  lastRun: { pt: 'Último', en: 'Last run' },
  paused: { pt: 'Pausado', en: 'Paused' },
  timezone: { pt: 'Fuso', en: 'Time zone' },
  machine: { pt: 'Máquina', en: 'Machine' },
  heartbeat: { pt: 'Heartbeat', en: 'Heartbeat' },
  concurrency: { pt: 'Concorrência', en: 'Concurrency' },
  startedAt: { pt: 'Iniciado', en: 'Started' },
  empty: { pt: 'Nada por aqui.', en: 'Nothing here.' },
  loading: { pt: 'Carregando…', en: 'Loading…' },
  prev: { pt: 'Anterior', en: 'Previous' },
  next: { pt: 'Próxima', en: 'Next' },
  page: { pt: 'Página', en: 'Page' },
  confirmRemove: { pt: 'Excluir este job?', en: 'Delete this job?' },
  subtitle: { pt: 'Agendamento de jobs, made in Brasil.', en: 'Job scheduling, made in Brasil.' },
  none: { pt: '—', en: '—' },
  lastSkipped: { pt: 'Último pulo', en: 'Last skipped' },
  interval: { pt: 'Intervalo', en: 'Interval' },
  cron: { pt: 'Cron', en: 'Cron' },
};

/** Tradução reativa: <c>t(chave)</c> reavalia quando o idioma muda. */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private static readonly Key = 'guara.lang';

  private readonly _lang = signal<Lang>(this.load());
  readonly lang = this._lang.asReadonly();

  t = (key: keyof typeof STRINGS): string => STRINGS[key]?.[this._lang()] ?? String(key);

  toggle(): void {
    this._lang.update((current) => (current === 'pt' ? 'en' : 'pt'));
    localStorage.setItem(I18nService.Key, this._lang());
  }

  private load(): Lang {
    const stored = localStorage.getItem(I18nService.Key);
    if (stored === 'pt' || stored === 'en') {
      return stored;
    }

    return navigator.language.toLowerCase().startsWith('en') ? 'en' : 'pt';
  }
}
