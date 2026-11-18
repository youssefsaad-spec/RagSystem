import { HttpClient, HttpContext, HttpEventType, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { filter, map, Observable } from 'rxjs';
import { ApiError } from './api-error';
import { REQUEST_TIMEOUT_MS } from './api-context';
import {
  ApiVersion,
  ChunkView,
  DocumentView,
  IngestReport,
  readChunks,
  readDocument,
  readDocuments,
  readReport,
  UploadOutcome,
} from './models';

export type UploadEvent = { type: 'progress'; percent: number | null } | { type: 'done'; outcome: UploadOutcome };

@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);

  list(version: ApiVersion): Observable<DocumentView[]> {
    return this.http
      .get<unknown>('/api/documents', { params: new HttpParams().set('version', version) })
      .pipe(map(readDocuments));
  }

  get(version: ApiVersion, id: string): Observable<DocumentView> {
    return this.http
      .get<unknown>(`/api/documents/${encodeURIComponent(id)}`, { params: new HttpParams().set('version', version) })
      .pipe(map(readDocument));
  }

  chunks(version: ApiVersion, id: string, options: { skip: number; take: number }): Observable<ChunkView[]> {
    const params = new HttpParams().set('version', version).set('skip', options.skip).set('take', options.take);
    return this.http
      .get<unknown>(`/api/documents/${encodeURIComponent(id)}/chunks`, { params })
      .pipe(map(readChunks));
  }

  delete(version: ApiVersion, id: string): Observable<void> {
    return this.http.delete<void>(`/api/documents/${encodeURIComponent(id)}`, {
      params: new HttpParams().set('version', version),
    });
  }

  /**
   * Uploads one file. Emits progress, then `done`. A 422 (the document has errors) arrives as an ApiError of kind
   * `unprocessable`: read its report with {@link reportOf}.
   */
  upload(version: ApiVersion, file: File, dryRun: boolean): Observable<UploadEvent> {
    const form = new FormData();
    form.append('file', file, file.name);
    let params = new HttpParams().set('version', version);
    if (dryRun) params = params.set('dryRun', true);

    return this.http
      .post<unknown>('/api/documents', form, {
        params,
        observe: 'events',
        reportProgress: true,
        context: new HttpContext().set(REQUEST_TIMEOUT_MS, 120_000),
      })
      .pipe(
        filter((e) => e.type === HttpEventType.UploadProgress || e.type === HttpEventType.Response),
        map((e): UploadEvent => {
          if (e.type === HttpEventType.UploadProgress) {
            return { type: 'progress', percent: e.total ? Math.min(100, Math.round((e.loaded / e.total) * 100)) : null };
          }
          return { type: 'done', outcome: readUploadOutcome(e as HttpResponse<unknown>, dryRun) };
        }),
      );
  }
}

function readUploadOutcome(res: HttpResponse<unknown>, dryRun: boolean): UploadOutcome {
  if (dryRun) return { kind: 'dryRun', report: readReport(res.body) };
  const body = res.body as Record<string, unknown> | null;
  const id = body && typeof body['id'] === 'string' ? body['id'] : null;
  if (!id) throw ApiError.badResponse('the upload response has no document id');
  const status = typeof body?.['status'] === 'string' ? (body['status'] as string) : 'Processing';
  return { kind: 'accepted', id, status };
}

/** The validation report that comes with a 422, or null when the error carries none. */
export function reportOf(error: ApiError): IngestReport | null {
  if (error.status !== 422 || !error.body || typeof error.body !== 'object') return null;
  try {
    return readReport(error.body);
  } catch {
    return null;
  }
}
