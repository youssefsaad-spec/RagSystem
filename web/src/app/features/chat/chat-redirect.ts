import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { PreferencesService } from '../../core/prefs/preferences.service';

/** /chat has no content of its own: it opens a new conversation on the version used last. */
@Component({ selector: 'app-chat-redirect', template: '' })
export class ChatRedirect {
  constructor() {
    const router = inject(Router);
    void router.navigate(['/chat', inject(PreferencesService).lastVersion()], { replaceUrl: true });
  }
}
