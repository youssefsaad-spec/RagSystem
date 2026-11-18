import {
  ChangeDetectionStrategy,
  Component,
  effect,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';

/**
 * A modal on the native <dialog> element: focus is trapped, Escape closes it, the page behind is inert, and
 * focus returns to the opener. `dismissible=false` blocks Escape and backdrop clicks (used when closing would lose data).
 */
@Component({
  selector: 'app-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './modal.component.html',
  styleUrl: './modal.component.css',
})
export class ModalComponent {
  readonly open = input.required<boolean>();
  readonly heading = input.required<string>();
  readonly dismissible = input(true);
  readonly wide = input(false);
  /** Emitted when the dialog closes for any reason; the parent should set `open` to false. */
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const el = this.dialog().nativeElement;
      if (this.open() && !el.open) {
        this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        el.showModal();
      } else if (!this.open() && el.open) {
        el.close();
      }
    });
  }

  protected onCancel(event: Event): void {
    if (!this.dismissible()) event.preventDefault();
  }

  protected onClose(): void {
    this.opener?.focus?.();
    this.opener = null;
    this.closed.emit();
  }

  protected onBackdrop(event: MouseEvent): void {
    if (this.dismissible() && event.target === this.dialog().nativeElement) this.dialog().nativeElement.close();
  }

  protected requestClose(): void {
    this.dialog().nativeElement.close();
  }
}
