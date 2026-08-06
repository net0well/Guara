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
  roles: { pt: 'Papéis', en: 'Roles' },
  roleRecurring: { pt: 'Recorrentes', en: 'Recurring' },
  roleMaintenance: { pt: 'Manutenção', en: 'Maintenance' },
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

  calendars: { pt: 'Calendários', en: 'Calendars' },
  calendar: { pt: 'Calendário', en: 'Calendar' },
  newCalendar: { pt: 'Novo calendário', en: 'New calendar' },
  calendarName: { pt: 'Nome do calendário', en: 'Calendar name' },
  rules: { pt: 'Regras', en: 'Rules' },
  usedBy: { pt: 'Usado por', en: 'Used by' },
  excludedDates: { pt: 'Datas excluídas', en: 'Excluded dates' },
  excludedRanges: { pt: 'Intervalos excluídos', en: 'Excluded ranges' },
  excludedDays: { pt: 'Dias da semana excluídos', en: 'Excluded weekdays' },
  excludedCron: { pt: 'Janelas cron excluídas', en: 'Excluded cron windows' },
  addDate: { pt: 'Excluir data', en: 'Exclude date' },
  addRange: { pt: 'Excluir intervalo', en: 'Exclude range' },
  addCron: { pt: 'Excluir janela cron', en: 'Exclude cron window' },
  save: { pt: 'Salvar', en: 'Save' },
  cancel: { pt: 'Cancelar', en: 'Cancel' },
  edit: { pt: 'Editar', en: 'Edit' },
  confirmRemoveCalendar: { pt: 'Excluir este calendário?', en: 'Delete this calendar?' },
  noCalendarSelected: { pt: 'Escolha um calendário para ver as exclusões.', en: 'Pick a calendar to see its exclusions.' },
  month: { pt: 'Mês', en: 'Month' },
  clickDayToToggle: { pt: 'Clique num dia para excluir ou liberar.', en: 'Click a day to exclude or allow it.' },

  pause: { pt: 'Pausar', en: 'Pause' },
  resume: { pt: 'Retomar', en: 'Resume' },
  triggerNow: { pt: 'Disparar agora', en: 'Trigger now' },
  editSchedule: { pt: 'Editar agenda', en: 'Edit schedule' },
  active: { pt: 'Ativo', en: 'Active' },
  resumeNoBackfill: {
    pt: 'Retomar não recupera o período pausado.',
    en: 'Resuming does not backfill the paused period.',
  },

  charts: { pt: 'Gráficos', en: 'Charts' },
  throughput: { pt: 'Vazão', en: 'Throughput' },
  latency: { pt: 'Latência', en: 'Latency' },
  succeeded: { pt: 'Sucesso', en: 'Succeeded' },
  failed: { pt: 'Falha', en: 'Failed' },
  window: { pt: 'Janela', en: 'Window' },
  noData: { pt: 'Sem dados no período.', en: 'No data in this period.' },
  seriesTruncated: {
    pt: 'A retenção limita o histórico: janelas longas podem vir incompletas.',
    en: 'Retention caps history: long windows may come back partial.',
  },

  search: { pt: 'Buscar', en: 'Search' },
  searchPlaceholder: { pt: 'id, tipo ou método', en: 'id, type or method' },
  filterType: { pt: 'Tipo', en: 'Type' },
  from: { pt: 'De', en: 'From' },
  to: { pt: 'Até', en: 'To' },
  clearFilters: { pt: 'Limpar', en: 'Clear' },
  results: { pt: 'resultados', en: 'results' },
  selected: { pt: 'selecionados', en: 'selected' },
  selectAll: { pt: 'Selecionar tudo', en: 'Select all' },
  clearSelection: { pt: 'Limpar seleção', en: 'Clear selection' },
  bulkRetry: { pt: 'Retentar selecionados', en: 'Retry selected' },
  bulkRemove: { pt: 'Excluir selecionados', en: 'Delete selected' },
  confirmBulkRemove: { pt: 'Excluir os jobs selecionados?', en: 'Delete the selected jobs?' },
  bulkOutcome: { pt: 'de', en: 'of' },
  bulkApplied: { pt: 'aplicados', en: 'applied' },
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
