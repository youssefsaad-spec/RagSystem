import { Block, inlineText, parseInline, parseMarkdown, safeHref } from './markdown';

const first = (src: string): Block => parseMarkdown(src)[0];

describe('parseMarkdown', () => {
  it('parses headings and paragraphs, joining soft line breaks', () => {
    const [h, p] = parseMarkdown('## Title\n\nline one\nline two');
    expect(h).toMatchObject({ t: 'h', level: 2 });
    expect(p.t === 'p' && inlineText(p.inline)).toBe('line one line two');
  });

  it('parses a fenced code block and keeps its text verbatim', () => {
    const b = first('```json\n{ "a": 1 }\n\n  indented\n```');
    expect(b).toEqual({ t: 'code', lang: 'json', text: '{ "a": 1 }\n\n  indented' });
  });

  it('keeps an unterminated fence as code (a cut-off answer)', () => {
    const blocks = parseMarkdown('Intro\n\n```ts\nconst x = 1;');
    expect(blocks[1]).toEqual({ t: 'code', lang: 'ts', text: 'const x = 1;' });
  });

  it('does not close a fence early on a shorter fence', () => {
    const b = first('````\n```\ninner\n```\n````');
    expect(b.t === 'code' && b.text).toBe('```\ninner\n```');
  });

  it('parses nested bullet lists', () => {
    const b = first('- a\n  - a1\n  - a2\n- b');
    expect(b.t).toBe('list');
    if (b.t !== 'list') return;
    expect(b.items).toHaveLength(2);
    expect(b.items[0].blocks[0]).toMatchObject({ t: 'list', ordered: false });
    expect(b.items[0].blocks[0].t === 'list' && b.items[0].blocks[0].items).toHaveLength(2);
  });

  it('parses numbered lists and remembers the start number', () => {
    const b = first('3. three\n4. four');
    expect(b).toMatchObject({ t: 'list', ordered: true, start: 3 });
  });

  it('parses a table with alignment and pads ragged rows', () => {
    const b = first('| Name | Qty |\n|:--|--:|\n| a | 1 |\n| b |');
    expect(b.t).toBe('table');
    if (b.t !== 'table') return;
    expect(b.align).toEqual(['left', 'right']);
    expect(b.rows).toHaveLength(2);
    expect(b.rows[1]).toHaveLength(2);
  });

  it('does not split a table cell on a pipe inside code', () => {
    const b = first('| a | b |\n|--|--|\n| `x|y` | z |');
    expect(b.t === 'table' && b.rows[0][0][0].text).toBe('x|y');
  });

  it('parses block quotes and rules', () => {
    const [q, hr] = parseMarkdown('> quoted\n> more\n\n---');
    expect(q.t).toBe('quote');
    expect(hr.t).toBe('hr');
  });

  it('returns nothing for empty input', () => {
    expect(parseMarkdown('')).toEqual([]);
    expect(parseMarkdown('\n\n  \n')).toEqual([]);
  });

  it('handles Windows line endings', () => {
    expect(parseMarkdown('# a\r\n\r\ntext\r\n')).toHaveLength(2);
  });

  it('survives deep nesting and huge input quickly', () => {
    const deep = Array.from({ length: 200 }, (_, i) => `${'  '.repeat(i)}- level ${i}`).join('\n');
    expect(() => parseMarkdown(deep)).not.toThrow();
    const started = performance.now();
    parseMarkdown('*'.repeat(50_000) + '\n\n' + '_a '.repeat(20_000) + '\n\n' + '[x'.repeat(20_000));
    expect(performance.now() - started).toBeLessThan(2000);
  });
});

describe('parseInline', () => {
  it('parses bold, italic and code', () => {
    const s = parseInline('a **b** *c* `d`');
    expect(s.map((x) => [x.text, !!x.bold, !!x.italic, !!x.code])).toEqual([
      ['a ', false, false, false],
      ['b', true, false, false],
      [' ', false, false, false],
      ['c', false, true, false],
      [' ', false, false, false],
      ['d', false, false, true],
    ]);
  });

  it('never treats underscores inside identifiers as emphasis', () => {
    expect(inlineText(parseInline('send offer_id and order_id_2 now'))).toBe('send offer_id and order_id_2 now');
    expect(parseInline('offer_id and order_id').every((s) => !s.italic)).toBe(true);
  });

  it('keeps unmatched markers as text', () => {
    expect(inlineText(parseInline('2 * 3 and **open'))).toBe('2 * 3 and **open');
  });

  it('parses links with an https target', () => {
    const [link] = parseInline('[docs](https://example.com/a_(b))');
    expect(link).toMatchObject({ text: 'docs', href: 'https://example.com/a_(b)' });
  });

  it('drops javascript: and data: links but keeps their words', () => {
    for (const url of ['javascript:alert(1)', 'JaVaScRiPt:alert(1)', 'data:text/html,<script>', 'vbscript:x', ' javascript:alert(1)']) {
      const segs = parseInline(`[click me](${url})`);
      expect(segs.every((s) => !s.href)).toBe(true);
      expect(inlineText(segs)).toBe('click me');
    }
  });

  it('links bare URLs without the trailing punctuation', () => {
    const segs = parseInline('see https://example.com/x.');
    expect(segs.find((s) => s.href)?.href).toBe('https://example.com/x');
    expect(inlineText(segs)).toBe('see https://example.com/x.');
  });

  it('leaves raw HTML as text', () => {
    const segs = parseInline('<img src=x onerror=alert(1)> <script>x</script>');
    expect(inlineText(segs)).toBe('<img src=x onerror=alert(1)> <script>x</script>');
    expect(segs.every((s) => !s.href)).toBe(true);
  });

  it('honours backslash escapes', () => {
    expect(inlineText(parseInline('\\*not italic\\*'))).toBe('*not italic*');
  });

  it('does not close emphasis inside a code span', () => {
    const segs = parseInline('**a `**` b**');
    expect(inlineText(segs)).toBe('a ** b');
  });
});

describe('safeHref', () => {
  it('allows http, https and mailto only', () => {
    expect(safeHref('https://a.b')).toBe('https://a.b');
    expect(safeHref('mailto:a@b.c')).toBe('mailto:a@b.c');
    expect(safeHref('/relative')).toBeNull();
    expect(safeHref('javascript:alert(1)')).toBeNull();
    expect(safeHref('https://a.b/with space')).toBeNull();
    expect(safeHref('https://a.b/\u0000')).toBeNull();
  });
});
