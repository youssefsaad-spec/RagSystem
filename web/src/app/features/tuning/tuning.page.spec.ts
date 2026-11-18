import { byText, configureTestApp, openPage, OpenedPage, textOf, type } from '../../../testing/test-app';
import { TuningPage } from './tuning.page';

describe('TuningPage against the mock backend', () => {
  beforeEach(() => {
    configureTestApp();
  });
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  const run = async (page: OpenedPage<TuningPage>) => {
    (byText(page.el, '.controls button', /^Run /) as HTMLButtonElement).click();
    await page.settle();
  };
  const rows = (page: OpenedPage<TuningPage>) => [...page.el.querySelectorAll('tbody tr:not(.detail-row)')];

  it('loads examples for the version and counts the questions on the Run button', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    expect((byText(page.el, '.controls button', 'Run') as HTMLButtonElement).disabled).toBe(true);
    byText(page.el, '.controls button', 'Load examples')!.click();
    await page.settle();
    expect(textOf(byText(page.el, '.controls button', 'Run'))).toBe('Run 6 questions');
    expect((page.el.querySelector('textarea') as HTMLTextAreaElement).value).toContain("=> What's New");
  });

  it('runs every question and fills the summary and the table', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    byText(page.el, '.controls button', 'Load examples')!.click();
    await page.settle();
    await run(page);
    await page.until(() => expect(textOf(page.el.querySelector('.tiles'))).toMatch(/6 \/ 6\s*questions searched/));

    expect(rows(page)).toHaveLength(6);
    const france = rows(page).find((r) => textOf(r).includes('capital of France'))!;
    // Nothing kept for the question the documentation does not cover, and that is what it was expected to do.
    expect(textOf(france)).toContain('nothing found, as expected');
    const seats = rows(page).find((r) => textOf(r).includes('select seats'))!;
    expect(textOf(seats)).toContain('found as S'); // the badge says Found, then the outcome in words
    expect(textOf(page.el.querySelector('.tiles'))).toMatch(/4 \/ 4\s*found the expected source/); // every example finds what it was written for
  });

  it('reports a source that was expected but not found, with the reason it was lost', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    type(page.el.querySelector('textarea')!, 'How do I select seats in V3? => Chapter that does not exist');
    await page.settle();
    await run(page);
    await page.until(() => expect(textOf(rows(page)[0])).toContain('Missed'));
    expect(textOf(rows(page)[0])).toContain('not among the candidates');
    expect(page.el.querySelector('.tile-bad')).not.toBeNull();
  });

  it('a different minimum score changes how many questions would be answered, without a new search', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    type(page.el.querySelector('textarea')!, 'What is the capital of France?');
    await page.settle();
    await run(page);
    await page.until(() => expect(textOf(page.el.querySelector('.tiles'))).toMatch(/1 \/ 1\s*questions searched/));
    const tile = () => textOf([...page.el.querySelectorAll('.tile')].find((t) => textOf(t).includes('would be answered at')));
    expect(tile()).toMatch(/^0\s*would be answered at 60%/);

    const input = page.el.querySelector('#tuning-threshold') as HTMLInputElement;
    input.value = '0.2';
    input.dispatchEvent(new Event('change'));
    await page.settle();
    expect(tile()).toMatch(/^1\s*would be answered at 20%/);
  });

  it('opens the details of a question: kept sources, what was left out, and a link to the search check', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    type(page.el.querySelector('textarea')!, 'How do I select seats in V3?');
    await page.settle();
    await run(page);
    await page.until(() => expect(page.el.querySelector('tbody .link')).not.toBeNull());
    (page.el.querySelector('tbody .link') as HTMLButtonElement).click();
    await page.settle();
    const detail = textOf(page.el.querySelector('.detail-row'));
    expect(detail).toContain('Kept');
    expect(detail).toMatch(/S1 .*part \d+/);
    const link = page.el.querySelector('.detail-row a') as HTMLAnchorElement;
    expect(new URL(link.href, 'http://x').searchParams.get('q')).toBe('How do I select seats in V3?');
  });

  it('remembers the question set per version', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    type(page.el.querySelector('textarea')!, 'A v3 question');
    await page.settle();
    expect(localStorage.getItem('ragsystem.tuning.v3')).toBe('A v3 question');
    expect(localStorage.getItem('ragsystem.tuning.v2')).toBeNull();
  });

  it('stops at the first failure of the model service instead of repeating it for every question', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    // A question that is too long is a problem of that question only: the run goes on.
    type(page.el.querySelector('textarea')!, ['a'.repeat(3600), 'How do I select seats in V3?'].join('\n'));
    await page.settle();
    await run(page);
    await page.until(() => expect(textOf(page.el.querySelector('.tiles'))).toMatch(/1 \/ 2\s*questions searched/));
    expect(textOf(rows(page)[0])).toContain('The question is over the limit');
    expect(textOf(rows(page)[1])).toMatch(/\d+%/);
  });

  it('copies the results as CSV', async () => {
    const page = await openPage('/tuning?version=v3', TuningPage);
    type(page.el.querySelector('textarea')!, 'How do I select seats in V3?');
    await page.settle();
    let copied = '';
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async (t: string) => void (copied = t) }, configurable: true });
    await run(page);
    await page.until(() => expect((byText(page.el, '.results-head button', 'Copy as CSV') as HTMLButtonElement).disabled).toBe(false));
    byText(page.el, '.results-head button', 'Copy as CSV')!.click();
    await page.until(() => expect(copied).toContain('version,question,expected'));
    expect(copied.split('\n')[1]).toMatch(/^v3,How do I select seats in V3\?,,\d+,\d+,0\.\d+/);
  });
});
