import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiVersion, versionLabel } from '../../core/api/models';
import { ClipboardService } from '../../core/ui/clipboard.service';
import { ToastService } from '../../core/ui/toast.service';
import { formatDuration, formatNumber, formatTime } from '../../core/util/format';
import { ErrorNoticeComponent } from '../../shared/error-notice/error-notice.component';
import { MarkdownComponent } from '../../shared/markdown/markdown.component';
import { ChatMessage } from './conversation-state';

@Component({
  selector: 'app-message',
  imports: [MarkdownComponent, ErrorNoticeComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message.component.html',
  styleUrl: './message.component.css',
})
export class MessageComponent {
  readonly message = input.required<ChatMessage>();
  readonly version = input.required<ApiVersion>();
  /** The question this answer replies to, so its retrieval can be opened in the Search check. */
  readonly question = input<string | null>(null);
  /** Seconds the pending question has waited (only meaningful for the pending message). */
  readonly waitingSec = input(0);
  readonly cooldownLeft = input(0);
  /** Another question of this conversation is in flight, so retrying now would double up. */
  readonly busy = input(false);

  readonly retry = output<void>();
  readonly edit = output<void>();
  readonly discard = output<void>();
  readonly askInNewConversation = output<void>();
  readonly stop = output<void>();

  private readonly clipboard = inject(ClipboardService);
  private readonly toast = inject(ToastService);

  protected readonly copied = signal(false);
  protected readonly label = computed(() => versionLabel(this.version()));
  protected readonly time = computed(() => formatTime(this.message().createdAt));
  protected readonly format = formatNumber;
  protected readonly duration = formatDuration;

  protected readonly usageText = computed(() => {
    const u = this.message().usage;
    if (!u || u.cacheHit || (!u.inputTokens && !u.outputTokens)) return null;
    const parts = [`${formatNumber(u.inputTokens)} in`, `${formatNumber(u.outputTokens)} out`];
    if (u.thinkingTokens) parts.push(`${formatNumber(u.thinkingTokens)} reasoning`);
    return parts.join(', ');
  });

  /** The conversation is gone: only a new one can carry this question. */
  protected readonly conversationGone = computed(() => this.message().error?.reason === 'conversation_not_found');
  protected readonly canRetry = computed(() => {
    const e = this.message().error;
    return !!e && e.retryable && !this.conversationGone();
  });

  protected async copy(): Promise<void> {
    if (await this.clipboard.copy(this.message().content)) {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1800);
    } else {
      this.toast.error('Could not copy', 'Select the text and copy it with the keyboard instead.');
    }
  }
}
