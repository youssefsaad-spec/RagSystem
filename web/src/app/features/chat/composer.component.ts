import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { API_VERSIONS, ApiVersion, MAX_QUESTION_CHARS, versionLabel } from '../../core/api/models';
import { formatDuration, formatNumber } from '../../core/util/format';

@Component({
  selector: 'app-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './composer.component.html',
  styleUrl: './composer.component.css',
})
export class ComposerComponent {
  readonly value = input.required<string>();
  /** A question is waiting for its answer. */
  readonly busy = input(false);
  /** Seconds before the server accepts another question. */
  readonly cooldown = input(0);
  readonly offline = input(false);
  /** Why sending is not possible right now (loading, conversation missing...). Null when it is. */
  readonly blockedReason = input<string | null>(null);
  readonly versionLabel = input.required<string>();
  readonly version = input.required<ApiVersion>();
  /** An existing conversation stays on the version it started with. */
  readonly versionLocked = input(false);

  readonly versionChange = output<ApiVersion>();
  readonly valueChange = output<string>();
  readonly submitted = output<string>();
  readonly stop = output<void>();

  private readonly area = viewChild.required<ElementRef<HTMLTextAreaElement>>('area');

  protected readonly max = MAX_QUESTION_CHARS;
  protected readonly length = computed(() => this.value().trim().length);
  protected readonly over = computed(() => this.length() - this.max);
  protected readonly showCounter = computed(() => this.length() >= this.max * 0.7);

  protected readonly canSend = computed(
    () =>
      this.length() > 0 &&
      this.over() <= 0 &&
      !this.busy() &&
      this.cooldown() === 0 &&
      !this.offline() &&
      !this.blockedReason(),
  );

  protected readonly status = computed(() => {
    if (this.offline()) return 'You are offline. Reconnect to send your question.';
    if (this.cooldown() > 0) return `The server asked you to wait. You can send again in ${formatDuration(this.cooldown())}.`;
    return this.blockedReason();
  });

  protected readonly format = formatNumber;
  protected readonly versions = API_VERSIONS;
  protected readonly label = versionLabel;

  constructor() {
    // Grows with the text up to a limit, then scrolls. Runs after the value is rendered.
    effect(() => {
      this.value();
      const el = this.area().nativeElement;
      queueMicrotask(() => {
        el.style.height = 'auto';
        el.style.height = `${Math.min(el.scrollHeight + 2, 220)}px`;
      });
    });
  }

  focus(): void {
    this.area().nativeElement.focus();
  }

  protected onInput(event: Event): void {
    this.valueChange.emit((event.target as HTMLTextAreaElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter sends; Shift+Enter is a new line. Enter that confirms an IME candidate (Japanese, Chinese...) must not send.
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing || event.keyCode === 229) return;
    event.preventDefault();
    this.submit();
  }

  protected submit(): void {
    if (this.canSend()) this.submitted.emit(this.value());
  }
}
