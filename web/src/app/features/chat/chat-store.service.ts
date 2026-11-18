import { inject, Injectable, signal, untracked } from '@angular/core';
import { ChatApi } from '../../core/api/chat-api.service';
import { ApiVersion } from '../../core/api/models';
import { ToastService } from '../../core/ui/toast.service';
import { ConversationIndex } from './conversation-index.service';
import { ChatEnv, ConversationState } from './conversation-state';

/** States kept in memory. Older ones that are idle are dropped; their messages reload from the server on demand. */
const MAX_STATES = 20;

@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly api = inject(ChatApi);
  private readonly index = inject(ConversationIndex);
  private readonly toast = inject(ToastService);

  /** Existing conversations, keyed by version and id. Insertion order doubles as recency. */
  private readonly existing = new Map<string, ConversationState>();
  /** The one unsent "new conversation" per version. */
  private readonly fresh = new Map<ApiVersion, ConversationState>();
  /** Bumped when a state is dropped, so a page showing it looks it up again. */
  readonly revision = signal(0);
  /** The conversation on screen, so an answer for any other one can be announced. */
  private active: ConversationState | null = null;

  private readonly env: ChatEnv = {
    api: this.api,
    index: this.index,
    toast: this.toast,
    idAssigned: (state) => this.adopt(state),
    answered: (state) => {
      if (state === this.active) return;
      this.toast.show({ tone: 'success', title: 'Answer ready', message: `In “${state.title()}”.` });
    },
  };

  /** The state for a route: an existing conversation when `id` is given, else the version's new conversation. */
  open(version: ApiVersion, id: string | null): ConversationState {
    // Called from a page's computed(): reading the state's own signals here (id, sending)
    // would make the page re-open a blank conversation the moment the first answer assigns an id.
    return untracked(() => this.openState(version, id));
  }

  private openState(version: ApiVersion, id: string | null): ConversationState {
    if (id) {
      const key = `${version}:${id}`;
      let state = this.existing.get(key);
      if (state) {
        this.existing.delete(key); // re-insert: most recently used last
      } else {
        state = new ConversationState(version, id, this.env);
      }
      this.existing.set(key, state);
      this.evict();
      return state;
    }
    let state = this.fresh.get(version);
    if (!state) {
      state = new ConversationState(version, null, this.env);
      this.fresh.set(version, state);
    }
    return state;
  }

  setActive(state: ConversationState | null): void {
    this.active = state;
  }

  /** "New conversation": starts from a clean slate unless the current one is still waiting for an answer. */
  startNew(version: ApiVersion): void {
    const current = this.fresh.get(version);
    if (current && !current.sending()) {
      current.dispose();
      this.fresh.delete(version);
      this.revision.update((n) => n + 1);
    }
  }

  /** A new conversation got its id: from now on it is an existing one, and the next "new" starts empty. */
  private adopt(state: ConversationState): void {
    const id = state.id();
    if (!id) return;
    this.existing.set(`${state.version}:${id}`, state);
    if (this.fresh.get(state.version) === state) this.fresh.delete(state.version);
    this.evict();
  }

  private evict(): void {
    if (this.existing.size <= MAX_STATES) return;
    for (const [key, state] of this.existing) {
      if (this.existing.size <= MAX_STATES) break;
      if (!state.sending()) {
        state.dispose();
        this.existing.delete(key);
      }
    }
  }
}
