import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError, timeout, TimeoutError } from 'rxjs';
import { ApiError } from './api-error';
import { REQUEST_TIMEOUT_MS } from './api-context';

export function toApiError(err: unknown, timeoutMs: number): ApiError {
  if (err instanceof ApiError) return err;
  if (err instanceof TimeoutError) return ApiError.timeout(Math.round(timeoutMs / 1000));
  if (err instanceof HttpErrorResponse) return ApiError.fromHttp(err);
  return ApiError.unknown(err);
}

/** For every /api call: applies the timeout and turns every failure into an ApiError. */
export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);

  const timeoutMs = req.context.get(REQUEST_TIMEOUT_MS);
  return next(req).pipe(
    timeout({ each: timeoutMs }),
    catchError((err: unknown) => throwError(() => toApiError(err, timeoutMs))),
  );
};
