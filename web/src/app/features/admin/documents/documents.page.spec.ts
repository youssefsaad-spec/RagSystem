import { TestBed } from '@angular/core/testing';
import { ConfirmService } from '../../../core/ui/confirm.service';
import { byText, configureTestApp, openPage, OpenedPage, textOf } from '../../../../testing/test-app';
import { DocumentsPage } from './documents.page';

const MD = ['---', 'version: v3', 'title: "Seat guide"', 'docType: guide', 'updated: 2026-11-17', '---', '', '# Seat guide', '', 'Seats are chosen by cell code.'].join('\n');

describe('DocumentsPage against the mock backend', () => {
  beforeEach(() => {
    configureTestApp();
  });
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  const pick = async (page: OpenedPage<DocumentsPage>, name: string, content: string) => {
    const input = page.el.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File([content], name)], configurable: true });
    input.dispatchEvent(new Event('change'));
    await page.settle();
  };

  const rows = (page: OpenedPage<DocumentsPage>) => [...page.el.querySelectorAll('tbody tr')].map((r) => textOf(r));

  it('lists the documents of the version in the address, with their status, chunks and failure reason', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await page.until(() => expect(rows(page)).toHaveLength(3));
    const list = rows(page);
    expect(list.some((r) => r.includes('ndc-portal-apis-v3.md') && r.includes('Ready') && r.includes('11'))).toBe(true);
    const failed = list.find((r) => r.includes('OffersV3.swagger.json'))!;
    expect(failed).toContain('Failed');
    expect(failed).toContain('Interrupted by a restart');
    expect(textOf(page.el.querySelector('.list-head'))).toContain('3 stored');
  });

  it('switches version and lists that version', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await page.until(() => expect(rows(page)).toHaveLength(3));
    byText(page.el, '.segmented button', 'V2')!.click();
    await page.until(() => expect(rows(page)).toHaveLength(1));
    expect(rows(page)[0]).toContain('ndc-portal-apis-v2.md');
  });

  it('"Check only" shows the report and the chunks, and stores nothing', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    (page.el.querySelector('input[type=checkbox]') as HTMLInputElement).click();
    await pick(page, 'seats.md', MD);
    await page.until(() => expect(textOf(page.el.querySelector('.queue'))).toContain('Checked only. Nothing was stored.'));
    const report = textOf(page.el.querySelector('.item-report'));
    expect(report).toContain('Valid');
    expect(report).toContain('Seat guide');
    expect(report).toContain('Chunks that would be stored');
    expect(rows(page)).toHaveLength(3);
  });

  it('shows the errors of a file with a wrong header, with the line, and does not store it', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await pick(page, 'bad.md', '# no header at all');
    await page.until(() => expect(textOf(page.el.querySelector('.queue'))).toContain('Rejected. The document has errors and was not stored.'));
    const report = textOf(page.el.querySelector('.item-report'));
    expect(report).toContain('Has errors');
    expect(report).toContain('frontmatter.missing');
    expect(report).toContain('line 1');
    expect(rows(page)).toHaveLength(3);
  });

  it('refuses a wrong file type before anything is sent', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await pick(page, 'notes.pdf', 'x');
    expect(textOf(page.el.querySelector('.queue'))).toContain('Unsupported file type');
    expect(textOf(page.el.querySelector('.queue .detail'))).toContain('Markdown (.md)');
  });

  it('uploads a valid file: Accepted, then Processing in the list, then Ready with its chunks', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await pick(page, 'seats.md', MD);
    await page.until(() => expect(textOf(page.el.querySelector('.queue'))).toContain('Accepted'));
    await page.until(() => expect(rows(page).find((r) => r.includes('seats.md'))).toContain('Processing'));

    // The mock's worker finishes after 50 ms here; the page polls again by itself while a document is Processing.
    await page.until(() => expect(rows(page).find((r) => r.includes('seats.md'))).toMatch(/Ready\s*1/), 6000);
    expect(textOf(page.el.querySelector('.list-head'))).toContain('4 stored');
  }, 10_000);

  it('a name containing "fail" ends Failed, and the reason is shown', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await pick(page, 'will-fail.md', MD);
    await page.until(() => expect(rows(page).find((r) => r.includes('will-fail.md'))).toContain('Failed'), 6000);
    expect(rows(page).find((r) => r.includes('will-fail.md'))).toContain('refused the request');
  }, 10_000);

  it('opens the details of a ready document with its chunks as numbered parts', async () => {
    const page = await openPage('/admin/documents?version=v3', DocumentsPage);
    await page.until(() => expect(rows(page)).toHaveLength(3));
    const open = [...page.el.querySelectorAll('tbody tr')].find((r) => textOf(r).includes('whats-new-v3.md'))!;
    (byText(open, 'button', 'Details') as HTMLButtonElement).click();
    await page.until(() => expect(page.el.querySelectorAll('app-document-detail details').length).toBeGreaterThan(0));
    const parts = [...page.el.querySelectorAll('app-document-detail details summary')].map(textOf);
    expect(parts).toHaveLength(5);
    expect(parts[0]).toMatch(/^Part 1/);
  });

  it('deletes a document after confirmation', async () => {
    const page = await openPage('/admin/documents?version=v2', DocumentsPage);
    await page.until(() => expect(rows(page)).toHaveLength(1));
    byText(page.el, 'tbody button', 'Delete')!.click();
    await page.settle();
    expect(TestBed.inject(ConfirmService).pending()?.title).toBe('Delete ndc-portal-apis-v2.md?');
    TestBed.inject(ConfirmService).answer(true);
    await page.until(() => expect(page.el.querySelector('.state-box')?.textContent).toContain('No V2 documents yet'));
  });

  it('keeps the document when the confirmation is declined', async () => {
    const page = await openPage('/admin/documents?version=v2', DocumentsPage);
    await page.until(() => expect(rows(page)).toHaveLength(1));
    byText(page.el, 'tbody button', 'Delete')!.click();
    await page.settle();
    TestBed.inject(ConfirmService).answer(false);
    await page.settle();
    expect(rows(page)).toHaveLength(1);
  });
});
