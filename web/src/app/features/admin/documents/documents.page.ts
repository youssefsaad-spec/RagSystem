import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  HostListener,
  inject,
  input,
  OnInit,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { DocumentsApi, reportOf } from '../../../core/api/documents-api.service';
import {
  API_VERSIONS,
  ApiVersion,
  DEFAULT_MAX_UPLOAD_BYTES,
  DocumentView,
  IngestReport,
  isApiVersion,
  versionLabel,
} from '../../../core/api/models';
import { PreferencesService } from '../../../core/prefs/preferences.service';
import { ConfirmService } from '../../../core/ui/confirm.service';
import { OnlineService } from '../../../core/ui/online.service';
import { ToastService } from '../../../core/ui/toast.service';
import { formatBytes, formatDateTime, formatNumber, formatRelative } from '../../../core/util/format';
import { localId } from '../../../core/util/ids';
import { ErrorNoticeComponent } from '../../../shared/error-notice/error-notice.component';
import { DocumentDetailComponent } from './document-detail.component';
import { ReportViewComponent } from './report-view.component';

type UploadStatus = 'queued' | 'uploading' | 'accepted' | 'checked' | 'rejected' | 'failed';

interface UploadItem {
  id: string;
  file: File;
  version: ApiVersion;
  dryRun: boolean;
  status: UploadStatus;
  percent: number | null;
  error: ApiError | null;
  report: IngestReport | null;
}

const ALLOWED = ['.md', '.json'];
const MAX_BATCH = 25;
const POLL_MS = 3_000;
const POLL_MAX_MS = 15_000;

function localError(title: string, message: string): ApiError {
  return new ApiError({ kind: 'validation', status: 0, title, message, retryable: false });
}

