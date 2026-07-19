import { Pipe, PipeTransform } from '@angular/core';

/** Formata um instante ISO (UTC) para data-hora local legível; vazio vira travessão. */
@Pipe({ name: 'instant' })
export class InstantPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) {
      return '—';
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleString(undefined, {
          year: 'numeric', month: '2-digit', day: '2-digit',
          hour: '2-digit', minute: '2-digit', second: '2-digit',
        });
  }
}
