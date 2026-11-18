/**
 * localStorage / sessionStorage can throw on access (blocked site data, some private modes) or on write (quota).
 * This wrapper never throws: it falls back to memory for the life of the page.
 */
export class SafeStorage {
  private readonly memory = new Map<string, string>();

  constructor(private readonly kind: 'local' | 'session') {}

  private backend(): Storage | null {
    try {
      const s = this.kind === 'local' ? window.localStorage : window.sessionStorage;
      return s ?? null;
    } catch {
      return null;
    }
  }

  get(key: string): string | null {
    try {
      const value = this.backend()?.getItem(key);
      if (value !== undefined && value !== null) return value;
    } catch {
      /* fall through to memory */
    }
    return this.memory.get(key) ?? null;
  }

  /** Returns false when the value could only be kept in memory. */
  set(key: string, value: string): boolean {
    try {
      const backend = this.backend();
      if (backend) {
        backend.setItem(key, value);
        this.memory.delete(key);
        return true;
      }
    } catch {
      /* quota exceeded or blocked */
    }
    this.memory.set(key, value);
    return false;
  }

  remove(key: string): void {
    this.memory.delete(key);
    try {
      this.backend()?.removeItem(key);
    } catch {
      /* ignore */
    }
  }
}

export const localStore = new SafeStorage('local');
export const sessionStore = new SafeStorage('session');
