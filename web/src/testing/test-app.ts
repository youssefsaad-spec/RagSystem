import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../app/app.routes';
import { apiInterceptor } from '../app/core/api/api.interceptor';
import { MockBackend } from '../app/core/mock/mock-backend';
import { MOCK_BACKEND, mockBackendInterceptor } from '../app/core/mock/mock-backend.interceptor';

/**
 * The app as the browser runs it in mock mode: the real routes, the real API interceptor and services, and the mock
 * backend (in memory, without delays) behind them. Specs drive the pages through the DOM.
 */
export function configureTestApp(backend = new MockBackend({ latency: 0, processingMs: 50 })): MockBackend {
  // jsdom has no Element.scrollTo (every browser does); the chat page scrolls to the newest message.
  if (!Element.prototype.scrollTo) Element.prototype.scrollTo = () => undefined;
  if (!window.matchMedia) window.matchMedia = ((query: string) => ({ matches: false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined })) as never;
  // jsdom may lack the methods of the native <dialog> that the modal uses.
  if (typeof HTMLDialogElement !== 'undefined' && typeof HTMLDialogElement.prototype.showModal !== 'function') {
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    };
  }
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiInterceptor, mockBackendInterceptor])),
      provideRouter(routes, withComponentInputBinding()),
      { provide: MOCK_BACKEND, useValue: backend },
    ],
  });
  return backend;
}

export interface OpenedPage<T> {
  harness: RouterTestingHarness;
  component: T;
  el: HTMLElement;
  /** Waits until `check` stops throwing (the page needs a few turns of the event loop to receive an answer). */
  until: (check: () => void, timeoutMs?: number) => Promise<void>;
  /** Waits for pending work and renders. */
  settle: () => Promise<void>;
}

/** Navigates to `url` and returns the page that the route shows. */
export async function openPage<T>(url: string, type: Type<T>): Promise<OpenedPage<T>> {
  const harness = await RouterTestingHarness.create();
  const component = await harness.navigateByUrl(url, type);
  const settle = async () => {
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  await settle();
  return {
    harness,
    component,
    el: harness.routeNativeElement as HTMLElement,
    settle,
    until: (check, timeoutMs = 3000) =>
      vi.waitFor(async () => {
        await settle();
        check();
      }, { timeout: timeoutMs, interval: 10 }),
  };
}

/** The visible text of an element, with runs of white space collapsed. */
export const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

/** Types into a textarea or input the way a user does. */
export function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): void {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

/** Finds a button or link by its visible text. */
export function byText(root: Element, selector: string, text: string | RegExp): HTMLElement | null {
  return (
    [...root.querySelectorAll<HTMLElement>(selector)].find((e) => (typeof text === 'string' ? textOf(e).includes(text) : text.test(textOf(e)))) ?? null
  );
}
