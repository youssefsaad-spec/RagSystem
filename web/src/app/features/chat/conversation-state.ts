import { signal, WritableSignal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import { ChatApi } from '../../core/api/chat-api.service';
import { ApiVersion, ChatResponse, ChatUsage, Citation, HistoryMessage, MAX_QUESTION_CHARS } from '../../core/api/models';
import { ToastService } from '../../core/ui/toast.service';
import { truncate } from '../../core/util/format';
import { localId } from '../../core/util/ids';
import { sessionStore } from '../../core/util/safe-storage';
import { ConversationIndex } from './conversation-index.service';

export interface ChatMessage {
  localId: string;
  role: 'user' | 'assistant';
  content: string;
  createdAt: number;
  citations: Citation[];
  usage: ChatUsage | null;
  /** `pending` and `failed` only ever apply to the user's own messages. */
  status: 'sent' | 'pending' | 'failed';
  error: ApiError | null;
}

export type LoadState = 'idle' | 'loading' | 'ready' | 'error';

export interface ChatEnv {
  api: ChatApi;
  index: ConversationIndex;
  toast: ToastService;
  /** A new conversation received its id from the server. */
  idAssigned: (state: ConversationState) => void;
  /** An answer arrived (also for a conversation the user is no longer looking at). */
  answered: (state: ConversationState) => void;
}

const STALE_AFTER_MS = 30_000;

/**
 * Everything about one conversation. It lives in a store, not in a page, so a question keeps going if the user opens
 * another conversation meanwhile, and a failed message is never lost on navigation.
 */
export class ConversationState {
  readonly id: WritableSignal<string | null>;
  readonly messages = signal<ChatMessage[]>([]);
  readonly loadState: WritableSignal<LoadState>;
  readonly loadError = signal<ApiError | null>(null);
  readonly sending = signal(false);
  /** Seconds the current question has been waiting, for the "still working" hint. */
  readonly waitingSec = signal(0);
  /** Seconds left before the model service should accept another question (from Retry-After). 0 = free. */
  readonly cooldownLeft = signal(0);
  readonly draft: WritableSignal<string>;
  /** The first question, shown as the conversation's title. */
  title(): string {
    const first = this.messages().find((m) => m.role === 'user');
    return first ? truncate(first.content, 120) : 'New conversation';
  }

  private sub: Subscription | null = null;
  private loadSub: Subscription | null = null;
  private waitTimer: ReturnType<typeof setInterval> | undefined;
  private cooldownTimer: ReturnType<typeof setInterval> | undefined;
  private loadedAt = 0;
  /** The user message whose answer is on its way, so Stop and a failure know which one to mark. */
  private activeUserId: string | null = null;
  /** Bumped whenever a send starts or ends, so a history load that raced with it can be discarded. */
  private mutations = 0;

  constructor(
    readonly version: ApiVersion,
    id: string | null,
    private readonly env: ChatEnv,
  ) {
    // Signals are created with their first value, never written here: a state is created while a page's
    // computed() runs, and Angular forbids writing signals there.
    this.id = signal(id);
    this.loadState = signal<LoadState>(id === null ? 'ready' : 'idle');
    this.draft = signal(sessionStore.get(this.draftKey()) || '');
  }

  private draftKey(): string {
    return `ragsystem.draft.${this.version}.${this.id() ?? 'new'}`;
  }

  // ---- Draft ---------------------------------------------------------------------------------------------------

  setDraft(text: string): void {
    this.draft.set(text);
    const key = this.draftKey();
    if (text) sessionStore.set(key, text);
    else sessionStore.remove(key);
  }

  // ---- Loading history -----------------------------------------------------------------------------------------

  /** Loads the messages of an existing conversation. Cheap to call again: a recent result is reused. */
  load(force = false): void {
    const id = this.id();
    if (!id || this.loadState() === 'loading' || this.sending()) return;
    if (!force && this.loadState() === 'ready' && Date.now() - this.loadedAt < STALE_AFTER_MS) return;

    const firstLoad = this.messages().length === 0;
    if (firstLoad) this.loadState.set('loading');
    this.loadError.set(null);

    const startedAt = this.mutations;
    this.loadSub?.unsubscribe();
    this.loadSub = this.env.api.messages(this.version, id).subscribe({
      next: (history) => {
        // A question was sent while this was loading: the local state is newer than this answer.
        if (this.mutations !== startedAt) return;
        this.applyHistory(history);
        this.loadedAt = Date.now();
        this.loadState.set('ready');
        // Opening a conversation must not move it to the top of the list: use the time of its last message.
        this.touchIndex(this.messages().findLast((m) => m.status === 'sent')?.createdAt ?? Date.now());
      },
      error: (error: ApiError) => {
        const gone = error.kind === 'not_found';
        // The API's own 404 text is generic; here it can say what it means for the user.
        const e = gone ? ApiError.conversationNotFound() : error;
        if (gone) this.env.index.remove(id);
        if (firstLoad || gone) {
          this.loadError.set(e);
          this.loadState.set('error');
        } else {
          this.env.toast.show({ tone: 'warn', title: 'Could not refresh the conversation', message: e.message });
        }
      },
    });
  }

  private applyHistory(history: HistoryMessage[]): void {
    const fromServer: ChatMessage[] = history.map((m) => ({
      localId: m.id,
      role: m.role,
      content: m.content,
      createdAt: Date.parse(m.createdAt) || Date.now(),
      citations: m.citations,
      usage: m.usage,
      status: 'sent',
      error: null,
    }));
    // Messages that never reached the server (failed) stay at the end, where the user left them.
    const unsent = this.messages().filter((m) => m.status !== 'sent');
    this.messages.set([...fromServer, ...unsent]);
  }

  // ---- Sending -------------------------------------------------------------------------------------------------

  /** Returns false when the message was not sent (empty, too long, busy or cooling down). */
  send(raw: string): boolean {
    const text = raw.trim();
    if (!text || text.length > MAX_QUESTION_CHARS || this.sending() || this.cooldownLeft() > 0) return false;
    if (this.loadState() === 'loading' || this.loadState() === 'error') return false;

    const message: ChatMessage = {
      localId: localId('msg'),
      role: 'user',
      content: text,
      createdAt: Date.now(),
      citations: [],
      usage: null,
      status: 'pending',
      error: null,
    };
    this.messages.update((list) => [...list, message]);
    this.setDraft('');
    this.run(message.localId, text);
    return true;
  }

  /** Sends a failed message again, unchanged. */
  retry(localMessageId: string): void {
    if (this.sending() || this.cooldownLeft() > 0) return;
    const message = this.messages().find((m) => m.localId === localMessageId && m.status === 'failed');
    if (message) this.run(message.localId, message.content);
  }

  /** Moves a failed message back into the composer so it can be changed before it is sent again. */
  edit(localMessageId: string): void {
    const message = this.messages().find((m) => m.localId === localMessageId);
    if (!message) return;
    this.discard(localMessageId);
    this.setDraft(this.draft().trim() ? `${message.content}\n\n${this.draft()}` : message.content);
  }

  discard(localMessageId: string): void {
    this.messages.update((list) => list.filter((m) => m.localId !== localMessageId));
  }

  /** Stops waiting. The server may still finish the request, so the message is marked as stopped, not as unsent. */
  cancel(): void {
    if (!this.sending()) return;
    const active = this.activeUserId;
    this.sub?.unsubscribe();
    this.sub = null;
    this.finishRequest();
    if (active) this.markFailed(active, ApiError.cancelled());
  }

  private run(localMessageId: string, text: string): void {
    this.sending.set(true);
    this.mutations++;
    this.activeUserId = localMessageId;
    this.patch(localMessageId, { status: 'pending', error: null });
    this.waitingSec.set(0);
    const startedAt = Date.now();
    this.waitTimer = setInterval(() => this.waitingSec.set(Math.floor((Date.now() - startedAt) / 1000)), 1000);

    this.sub = this.env.api.ask(this.version, this.id(), text).subscribe({
      next: (res) => this.complete(localMessageId, res),
      error: (e: ApiError) => {
        this.finishRequest();
        this.markFailed(localMessageId, e);
        // The model service says how long to wait when it is busy: hold the composer until then.
        if (e.retryable && e.retryAfterSec && e.kind === 'unavailable') this.startCooldown(e.retryAfterSec);
      },
    });
  }

  private complete(localMessageId: string, res: ChatResponse): void {
    this.finishRequest();
    const isNew = this.id() === null;
    this.id.set(res.conversationId);
    this.patch(localMessageId, { status: 'sent', error: null });
    const answer: ChatMessage = {
      localId: res.messageId,
      role: 'assistant',
      content: res.answer,
      createdAt: Date.now(),
      citations: res.citations,
      usage: res.usage,
      status: 'sent',
      error: null,
    };
    this.messages.update((list) => [...list, answer]);
    this.loadState.set('ready');
    this.loadedAt = Date.now();
    this.touchIndex(Date.now());
    if (isNew) this.env.idAssigned(this);
    this.env.answered(this);
  }

  private finishRequest(): void {
    this.mutations++;
    this.sending.set(false);
    this.activeUserId = null;
    clearInterval(this.waitTimer);
    this.waitTimer = undefined;
    this.waitingSec.set(0);
  }

  private markFailed(localMessageId: string, error: ApiError): void {
    this.patch(localMessageId, { status: 'failed', error });
  }

  private patch(localMessageId: string, changes: Partial<ChatMessage>): void {
    this.messages.update((list) => list.map((m) => (m.localId === localMessageId ? { ...m, ...changes } : m)));
  }

  private startCooldown(seconds: number): void {
    clearInterval(this.cooldownTimer);
    const until = Date.now() + seconds * 1000;
    this.cooldownLeft.set(seconds);
    // Computed from the clock, not by counting ticks, so a throttled background tab is still right when it wakes.
    this.cooldownTimer = setInterval(() => {
      const left = Math.ceil((until - Date.now()) / 1000);
      if (left <= 0) {
        clearInterval(this.cooldownTimer);
        this.cooldownTimer = undefined;
        this.cooldownLeft.set(0);
      } else {
        this.cooldownLeft.set(left);
      }
    }, 250);
  }

  private touchIndex(updatedAt: number): void {
    const id = this.id();
    if (!id) return;
    if (!this.messages().some((m) => m.role === 'user')) return;
    this.env.index.upsert({ id, version: this.version, title: this.title(), updatedAt });
  }

  /** Stops timers and any request. Called when the state is dropped (eviction, "new conversation"). */
  dispose(): void {
    this.sub?.unsubscribe();
    this.loadSub?.unsubscribe();
    clearInterval(this.waitTimer);
    clearInterval(this.cooldownTimer);
  }
}
