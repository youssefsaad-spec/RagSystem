import { Injectable, signal } from '@angular/core';
import { localId } from '../util/ids';

export type ToastTone = 'info' | 'success' | 'warn' | 'error';

export interface Toast {
  id: string;
  tone: ToastTone;
  title: string;
  message?: string;
}

export interface ToastInput {
  tone?: ToastTone;
  title: string;
  message?: string;
  /** Milliseconds; 0 keeps it until dismissed. Errors stay longer by default. */
  durationMs?: number;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();

  show(input: ToastInput): string {
    const tone = input.tone ?? 'info';
    const toast: Toast = { id: localId('toast'), tone, title: input.title, message: input.message };
    // Never stack identical toasts (for example three failed polls in a row).
    const existing = this.toasts().find((t) => t.title === toast.title && t.message === toast.message && t.tone === tone);
    if (existing) return existing.id;

    this.toasts.update((list) => [...list.slice(-3), toast]);
    const duration = input.durationMs ?? (tone === 'error' ? 10_000 : tone === 'warn' ? 8_000 : 4_000);
    if (duration > 0) this.timers.set(toast.id, setTimeout(() => this.dismiss(toast.id), duration));
    return toast.id;
  }

  success(title: string, message?: string): void {
    this.show({ tone: 'success', title, message });
  }

  error(title: string, message?: string): void {
    this.show({ tone: 'error', title, message });
  }

  dismiss(id: string): void {
    const timer = this.timers.get(id);
    if (timer) clearTimeout(timer);
    this.timers.delete(id);
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