@Component({
  selector: 'app-documents-page',
  imports: [ErrorNoticeComponent, ReportViewComponent, DocumentDetailComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './documents.page.html',
  styleUrl: './documents.page.css',
})
export class DocumentsPage implements OnInit {
  /** From the address: /admin/documents?version=v3 */
  readonly versionParam = input<string | undefined>(undefined, { alias: 'version' });

  private readonly api = inject(DocumentsApi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly prefs = inject(PreferencesService);
  private readonly online = inject(OnlineService);

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly versions = API_VERSIONS;
  protected readonly label = versionLabel;
  protected readonly format = formatNumber;
  protected readonly bytes = formatBytes;
  protected readonly absolute = formatDateTime;
  protected readonly relative = formatRelative;
  protected readonly accept = ALLOWED.join(',');
  protected readonly maxMb = Math.round(DEFAULT_MAX_UPLOAD_BYTES / 1024 / 1024);

  protected readonly version = computed<ApiVersion>(() => {
    const v = this.versionParam()?.toLowerCase();
    return isApiVersion(v) ? v : this.prefs.lastVersion();
  });

  protected readonly docs = signal<DocumentView[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<ApiError | null>(null);
  protected readonly busyIds = signal<ReadonlySet<string>>(new Set());
  protected readonly selected = signal<DocumentView | null>(null);

  protected readonly queue = signal<UploadItem[]>([]);
  protected readonly dryRun = signal(false);
  protected readonly dragging = signal(false);
  protected readonly uploading = computed(() => this.queue().some((i) => i.status === 'queued' || i.status === 'uploading'));
  protected readonly finished = computed(() => this.queue().some((i) => i.status !== 'queued' && i.status !== 'uploading'));

  protected readonly processing = computed(() => this.docs().filter((d) => d.status === 'Processing').length);

  private listSub: Subscription | null = null;
  private uploadSub: Subscription | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | undefined;
  private pollDelay = POLL_MS;

  constructor() {
    // Switching version (tab or address) reloads the list.
    effect(() => {
      const v = this.version();
      untracked(() => {
        this.selected.set(null);
        this.docs.set([]);
        this.load(v, false);
      });
    });

    // Back from another tab or from sleep: refresh at once instead of waiting for the next poll.
    const onVisible = () => {
      if (!document.hidden) this.load(this.version(), true);
    };
    document.addEventListener('visibilitychange', onVisible);

    const warnUnload = (e: BeforeUnloadEvent) => {
      if (this.uploading()) e.preventDefault();
    };
    window.addEventListener('beforeunload', warnUnload);

    inject(DestroyRef).onDestroy(() => {
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('beforeunload', warnUnload);
      clearTimeout(this.pollTimer);
      this.listSub?.unsubscribe();
      this.uploadSub?.unsubscribe();
    });
  }

  ngOnInit(): void {
    this.prefs.setLastVersion(this.version());
  }

  // ---- The list ------------------------------------------------------------------------------------------------

  protected setVersion(v: ApiVersion): void {
    if (v === this.version()) return;
    this.prefs.setLastVersion(v);
    void this.router.navigate([], { queryParams: { version: v }, replaceUrl: true });
  }

  protected reload(): void {
    this.load(this.version(), false);
  }

  /** `quiet` keeps the current rows on screen and does not show errors as a banner (used by polling). */
  private load(version: ApiVersion, quiet: boolean): void {
    clearTimeout(this.pollTimer);
    this.listSub?.unsubscribe();
    if (!quiet) {
      this.loading.set(true);
      this.loadError.set(null);
    }
    this.listSub = this.api.list(version).subscribe({
      next: (list) => {
        if (version !== this.version()) return; // the user switched version while this was loading
        this.docs.set(list);
        this.loading.set(false);
        this.loadError.set(null);
        this.pollDelay = POLL_MS;
        // A document the user is looking at follows its refreshed row.
        const open = this.selected();
        if (open) this.selected.set(list.find((d) => d.id === open.id) ?? null);
        this.schedulePoll();
      },
      error: (e: ApiError) => {
        if (version !== this.version()) return;
        this.loading.set(false);
        if (quiet && this.docs().length > 0) {
          this.pollDelay = Math.min(POLL_MAX_MS, this.pollDelay * 2);
          this.schedulePoll();
        } else {
          this.loadError.set(e);
        }
      },
    });
  }

  /** While a document is Processing, look again every few seconds (slower after errors, paused when hidden or offline). */
  private schedulePoll(): void {
    clearTimeout(this.pollTimer);
    if (this.processing() === 0) return;
    this.pollTimer = setTimeout(() => {
      if (document.hidden || !this.online.online()) this.schedulePoll();
      else this.load(this.version(), true);
    }, this.pollDelay);
  }

  protected async remove(doc: DocumentView): Promise<void> {
    if (this.busyIds().has(doc.id)) return;
    const ok = await this.confirm.ask({
      title: `Delete ${doc.fileName}?`,
      message: `${doc.chunkCount ? `Its ${formatNumber(doc.chunkCount)} chunks are` : 'Its chunks are'} removed from the ${versionLabel(this.version())} knowledge base, so the assistant stops answering from it. Upload the file again to restore it.`,
      confirmLabel: 'Delete document',
      danger: true,
    });
    if (!ok) return;

    this.setBusy(doc.id, true);
    this.api.delete(this.version(), doc.id).subscribe({
      next: () => {
        this.setBusy(doc.id, false);
        this.docs.update((list) => list.filter((d) => d.id !== doc.id));
        if (this.selected()?.id === doc.id) this.selected.set(null);
        this.toast.success('Document deleted', doc.fileName);
      },
      error: (e: ApiError) => {
        this.setBusy(doc.id, false);
        if (e.kind === 'not_found') {
          // Someone else already removed it: the goal is reached.
          this.docs.update((list) => list.filter((d) => d.id !== doc.id));
          this.toast.show({ tone: 'info', title: 'Already deleted', message: doc.fileName });
        } else {
          this.toast.error('Could not delete the document', e.message);
        }
      },
    });
  }

  private setBusy(id: string, busy: boolean): void {
    this.busyIds.update((set) => {
      const next = new Set(set);
      if (busy) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  // ---- Uploading -----------------------------------------------------------------------------------------------

  protected pick(): void {
    this.fileInput()?.nativeElement.click();
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files) this.addFiles(Array.from(input.files));
    input.value = ''; // so picking the same file again still fires a change
  }

  // The browser would otherwise open a dropped file in the tab and throw the page away.
  @HostListener('window:dragover', ['$event'])
  protected onWindowDragOver(e: DragEvent): void {
    if (e.dataTransfer?.types.includes('Files')) e.preventDefault();
  }

  @HostListener('window:drop', ['$event'])
  protected onWindowDrop(e: DragEvent): void {
    if (e.dataTransfer?.types.includes('Files')) e.preventDefault();
    this.dragging.set(false);
  }

  protected onDrop(e: DragEvent): void {
    e.preventDefault();
    this.dragging.set(false);
    const files = Array.from(e.dataTransfer?.files ?? []);
    if (files.length > 0) this.addFiles(files);
  }

  protected addFiles(files: File[]): void {
    const accepted = files.slice(0, MAX_BATCH);
    if (files.length > MAX_BATCH) {
      this.toast.show({ tone: 'warn', title: `Only the first ${MAX_BATCH} files were added`, message: 'Upload the rest in another batch.' });
    }
    const items = accepted.map((file): UploadItem => {
      const item: UploadItem = {
        id: localId('upload'),
        file,
        version: this.version(),
        dryRun: this.dryRun(),
        status: 'queued',
        percent: null,
        error: null,
        report: null,
      };
      const problem = this.checkFile(file);
      return problem ? { ...item, status: 'failed', error: problem } : item;
    });
    this.queue.update((q) => [...q, ...items]);
    this.next();
  }

  /** Cheap checks before sending anything. The server checks again and has the last word. */
  private checkFile(file: File): ApiError | null {
    const name = file.name.toLowerCase();
    if (!ALLOWED.some((ext) => name.endsWith(ext))) {
      return localError('Unsupported file type', 'Upload a Markdown (.md) document or an OpenAPI (.json) file.');
    }
    if (file.size === 0) {
      return localError('The file is empty', 'Choose a file that has content. A folder cannot be uploaded.');
    }
    if (file.size > DEFAULT_MAX_UPLOAD_BYTES) {
      return localError('File too large', `This file is ${formatBytes(file.size)}. The limit is ${this.maxMb} MB.`);
    }
    return null;
  }

  /** One file at a time: the server rejects a file whose earlier version is still being processed. */
  private next(): void {
    if (this.queue().some((i) => i.status === 'uploading')) return;
    const item = this.queue().find((i) => i.status === 'queued');
    if (!item) return;

    this.patch(item.id, { status: 'uploading', percent: 0 });
    this.uploadSub = this.api.upload(item.version, item.file, item.dryRun).subscribe({
      next: (event) => {
        if (event.type === 'progress') {
          this.patch(item.id, { percent: event.percent });
          return;
        }
        const o = event.outcome;
        if (o.kind === 'dryRun') this.patch(item.id, { status: 'checked', report: o.report, percent: 100 });
        else this.patch(item.id, { status: o.kind, percent: 100 });
        if (o.kind !== 'dryRun' && item.version === this.version()) this.load(this.version(), true);
      },
      error: (e: ApiError) => {
        const report = reportOf(e);
        this.patch(item.id, { status: report ? 'rejected' : 'failed', error: e, report, percent: null });
        // A busy document usually means a previous upload of it is still running: refresh so its status shows.
        if (e.kind === 'conflict' && item.version === this.version()) this.load(this.version(), true);
        this.next();
      },
      complete: () => this.next(),
    });
  }

  /** Sending the same file again can only help for problems that are not about the file itself. */
  protected canRetry(item: UploadItem): boolean {
    const kind = item.error?.kind;
    return item.status === 'failed' && !!kind && !['validation', 'payload_too_large', 'unsupported_media', 'unprocessable'].includes(kind);
  }

  protected retryItem(item: UploadItem): void {
    this.patch(item.id, { status: 'queued', error: null, report: null, percent: null });
    this.next();
  }

  protected removeItem(id: string): void {
    const item = this.queue().find((i) => i.id === id);
    if (item?.status === 'uploading') {
      this.uploadSub?.unsubscribe(); // aborts the request in the browser
      this.uploadSub = null;
    }
    this.queue.update((q) => q.filter((i) => i.id !== id));
    this.next();
  }

  protected clearFinished(): void {
    this.queue.update((q) => q.filter((i) => i.status === 'queued' || i.status === 'uploading'));
  }

  private patch(id: string, changes: Partial<UploadItem>): void {
    this.queue.update((q) => q.map((i) => (i.id === id ? { ...i, ...changes } : i)));
  }

  protected statusText(item: UploadItem): string {
    switch (item.status) {
      case 'queued':
        return 'Waiting';
      case 'uploading':
        return item.percent === null ? 'Uploading' : item.percent >= 100 ? 'Checking the file' : `Uploading ${item.percent}%`;
      case 'accepted':
        return 'Accepted. It is being processed and appears in the list below.';
      case 'checked':
        return 'Checked only. Nothing was stored.';
      case 'rejected':
        return 'Rejected. The document has errors and was not stored.';
      default:
        return item.error?.title ?? 'Failed';
    }
  }

  protected statusTone(item: UploadItem): 'ok' | 'warn' | 'danger' | 'info' | 'plain' {
    switch (item.status) {
      case 'accepted':
        return 'ok';
      case 'checked':
        return 'info';
      case 'rejected':
      case 'failed':
        return 'danger';
      default:
        return 'plain';
    }
  }
}
