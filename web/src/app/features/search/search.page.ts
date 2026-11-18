import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import { SearchApi } from '../../core/api/search-api.service';
import { API_VERSIONS, ApiVersion, isApiVersion, MAX_QUESTION_CHARS, SearchResult, versionLabel } from '../../core/api/models';
import { PreferencesService } from '../../core/prefs/preferences.service';
import { formatNumber } from '../../core/util/format';
import { ErrorNoticeComponent } from '../../shared/error-notice/error-notice.component';

/** Why retrieval left a chunk out, in words. The API sends the enum name. */
const DROP_REASONS: Record<string, string> = {
  BelowMinScore: 'Below the minimum score, so it is not relevant enough.',
  NearDuplicate: 'Almost the same text as a chunk that was kept.',
  OverBudget: 'Relevant, but the answer’s context budget was already full.',
};

/**
 * Shows what retrieval finds for a question, without calling a chat model: the chunks that would be sent to the model
 * and the candidates that were left out, with scores. A tool for checking and tuning. Address: /search?version=v3&q=...
 */
@Component({
  selector: 'app-search-page',
  imports: [ErrorNoticeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search.page.html',
  styleUrl: './search.page.css',
})
export class SearchPage {
  readonly versionParam = input<string | undefined>(undefined, { alias: 'version' });
  readonly queryParam = input<string | undefined>(undefined, { alias: 'q' });

  private readonly api = inject(SearchApi);
  private readonly router = inject(Router);
  private readonly prefs = inject(PreferencesService);

  protected readonly versions = API_VERSIONS;
  protected readonly label = versionLabel;
  protected readonly format = formatNumber;
  protected readonly max = MAX_QUESTION_CHARS;

  protected readonly version = computed<ApiVersion>(() => {
    const v = this.versionParam()?.toLowerCase();
    return isApiVersion(v) ? v : this.prefs.lastVersion();
  });

  protected readonly text = signal('');
  protected readonly running = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly result = signal<SearchResult | null>(null);
  protected readonly canRun = computed(() => {
    const length = this.text().trim().length;
    return length > 0 && length <= this.max && !this.running();
  });

  private sub: Subscription | null = null;

  constructor() {
    // The address is the state: opening a shared link (or going back) runs the same search again.
    effect(() => {
      const q = this.queryParam()?.trim();
      const v = this.version();
      untracked(() => {
        if (q) {
          this.text.set(q);
          this.execute(v, q);
        } else {
          this.sub?.unsubscribe();
          this.running.set(false);
          this.result.set(null);
          this.error.set(null);
        }
      });
    });
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  protected setVersion(v: ApiVersion): void {
    if (v === this.version()) return;
    this.prefs.setLastVersion(v);
    const q = this.text().trim();
    void this.router.navigate([], { queryParams: { version: v, q: q || null }, replaceUrl: true });
  }

  protected submit(): void {
    const q = this.text().trim();
    if (!this.canRun()) return;
    if (q === this.queryParam()?.trim()) {
      this.execute(this.version(), q); // same address: search again
    } else {
      void this.router.navigate([], { queryParams: { version: this.version(), q } });
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing || event.keyCode === 229) return;
    event.preventDefault();
    this.submit();
  }

  protected reason(code: string): string {
    return DROP_REASONS[code] ?? code;
  }

  protected percent(score: number): string {
    return `${Math.round(score * 100)}%`;
  }

  private execute(version: ApiVersion, question: string): void {
    this.sub?.unsubscribe();
    this.running.set(true);
    this.error.set(null);
    this.sub = this.api.search(version, question).subscribe({
      next: (r) => {
        this.result.set(r);
        this.running.set(false);
      },
      error: (e: ApiError) => {
        this.result.set(null);
        this.error.set(e);
        this.running.set(false);
      },
    });
  }
}
