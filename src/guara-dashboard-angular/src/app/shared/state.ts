import { JobState } from '../core/models';

/** Variável CSS de cor para cada estado (usada em badges e no gráfico SVG). */
export function stateColorVar(state: string): string {
  const map: Record<string, string> = {
    Succeeded: 'var(--ok)',
    Failed: 'var(--erro)',
    Processing: 'var(--info)',
    Enqueued: 'var(--marca)',
    Scheduled: 'var(--roxo)',
    Retrying: 'var(--aviso)',
    Created: 'var(--suave)',
  };
  return map[state] ?? 'var(--suave)';
}

/** Classe CSS do badge de estado. */
export function stateBadgeClass(state: JobState | string): string {
  return `badge estado-${state}`;
}
