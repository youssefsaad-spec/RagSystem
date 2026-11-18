import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { REQUEST_TIMEOUT_MS } from './api-context';
import { ApiVersion, readSearch, SearchResult } from './models';

@Injectable({ providedIn: 'root' })
export class SearchApi {
  private readonly http = inject(HttpClient);

  /** What retrieval finds for a question, without calling a chat model. */
  search(version: ApiVersion, question: string): Observable<SearchResult> {
    return this.http
      .get<unknown>(`/api/${version}/search`, {
        params: new HttpParams().set('q', question),
        context: new HttpContext().set(REQUEST_TIMEOUT_MS, 60_000),
      })
      .pipe(map(readSearch));
  }
}
