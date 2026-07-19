import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/** Formato RFC 9457 devolvido pela API em erro. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

/** Mensagem amigável a partir de um erro HTTP (ProblemDetails quando houver). */
export function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ProblemDetails | string | null;
    if (problem && typeof problem === 'object') {
      return problem.detail || problem.title || `Erro ${error.status}`;
    }

    return error.status === 0 ? 'Sem conexão com a API.' : `Erro ${error.status}`;
  }

  return 'Erro inesperado.';
}

/**
 * Sessão expirada (401) leva de volta ao login do dashboard; os demais erros seguem
 * para quem chamou tratar (a UI mostra a mensagem, nunca uma tela branca).
 */
export const problemDetailsInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((error: HttpErrorResponse) => {
    if (error.status === 401) {
      // Relativo ao <base href>: respeita o BasePath real do dashboard.
      window.location.assign('login');
    }

    return throwError(() => error);
  }));
