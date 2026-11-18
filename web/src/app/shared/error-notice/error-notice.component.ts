import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ApiError } from '../../core/api/api-error';

/** Shows an ApiError the same way everywhere. Projected content is the action buttons (Retry, Edit...). */
@Component({
  selector: 'app-error-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="notice" [class.notice-error]="tone() === 'error'" [class.notice-warn]="tone() === 'warn'" [attr.role]="tone() === 'error' ? 'alert' : 'status'">
      <div class="body">
        <strong>{{ error().title }}</strong>
        <p>{{ error().message }}</p>
        @if (error().traceId) {
          <p class="ref muted">Reference <span class="mono">{{ error().traceId }}</span></p>
        }
      </div>
      <div class="actions"><ng-content /></div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .notice {
      justify-content: space-between;
      align-items: flex-start;
      flex-wrap: wrap;
    }
    .body {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
      min-width: min(100%, 16rem);
      flex: 1 1 16rem;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
      align-items: center;
    }
    .actions:empty {
      display: none;
    }
    .ref {
      font-size: 0.82rem;
    }
  `,
})
export class ErrorNoticeComponent {
  readonly error = input.required<ApiError>();

  protected readonly tone = computed(() => {
    const kind = this.error().kind;
    return kind === 'unavailable' || kind === 'cancelled' || kind === 'timeout' ? 'warn' : 'error';
  });
}
