import { ChangeDetectionStrategy, Component, inject, Injector } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConfirmService } from './core/ui/confirm.service';
import { OnlineService } from './core/ui/online.service';
import { ThemeService } from './core/ui/theme.service';
import { ToastService } from './core/ui/toast.service';
import { ModalComponent } from './shared/modal/modal.component';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly online = inject(OnlineService);
  protected readonly theme = inject(ThemeService);
  protected readonly toasts = inject(ToastService);
  protected readonly confirm = inject(ConfirmService);
  protected readonly mock = environment.mock;
  private readonly injector = inject(Injector);

  protected async resetMock(): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Reset the mock data?',
      message: 'The documents go back to the ones the mock starts with, and the conversations of this browser are forgotten.',
      confirmLabel: 'Reset',
      danger: true,
    });
    if (!ok) return;
    environment.resetMock?.(this.injector);
  }

  protected readonly themeLabel = () => {
    const c = this.theme.choice();
    return c === 'system' ? 'Theme: follows your system' : `Theme: ${c}`;
  };
}
