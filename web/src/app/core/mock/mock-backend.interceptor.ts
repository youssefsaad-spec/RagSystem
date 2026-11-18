import {
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
  HttpHeaders,
  HttpInterceptorFn,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { inject, InjectionToken } from '@angular/core';
import { concat, EMPTY, from, mergeMap, Observable, of, switchMap, throwError, timer } from 'rxjs';
import { localStore } from '../util/safe-storage';
import { MockBackend, MockReply, MockRequest, MockState, MockStorage } from './mock-backend';

const STORAGE_KEY = 'ragsystem.mock.v1';

/** Keeps the mock's documents and conversations in this browser, so a reload does not forget them. */
const browserStorage: MockStorage = {
  load(): MockState | null {
    try {
      const parsed = JSON.parse(localStore.get(STORAGE_KEY) ?? 'null') as MockState | null;
      return parsed && Array.isArray(parsed.documents) && Array.isArray(parsed.chunks) && Array.isArray(parsed.conversations) ? parsed : null;
    } catch {
      return null;
    }
  },
  save(state: MockState): void {
    localStore.set(STORAGE_KEY, JSON.stringify(state));
  },
};

/** The one mock backend of the app. Tests provide their own (in memory, without delays). */
export const MOCK_BACKEND = new InjectionToken<MockBackend>('MOCK_BACKEND', {
  providedIn: 'root',
  factory: () => new MockBackend({ storage: browserStorage }),
});

/** Back to the starting documents, forgets the conversation list of this browser, and reloads the page. */
export function resetMockData(backend: MockBackend): void {
  backend.reset();
  localStore.remove('ragsystem.conversations');
  window.location.reload();
}

/** File.text() is missing in some browsers and test environments; FileReader is everywhere. */
function readText(file: Blob): Promise<string> {
  if (typeof file.text === 'function') return file.text();
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(file);
  });
}

function toRequest(req: HttpRequest<unknown>, text: string | null): MockRequest {
  const url = new URL(req.url, 'http://mock.local');
  const query: Record<string, string> = {};
  url.searchParams.forEach((value, key) => (query[key] = value));
  for (const key of req.params.keys()) query[key] = req.params.get(key) ?? '';

  if (req.body instanceof FormData) {
    const entry = req.body.get('file') ?? [...req.body.values()].find((v) => v instanceof File) ?? null;
    const file = entry instanceof File ? entry : null;
    return { method: req.method, path: url.pathname, query, multipart: true, file: file ? { name: file.name, size: file.size, text: text ?? '' } : null };
  }
  return { method: req.method, path: url.pathname, query, body: req.body };
}

function toEvents(req: HttpRequest<unknown>, reply: MockReply): Observable<HttpEvent<unknown>> {
  const headers = new HttpHeaders(reply.headers ?? {});
  if (reply.status === 0) {
    return throwError(() => new HttpErrorResponse({ status: 0, url: req.url, error: new ProgressEvent('error') }));
  }
  if (reply.status >= 400) {
    return throwError(() => new HttpErrorResponse({ status: reply.status, statusText: 'Mock', url: req.url, headers, error: reply.body ?? null }));
  }
  return of(new HttpResponse({ status: reply.status, url: req.url, headers, body: reply.body ?? null }));
}

/**
 * Answers every /api call from the in-memory mock backend instead of the network. Registered after the app's own API
 * interceptor, so timeouts and the ApiError mapping run exactly as they do against the real API.
 */
export const mockBackendInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);
  const backend = inject(MOCK_BACKEND);

  const file = req.body instanceof FormData ? (req.body.get('file') as Blob | null) : null;
  return (file instanceof Blob ? from(readText(file)) : of<string | null>(null)).pipe(
    switchMap((text) => {
      const reply = backend.handle(toRequest(req, text));
      // A browser reports upload progress before the answer arrives.
      const progress: Observable<HttpEvent<unknown>> =
        req.reportProgress && file instanceof Blob
          ? of({ type: HttpEventType.UploadProgress, loaded: file.size, total: file.size } as HttpEvent<unknown>)
          : EMPTY;
      return timer(reply.delayMs).pipe(mergeMap(() => concat(progress, toEvents(req, reply))));
    }),
  );
};
