import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IngestReport } from '../../../core/api/models';
import { formatNumber, truncate } from '../../../core/util/format';

const PREVIEW_LIMIT = 30;

/** The validation report of a document: errors with line numbers, warnings, and (for a dry run) the chunks. */
@Component({
  selector: 'app-report-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-view.component.html',
  styleUrl: './report-view.component.css',
})
export class ReportViewComponent {
  readonly report = input.required<IngestReport>();
  protected readonly format = formatNumber;

  protected readonly preview = computed(() => this.report().chunks.slice(0, PREVIEW_LIMIT));
  protected readonly hiddenChunks = computed(() => Math.max(0, this.report().chunks.length - PREVIEW_LIMIT));
  protected readonly totalTokens = computed(() => this.report().chunks.reduce((sum, c) => sum + c.estTokens, 0));

  protected heading(text: string): string {
    return truncate(text, 90);
  }
}
