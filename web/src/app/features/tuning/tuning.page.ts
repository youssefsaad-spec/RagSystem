import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import { API_VERSIONS, ApiVersion, isApiVersion, versionLabel } from '../../core/api/models';
import { SearchApi } from '../../core/api/search-api.service';
import { PreferencesService } from '../../core/prefs/preferences.service';
import { ClipboardService } from '../../core/ui/clipboard.service';
import { ToastService } from '../../core/ui/toast.service';
import { formatNumber } from '../../core/util/format';
import { localStore } from '../../core/util/safe-storage';
import {
  bestScore,
  checkExpected,
  countAtOrAbove,
  describeOutcome,
  ExpectedOutcome,
  isHit,
  isMiss,
  MAX_QUESTIONS,
  parseQuestions,
  summarise,
  toCsv,
  TuningRow,
  weakestKept,
} from './tuning-math';

const EXAMPLES: Record<ApiVersion, string> = {
  v2: [
    '# One question per line. Add " => text" to say which source it should find, or " => none" if nothing should be found.',
    'How do I send the subscription key? => NDC Portal APIs V2',
    'What is the difference between Hold and BookAndPay?',
    'What happens when I exceed the rate limit?',
    'Which call returns the ndcBookingReference?',
    'What is the capital of France? => none',
  ].join('\n'),
  v3: [
    '# One question per line. Add " => text" to say which source it should find, or " => none" if nothing should be found.',
    "What changed in V3 compared with V2? => What's New",
    'How do I select seats in V3? => NDC Portal APIs V3',
    'Which endpoints return a new offerId?',
    'How do I send the subscription key? => NDC Portal APIs V3',
    'What is the difference between Hold and BookAndPay?',
    'What is the capital of France? => none',
  ].join('\n'),
};

const DEFAULT_THRESHOLD = 0.6;
const keyFor = (v: ApiVersion) => `ragsystem.tuning.${v}`;
const THRESHOLD_KEY = 'ragsystem.tuning.threshold';

/**
 * F28, retrieval tuning: runs a set of real questions through the search (no chat model), shows the scores next to the
 * minimum score, lets you try another minimum, and checks that each question finds the source it was written for.
 */
@Component({
  selector: 'app-tuning-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tuning.page.html',
  styleUrl: './tuning.page.css',
})
export class TuningPage {
  readonly versionParam = input<string | undefined>(undefined, { alias: 'version' });

  private readonly api = inject(SearchApi);
  private readonly router = inject(Router);
  private readonly prefs = inject(PreferencesService);
  private readonly clipboard = inject(ClipboardService);
  private readonly toast = inject(ToastService);

  protected readonly versions = API_VERSIONS;
  protected readonly label = versionLabel;
  protected readonly format = formatNumber;
  protected readonly maxQuestions = MAX_QUESTIONS;

  protected readonly version = computed<ApiVersion>(() => {
    const v = this.versionParam()?.toLowerCase();
    return isApiVersion(v) ? v : this.prefs.lastVersion();
  });

  protected readonly text = signal('');
  protected readonly threshold = signal(this.readThreshold());
  protected readonly rows = signal<TuningRow[]>([]);
  protected readonly running = signal(false);
  protected readonly open = signal<number | null>(null);

  protected readonly questions = computed(() => parseQuestions(this.text()));
  protected readonly summary = computed(() => summarise(this.rows(), this.threshold()));
  protected readonly canRun = computed(() => this.questions().length > 0 && !this.running());
  protected readonly hasResults = computed(() => this.rows().some((r) => r.state === 'done' || r.state === 'failed'));

  private sub: Subscription | null = null;

