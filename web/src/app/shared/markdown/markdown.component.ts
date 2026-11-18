import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ClipboardService } from '../../core/ui/clipboard.service';
import { ToastService } from '../../core/ui/toast.service';
import { parseMarkdown } from './markdown';

/** Renders model output. See markdown.ts for why this never uses innerHTML. */
@Component({
  selector: 'app-markdown',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './markdown.component.html',
  styleUrl: './markdown.component.css',
})
export class MarkdownComponent {
  readonly source = input.required<string>();

  private readonly clipboard = inject(ClipboardService);
  private readonly toast = inject(ToastService);

  protected readonly blocks = computed(() => {
    try {
      return parseMarkdown(this.source());
    } catch {
      // The parser is defensive, but a rendering bug must never hide the answer: fall back to plain text.
      return [{ t: 'p' as const, inline: [{ text: this.source() }] }];
    }
  });

  /** The text of the code block that was just copied (shows "Copied" on its button). */
  protected readonly copied = signal<string | null>(null);
  private copiedTimer: ReturnType<typeof setTimeout> | undefined;

  protected async copyCode(text: string): Promise<void> {
    const ok = await this.clipboard.copy(text);
    if (!ok) {
      this.toast.error('Could not copy', 'Select the code and copy it with the keyboard instead.');
      return;
    }
    this.copied.set(text);
    clearTimeout(this.copiedTimer);
    this.copiedTimer = setTimeout(() => this.copied.set(null), 1800);
  }

  protected headingTag(level: number): 'h3' | 'h4' | 'h5' {
    return level <= 2 ? 'h3' : level === 3 ? 'h4' : 'h5';
  }
}
