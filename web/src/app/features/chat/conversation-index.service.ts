import { Injectable, signal } from '@angular/core';
import { ApiVersion, isApiVersion } from '../../core/api/models';
import { isGuid } from '../../core/util/ids';
import { localStore } from '../../core/util/safe-storage';

export interface ConversationMeta {
  id: string;
  version: ApiVersion;
  title: string;
  updatedAt: number;
}

const MAX_ITEMS = 100;
const STORAGE_KEY = 'ragsystem.conversations';

/**
 * The API has no "list conversations" endpoint, so this browser remembers the ones it has seen. The messages
 * themselves always come from the server. Removing an entry only forgets it here: the server has no delete endpoint.
 * (Until API keys exist, every visitor of this browser shares the list. F29 makes it per client.)
 */
@Injectable({ providedIn: 'root' })
export class ConversationIndex {
  readonly items = signal<ConversationMeta[]>(this.read());

  constructor() {
    // Another tab of the same app changed the list.
    window.addEventListener('storage', (e) => {
      if (e.key === STORAGE_KEY) this.items.set(this.read());
    });
  }

  upsert(meta: ConversationMeta): void {
    // Re-read first so a change made in another tab is merged, not overwritten.
    const merged = [meta, ...this.read().filter((m) => m.id !== meta.id)]
      .sort((a, b) => b.updatedAt - a.updatedAt)
      .slice(0, MAX_ITEMS);
    this.write(merged);
  }

  remove(id: string): void {
    this.write(this.read().filter((m) => m.id !== id));
  }

  private write(list: ConversationMeta[]): void {
    this.items.set(list);
    localStore.set(STORAGE_KEY, JSON.stringify(list));
  }

  /** Tolerates a missing, corrupt or hand-edited value. */
  private read(): ConversationMeta[] {
    try {
      const parsed: unknown = JSON.parse(localStore.get(STORAGE_KEY) ?? '[]');
      if (!Array.isArray(parsed)) return [];
      const seen = new Set<string>();
      const out: ConversationMeta[] = [];
      for (const item of parsed) {
        const m = item as Partial<ConversationMeta> | null;
        if (!m || !isGuid(m.id) || !isApiVersion(m.version) || typeof m.title !== 'string' || typeof m.updatedAt !== 'number') continue;
        if (seen.has(m.id)) continue;
        seen.add(m.id);
        out.push({ id: m.id, version: m.version, title: m.title.slice(0, 120), updatedAt: m.updatedAt });
      }
      return out.sort((a, b) => b.updatedAt - a.updatedAt).slice(0, MAX_ITEMS);
    } catch {
      return [];
    }
  }
}
