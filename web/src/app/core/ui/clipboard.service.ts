import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ClipboardService {
  /**
   * navigator.clipboard needs a secure context (https or localhost) and a user gesture; on a plain-http intranet
   * address it is undefined. The fallback uses a hidden textarea. Resolves false when both fail.
   */
  async copy(text: string): Promise<boolean> {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return true;
      }
    } catch {
      /* permission denied or not focused: try the fallback */
    }
    return this.legacyCopy(text);
  }

  private legacyCopy(text: string): boolean {
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.cssText = 'position:fixed;top:0;left:0;opacity:0;pointer-events:none';
    const active = document.activeElement as HTMLElement | null;
    document.body.appendChild(area);
    try {
      area.select();
      area.setSelectionRange(0, text.length);
      return document.execCommand('copy');
    } catch {
      return false;
    } finally {
      area.remove();
      active?.focus?.();
    }
  }
}
