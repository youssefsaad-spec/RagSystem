import { Injectable, signal } from '@angular/core';

export interface ConfirmRequest {
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
  /** Marks the action as destructive (red button, focus starts on Cancel). */
  danger?: boolean;
}

interface Pending extends ConfirmRequest {
  resolve: (value: boolean) => void;
}

/** A promise-based replacement for window.confirm, rendered by the shell as an accessible dialog. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly pending = signal<Pending | null>(null);

  ask(request: ConfirmRequest): Promise<boolean> {
    // Only one question at a time: a new one answers the old one with "no".
    this.pending()?.resolve(false);
    return new Promise<boolean>((resolve) => this.pending.set({ ...request, resolve }));
  }

  answer(value: boolean): void {
    const p = this.pending();
    this.pending.set(null);
    p?.resolve(value);
  }
}