  constructor() {
    // Each version has its own question set, remembered in this browser.
    effect(() => {
      const v = this.version();
      untracked(() => {
        this.stop();
        this.text.set(localStore.get(keyFor(v)) ?? '');
        this.rows.set([]);
        this.open.set(null);
      });
    });
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  private readThreshold(): number {
    const saved = Number(localStore.get(THRESHOLD_KEY));
    return Number.isFinite(saved) && saved > 0 && saved <= 1 ? saved : DEFAULT_THRESHOLD;
  }

  protected setVersion(v: ApiVersion): void {
    if (v === this.version()) return;
    this.prefs.setLastVersion(v);
    void this.router.navigate([], { queryParams: { version: v }, replaceUrl: true });
  }

  protected setText(value: string): void {
    this.text.set(value);
    localStore.set(keyFor(this.version()), value);
  }

  protected loadExamples(): void {
    this.setText(EXAMPLES[this.version()]);
    this.rows.set([]);
  }

  protected setThreshold(raw: string): void {
    const n = Number(raw);
    if (!Number.isFinite(n) || n <= 0 || n > 1) return;
    this.threshold.set(n);
    localStore.set(THRESHOLD_KEY, String(n));
  }

  // ---- Running -------------------------------------------------------------------------------------------------

  /** One question at a time: every search embeds the question, which is a paid call to the model service. */
  protected run(): void {
    if (!this.canRun()) return;
    const version = this.version();
    this.rows.set(this.questions().map((question) => ({ question, state: 'waiting', result: null, error: null })));
    this.open.set(null);
    this.running.set(true);
    this.next(version, 0);
  }

  protected stop(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    if (this.running()) {
      this.running.set(false);
      this.rows.update((rows) => rows.map((r) => (r.state === 'running' ? { ...r, state: 'waiting' } : r)));
    }
  }

  private next(version: ApiVersion, index: number): void {
    const rows = this.rows();
    if (index >= rows.length) {
      this.running.set(false);
      return;
    }
    this.patch(index, { state: 'running' });
    this.sub = this.api.search(version, rows[index].question.text).subscribe({
      next: (result) => this.patch(index, { state: 'done', result, error: null }),
      error: (e: ApiError) => {
        this.patch(index, { state: 'failed', error: e.message });
        // A model service that is down will fail every question: stop instead of repeating it 50 times.
        if (e.kind === 'unavailable' || e.kind === 'upstream' || e.kind === 'network' || e.kind === 'timeout') {
          this.running.set(false);
          this.toast.show({ tone: 'warn', title: 'Stopped after a failure', message: e.message });
          return;
        }
        this.next(version, index + 1);
      },
      complete: () => this.next(version, index + 1),
    });
  }

  private patch(index: number, changes: Partial<TuningRow>): void {
    this.rows.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...changes } : r)));
  }

  // ---- Reading a row -------------------------------------------------------------------------------------------

  protected best(row: TuningRow): number | null {
    return row.result ? bestScore(row.result) : null;
  }

  protected weakest(row: TuningRow): number | null {
    return row.result ? weakestKept(row.result) : null;
  }

  protected atThreshold(row: TuningRow): number | null {
    return row.result ? countAtOrAbove(row.result, this.threshold()) : null;
  }

  protected outcome(row: TuningRow): ExpectedOutcome | null {
    return row.result && row.question.expected ? checkExpected(row.result, row.question.expected) : null;
  }

  protected describe = describeOutcome;
  protected hit = isHit;
  protected miss = isMiss;

  protected percent(score: number | null): string {
    return score === null ? 'n/a' : `${Math.round(score * 100)}%`;
  }

  protected toggle(index: number): void {
    this.open.update((current) => (current === index ? null : index));
  }

  // ---- Export --------------------------------------------------------------------------------------------------

  protected async copyCsv(): Promise<void> {
    const csv = toCsv(this.rows(), this.version(), this.threshold());
    if (await this.clipboard.copy(csv)) this.toast.success('Results copied', 'Paste them into a spreadsheet or the tuning notes.');
    else this.toast.error('Could not copy', 'Use "Download JSON" instead.');
  }

  protected downloadJson(): void {
    const data = {
      version: this.version(),
      triedMinScore: this.threshold(),
      ranAt: new Date().toISOString(),
      summary: this.summary(),
      rows: this.rows().map((r) => ({ question: r.question, state: r.state, error: r.error, result: r.result })),
    };
    const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `retrieval-tuning-${this.version()}-${new Date().toISOString().slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }
}
