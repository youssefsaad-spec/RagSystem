import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { DocumentsApi } from '../../../core/api/documents-api.service';
import { ApiVersion, ChunkView, DocumentView } from '../../../core/api/models';
import { formatDateTime, formatNumber, truncate } from '../../../core/util/format';
import { ErrorNoticeComponent } from '../../../shared/error-notice/error-notice.component';
import { ModalComponent } from '../../../shared/modal/modal.component';

const PAGE_SIZE = 25;

/** What the API stored for a document: its metadata and its chunks, a page at a time. */
@Component({
  selector: 'app-document-detail',
  imports: [ModalComponent, ErrorNoticeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-detail.component.html',
  styleUrl: './document-detail.component.css',
})
export class DocumentDetailComponent {
  readonly document = input.required<DocumentView | null>();
  readonly version = input.required<ApiVersion>();
  readonly closed = output<void>();

  private readonly api = inject(DocumentsApi);
  private sub: Subscription | null = null;

  protected readonly format = formatNumber;
  protected readonly date = formatDateTime;

  protected readonly chunks = signal<ChunkView[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly page = signal(0);
  protected readonly hasMore = signal(false);
  protected readonly pageSize = PAGE_SIZE;

  protected readonly open = computed(() => this.document() !== null);

  constructor() {
    // A different document (or none) resets the view.
    effect(() => {
      const doc = this.document();
      untracked(() => {
        this.sub?.unsubscribe();
        this.chunks.set([]);
        this.error.set(null);
        this.page.set(0);
        this.hasMore.set(false);
        if (doc && doc.status === 'Ready') this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  /** The first line of a chunk, as its heading in the list. */
  protected preview(content: string): string {
    return truncate(content, 90);
  }

  protected goTo(page: number): void {
    this.page.set(Math.max(0, page));
    this.load();
  }

  protected load(): void {
    const doc = this.document();
    if (!doc) return;
    this.sub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    const skip = this.page() * PAGE_SIZE;
    // One extra row tells whether there is a next page without a count endpoint.
    this.sub = this.api.chunks(this.version(), doc.id, { skip, take: PAGE_SIZE + 1 }).subscribe({
      next: (rows) => {
        this.hasMore.set(rows.length > PAGE_SIZE);
        this.chunks.set(rows.slice(0, PAGE_SIZE));
        this.loading.set(false);
      },
      error: (e: ApiError) => {
        this.error.set(e);
        this.loading.set(false);
      },
    });
  }
}
