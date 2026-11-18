import { Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { MockBackend } from '../../core/mock/mock-backend';
import { byText, configureTestApp, openPage, OpenedPage, textOf, type } from '../../../testing/test-app';
import { ChatPage } from './chat.page';

describe('ChatPage against the mock backend', () => {
  let backend: MockBackend;

  beforeEach(() => {
    backend = configureTestApp();
  });
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  const send = async (page: OpenedPage<ChatPage>, text: string) => {
    type(page.el.querySelector('textarea')!, text);
    await page.settle();
    (page.el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await page.settle();
  };

  it('opens a new conversation with example questions and the version chosen in the address', async () => {
    const page = await openPage('/chat/v2', ChatPage);
    expect(textOf(page.el.querySelector('.empty h2'))).toBe('Ask about the V2 NDC Portal APIs');
    const starters = [...page.el.querySelectorAll('.starter')].map(textOf);
    expect(starters).toHaveLength(4);
    expect(starters[0]).toContain('booking flow');
    expect(page.el.querySelector('.segmented [aria-pressed=true]')?.textContent?.trim()).toBe('V2');
  });

  it('puts an example question into the box when it is clicked, and enables Send', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    (page.el.querySelector('.starter') as HTMLButtonElement).click();
    await page.settle();
    expect((page.el.querySelector('textarea') as HTMLTextAreaElement).value).toContain('What changed in V3');
    expect((page.el.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('shows the question, then the answer with its sources, and moves the address to the new conversation', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'How do I select seats in V3?');

    await page.until(() => expect(page.el.querySelector('article.answer')).not.toBeNull());
    expect(textOf(page.el.querySelector('article.question'))).toContain('How do I select seats in V3?');
    const answer = page.el.querySelector('article.answer')!;
    expect(textOf(answer)).toContain('According to the V3 documentation');
    expect(textOf(answer)).not.toContain('Sources:');

    const sources = [...answer.querySelectorAll('.sources li')].map(textOf);
    expect(sources.length).toBeGreaterThan(0);
    expect(sources[0]).toMatch(/^Part \d+\S/); // the part number, then the document title in its own line
    expect(textOf(answer.querySelector('.meta'))).toMatch(/\d+ in, \d+ out/);

    // The first answer gives the conversation its id: the address changes, the page stays.
    await page.until(() => expect(TestBed.inject(Router).url).toMatch(/^\/chat\/v3\/[0-9a-f-]{36}$/));
    // The browser remembers the conversation for the list.
    expect(textOf(page.el.querySelector('.convos li.active .c-title'))).toBe('How do I select seats in V3?');
  });

  it('links each answer to the search check of its own question', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'How do I select seats in V3?');
    await page.until(() => expect(page.el.querySelector('article.answer')).not.toBeNull());
    const link = byText(page.el, 'article.answer a', 'Check retrieval') as HTMLAnchorElement;
    const url = new URL(link.href, 'http://x');
    expect(url.pathname).toBe('/search');
    expect(url.searchParams.get('q')).toBe('How do I select seats in V3?');
    expect(url.searchParams.get('version')).toBe('v3');
  });

  it('answers "not covered" for a question outside the documentation, and warns that nothing was cited', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'What is the capital of France?');
    await page.until(() => expect(page.el.querySelector('article.answer')).not.toBeNull());
    const answer = page.el.querySelector('article.answer')!;
    expect(textOf(answer)).toContain("couldn't find this in the V3 NDC Portal documentation");
    expect(textOf(answer)).toContain('No documentation section was cited');
    expect(textOf(answer.querySelector('.meta'))).toContain('0 tokens used');
  });

  it('keeps the thread when a follow-up is sent, and the follow-up finds its sources through the earlier question', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'How do I select seats in V3?');
    await page.until(() => expect(page.el.querySelectorAll('article.answer')).toHaveLength(1));
    await send(page, 'What about children?');
    await page.until(() => expect(page.el.querySelectorAll('article.answer')).toHaveLength(2));
    expect(page.el.querySelectorAll('article.question')).toHaveLength(2);
    expect(page.el.querySelectorAll('article.answer')[1].querySelectorAll('.sources li').length).toBeGreaterThan(0);
  });

  it('a busy model service shows a countdown and keeps the question so it can be edited', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'How do I select seats in V3? mock:503');
    await page.until(() => expect(page.el.querySelector('article.question.failed')).not.toBeNull());

    const notice = textOf(page.el.querySelector('article.question .notice'));
    expect(notice).toContain('Model unavailable');
    expect(notice).toContain('5 s');
    expect(textOf(page.el.querySelector('button[type=submit]'))).toMatch(/Wait \d s/);
    expect((page.el.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
    expect(byText(page.el, 'article.question button', 'Retry in')).not.toBeNull();

    byText(page.el, 'article.question button', 'Edit message')!.click();
    await page.settle();
    expect(page.el.querySelector('article.question')).toBeNull();
    expect((page.el.querySelector('textarea') as HTMLTextAreaElement).value).toContain('mock:503');
  });

  it('a model error can be sent again straight away', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'How do I select seats in V3? mock:502');
    await page.until(() => expect(page.el.querySelector('article.question.failed')).not.toBeNull());
    expect(textOf(page.el.querySelector('article.question .notice'))).toContain('The model request failed');
    expect(textOf(page.el.querySelector('article.question .notice'))).toContain('API key not valid');
    expect(byText(page.el, 'article.question button', 'Send again')).not.toBeNull();
  });

  it('shows an empty model answer as a retryable error', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    await send(page, 'How do I select seats in V3? mock:empty');
    await page.until(() => expect(textOf(page.el.querySelector('article.question .notice'))).toContain('Empty answer'));

  });

  it('a lost connection reads as "Cannot reach the server"', async () => {
    const page = await openPage('/chat/v2', ChatPage);
    await send(page, 'authenticate subscription key mock:network');
    await page.until(() => expect(textOf(page.el.querySelector('article.question .notice'))).toContain('Cannot reach the server'));
  });

  it('refuses a question that is too long before sending it, and counts the characters', async () => {
    const page = await openPage('/chat/v3', ChatPage);
    type(page.el.querySelector('textarea')!, 'a'.repeat(3600));
    await page.settle();
    expect(textOf(page.el.querySelector('.foot .problem'))).toContain('100 characters over the limit');
    expect((page.el.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('reopens a conversation from the server by its address', async () => {
    const first = await openPage('/chat/v3', ChatPage);
    await send(first, 'How do I select seats in V3?');
    await first.until(() => expect(TestBed.inject(Router).url).toMatch(/^\/chat\/v3\/[0-9a-f-]{36}$/));
    const url = TestBed.inject(Router).url;

    // A fresh app (a reload) with the same mock data, as the browser would have it.
    TestBed.resetTestingModule();
    configureTestApp(backend);
    const reopened = await openPage(url, ChatPage);
    await reopened.until(() => expect(reopened.el.querySelectorAll('article.answer')).toHaveLength(1));
    expect(textOf(reopened.el.querySelector('article.question'))).toContain('How do I select seats in V3?');
    expect(textOf(reopened.el.querySelector('.badge-signal'))).toBe('V3 documentation');
  });

  it('says so when a conversation does not exist, and offers a new one', async () => {
    const page = await openPage('/chat/v3/99999999-0000-4000-8000-000000000000', ChatPage);
    await page.until(() => expect(textOf(page.el.querySelector('.notice'))).toContain('Conversation not found'));
    expect(byText(page.el, '.notice a', 'Start a new conversation')).not.toBeNull();
  });

  it('shows a clear message for a link with a version that does not exist', async () => {
    const page = await openPage('/chat/v9', ChatPage);
    expect(textOf(page.el.querySelector('.notice'))).toContain('There is no “v9” documentation');
  });
});
