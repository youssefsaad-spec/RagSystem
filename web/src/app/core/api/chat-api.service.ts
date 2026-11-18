import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { REQUEST_TIMEOUT_MS } from './api-context';
import { ApiVersion, ChatResponse, HistoryMessage, readChatResponse, readHistory } from './models';

/**
 * The server waits up to 60 s for the model, plus retrieval and the rewrite step; this is a little longer, so the
 * server's own answer (and its error) arrives before the browser gives up.
 */
const CHAT_TIMEOUT_MS = 110_000;

@Injectable({ providedIn: 'root' })
export class ChatApi {
  private readonly http = inject(HttpClient);

  /** Cancelling the subscription aborts the request in the browser (the server may still finish it). */
  ask(version: ApiVersion, conversationId: string | null, message: string): Observable<ChatResponse> {
    const body: { message: string; conversationId?: string } = { message };
    if (conversationId) body.conversationId = conversationId;
    return this.http
      .post<unknown>(`/api/${version}/chat`, body, { context: new HttpContext().set(REQUEST_TIMEOUT_MS, CHAT_TIMEOUT_MS) })
      .pipe(map(readChatResponse));
  }

  messages(version: ApiVersion, conversationId: string): Observable<HistoryMessage[]> {
    return this.http
      .get<unknown>(`/api/${version}/conversations/${encodeURIComponent(conversationId)}/messages`)
      .pipe(map(readHistory));
  }
}
