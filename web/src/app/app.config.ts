import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling, withNavigationErrorHandler } from '@angular/router';
import { routes } from './app.routes';
import { apiInterceptor } from './core/api/api.interceptor';
import { environment } from '../environments/environment';
import { ToastService } from './core/ui/toast.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // In mock mode the mock backend comes after the API interceptor, so errors and timeouts behave as with the real API.
    provideHttpClient(withInterceptors([apiInterceptor, ...environment.interceptors])),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
      // Most often a lazy-loaded file that no longer exists because a new release replaced it while this tab was open.
      withNavigationErrorHandler(() => {
        inject(ToastService).show({
          tone: 'warn',
          title: 'Could not open that page',
          message: 'The app may have been updated. Reload the page and try again.',
          durationMs: 0,
        });
      }),
    ),
  ],
};
