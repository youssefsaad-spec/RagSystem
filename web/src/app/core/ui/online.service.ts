import { Injectable, signal } from '@angular/core';

/**
 * `navigator.onLine === false` is reliable (there is no network); `true` only means "some network". So this is used
 * to warn and to pause polling, never to block a request that might still work.
 */
@Injectable({ providedIn: 'root' })
export class OnlineService {
  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine !== false);

  constructor() {
    window.addEventListener('online', () => this.online.set(true));
    window.addEventListener('offline', () => this.online.set(false));
  }
}
