import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page narrow">
      <h1>This page does not exist</h1>
      <p class="muted">The address may be mistyped, or the page may have moved.</p>
      <p><a class="btn btn-primary" routerLink="/chat">Go to the chat</a></p>
    </main>
  `,
  styles: `
    .narrow {
      max-width: 520px;
      padding-top: 12vh;
    }
  `,
})
export class NotFoundPage {}
