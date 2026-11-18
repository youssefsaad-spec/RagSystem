import { effect, Injectable, signal } from '@angular/core';
import { localStore } from '../util/safe-storage';

export type ThemeChoice = 'system' | 'light' | 'dark';
const KEY = 'ragsystem.theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly choice = signal<ThemeChoice>('system');

  constructor() {
    const saved = localStore.get(KEY);
    if (saved === 'light' || saved === 'dark') this.choice.set(saved);
    effect(() => {
      const c = this.choice();
      const root = document.documentElement;
      if (c === 'system') root.removeAttribute('data-theme');
      else root.setAttribute('data-theme', c);
    });
  }

  /** Cycles system -> light -> dark. */
  next(): void {
    const order: ThemeChoice[] = ['system', 'light', 'dark'];
    const n = order[(order.indexOf(this.choice()) + 1) % order.length];
    this.choice.set(n);
    if (n === 'system') localStore.remove(KEY);
    else localStore.set(KEY, n);
  }
}
