import { Injectable, signal } from '@angular/core';
import { ApiVersion, isApiVersion } from '../api/models';
import { localStore } from '../util/safe-storage';

const VERSION = 'ragsystem.version';

/** Small things this browser remembers between visits: the documentation version used last. */
@Injectable({ providedIn: 'root' })
export class PreferencesService {
  readonly lastVersion = signal<ApiVersion>('v3');

  constructor() {
    const saved = localStore.get(VERSION);
    if (isApiVersion(saved)) this.lastVersion.set(saved);
  }

  setLastVersion(version: ApiVersion): void {
    this.lastVersion.set(version);
    localStore.set(VERSION, version);
  }
}
