import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { configureTestApp, openPage, textOf, type } from '../../../testing/test-app';
import { SearchPage } from './search.page';

describe('SearchPage against the mock backend', () => {
  beforeEach(() => {
    configureTestApp();
  });
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('starts empty and cannot run an empty question', async () => {
    const page = await openPage('/search?version=v3', SearchPage);
    expect(page.el.querySelector('.results')).toBeNull();
    expect((page.el.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('runs the search of the address on arrival and shows the sources and what was left out', async () => {
    const page = await openPage('/search?version=v3&q=How%20do%20I%20select%20seats%20in%20V3%3F', SearchPage);
    await page.until(() => expect(page.el.querySelectorAll('.sources li').length).toBeGreaterThan(0));
    expect((page.el.querySelector('textarea') as HTMLTextAreaElement).value).toBe('How do I select seats in V3?');
    const first = textOf(page.el.querySelector('.sources li'));
    expect(first).toMatch(/^S1.*part \d+.*\d+%.*\d+ tokens/);
    expect(textOf(page.el.querySelector('.summary'))).toMatch(/\d+ found, \d+ kept, \d+ left out/);
    expect(page.el.querySelectorAll('tbody tr').length).toBeGreaterThan(0);
    expect(textOf(page.el.querySelector('tbody'))).toMatch(/Below the minimum score|Almost the same text|context budget/);
  });

  it('puts a typed question in the address, and runs it', async () => {
    const page = await openPage('/search?version=v2', SearchPage);
    type(page.el.querySelector('textarea')!, 'How do I authenticate with the subscription key?');
    await page.settle();
    (page.el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await page.until(() => expect(page.el.querySelectorAll('.sources li').length).toBeGreaterThan(0));
    expect(TestBed.inject(Router).url).toContain('version=v2');
    expect(decodeURIComponent(TestBed.inject(Router).url)).toContain('q=How do I authenticate');
    expect(textOf(page.el.querySelector('.sources'))).toContain('NDC Portal APIs V2');
  });

  it('explains when nothing in the documentation is relevant', async () => {
    const page = await openPage('/search?version=v3&q=What%20is%20the%20capital%20of%20France%3F', SearchPage);
    await page.until(() => expect(textOf(page.el.querySelector('.empty'))).toContain('scored high enough'));
    expect(page.el.querySelectorAll('.sources li')).toHaveLength(0);
    expect(textOf(page.el.querySelector('tbody'))).toContain('Below the minimum score');
  });

  it('shows the API problem for a question that is too long', async () => {
    const page = await openPage('/search?version=v3&q=' + 'a'.repeat(3600), SearchPage);
    await page.until(() => expect(textOf(page.el.querySelector('.notice'))).toContain('Question too long'));
  });
});
