/**
 * A small Markdown parser for model answers. It produces a typed tree that Angular renders with normal text
 * bindings, so there is no innerHTML and no HTML sanitizer: whatever the model (or a prompt injection inside the
 * documentation) writes can only ever become text, a code block, or a link with an allowed scheme.
 *
 * Supported: headings, paragraphs, fenced code (also unterminated, as in a cut-off answer), bullet and numbered
 * lists with nesting, block quotes, tables, rules, `code`, **bold**, *italic*, and [links](https://...).
 * Not supported on purpose: raw HTML, images, footnotes.
 */

export interface Inline {
  text: string;
  bold?: true;
  italic?: true;
  code?: true;
  href?: string;
}

export interface ListItem {
  inline: Inline[];
  blocks: Block[];
}

export type Align = 'left' | 'center' | 'right' | null;

export type Block =
  | { t: 'p'; inline: Inline[] }
  | { t: 'h'; level: number; inline: Inline[] }
  | { t: 'code'; lang: string; text: string }
  | { t: 'list'; ordered: boolean; start: number; items: ListItem[] }
  | { t: 'quote'; blocks: Block[] }
  | { t: 'table'; head: Inline[][]; align: Align[]; rows: Inline[][][] }
  | { t: 'hr' };

/** Blocks longer than this are shown as plain text: it bounds the work an adversarial answer can cause. */
const MAX_INLINE_CHARS = 8000;
const MAX_NESTING = 8;

const FENCE = /^ {0,3}(`{3,}|~{3,})\s*([^\s`]*)[^`]*$/;
const HEADING = /^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/;
const HR = /^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$/;
const QUOTE = /^ {0,3}>/;
const ITEM = /^( *)([-*+]|\d{1,9}[.)])[ \t]+(.*)$/;
const TABLE_DELIMITER = /^[ \t]*\|?[ \t]*:?-+:?[ \t]*(?:\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$/;

export function parseMarkdown(source: string): Block[] {
  const lines = source.replace(/\r\n?/g, '\n').replace(/\t/g, '    ').split('\n');
  return parseBlocks(lines, 0);
}

function indentOf(line: string): number {
  let n = 0;
  while (n < line.length && line[n] === ' ') n++;
  return n;
}

const isBlank = (line: string) => line.trim().length === 0;

function startsBlock(line: string, next: string | undefined): boolean {
  return FENCE.test(line) || HEADING.test(line) || HR.test(line) || QUOTE.test(line) || ITEM.test(line) || isTableStart(line, next);
}

function isTableStart(line: string, next: string | undefined): boolean {
  return next !== undefined && line.includes('|') && next.includes('-') && TABLE_DELIMITER.test(next);
}

function parseBlocks(lines: string[], depth: number): Block[] {
  const blocks: Block[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    if (isBlank(line)) {
      i++;
      continue;
    }

    const fence = FENCE.exec(line);
    if (fence) {
      const marker = fence[1];
      const body: string[] = [];
      i++;
      // The closing fence must use the same character and be at least as long. Unterminated: runs to the end.
      while (i < lines.length && !new RegExp(`^ {0,3}\\${marker[0]}{${marker.length},}[ \\t]*$`).test(lines[i])) {
        body.push(lines[i]);
        i++;
      }
      i++; // the closing fence
      blocks.push({ t: 'code', lang: fence[2].slice(0, 24), text: body.join('\n') });
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      blocks.push({ t: 'h', level: heading[1].length, inline: parseInline(heading[2]) });
      i++;
      continue;
    }

    if (HR.test(line)) {
      blocks.push({ t: 'hr' });
      i++;
      continue;
    }

    if (isTableStart(line, lines[i + 1])) {
      const table = parseTable(lines, i);
      blocks.push(table.block);
      i = table.next;
      continue;
    }

    if (QUOTE.test(line) && depth < MAX_NESTING) {
      const inner: string[] = [];
      while (i < lines.length && QUOTE.test(lines[i])) {
        inner.push(lines[i].replace(/^ {0,3}> ?/, ''));
        i++;
      }
      blocks.push({ t: 'quote', blocks: parseBlocks(inner, depth + 1) });
      continue;
    }

    if (ITEM.test(line) && depth < MAX_NESTING) {
      const list = parseList(lines, i, depth);
      blocks.push(list.block);
      i = list.next;
      continue;
    }

    // Paragraph: runs until a blank line or the start of another block. Soft line breaks become spaces.
    const paragraph: string[] = [line.trim()];
    i++;
    while (i < lines.length && !isBlank(lines[i]) && !startsBlock(lines[i], lines[i + 1])) {
      paragraph.push(lines[i].trim());
      i++;
    }
    blocks.push({ t: 'p', inline: parseInline(paragraph.join(' ')) });
  }
  return blocks;
}

function parseList(lines: string[], start: number, depth: number): { block: Block; next: number } {
  const first = ITEM.exec(lines[start])!;
  const baseIndent = first[1].length;
  const ordered = /\d/.test(first[2]);
  const items: ListItem[] = [];
  let i = start;

  while (i < lines.length) {
    const m = ITEM.exec(lines[i]);
    // A sibling is a marker of the same kind at the same level (up to one space off); anything else ends the list.
    if (!m || m[1].length < baseIndent || m[1].length >= baseIndent + 2 || /\d/.test(m[2]) !== ordered) break;

    const text: string[] = [m[3]];
    const child: string[] = [];
    i++;

    while (i < lines.length) {
      const line = lines[i];
      if (isBlank(line)) {
        // A blank line continues the item only if the next non-blank line is indented into it.
        let j = i + 1;
        while (j < lines.length && isBlank(lines[j])) j++;
        if (j < lines.length && indentOf(lines[j]) >= baseIndent + 2) {
          child.push('');
          i++;
          continue;
        }
        break;
      }
      if (indentOf(line) >= baseIndent + 2) {
        child.push(line);
        i++;
        continue;
      }
      // A less indented line with no blank before it: lazy continuation of the text, unless it starts a block.
      if (child.length === 0 && !startsBlock(line, lines[i + 1])) {
        text.push(line.trim());
        i++;
        continue;
      }
      break;
    }

    const min = child.filter((l) => !isBlank(l)).reduce((acc, l) => Math.min(acc, indentOf(l)), Infinity);
    const dedented = child.map((l) => (isBlank(l) ? '' : l.slice(Number.isFinite(min) ? min : 0)));
    items.push({ inline: parseInline(text.join(' ')), blocks: parseBlocks(dedented, depth + 1) });
  }

  const startNumber = ordered ? Number.parseInt(first[2], 10) : 1;
  return { block: { t: 'list', ordered, start: Number.isFinite(startNumber) ? startNumber : 1, items }, next: i };
}

function splitRow(line: string): string[] {
  let text = line.trim();
  if (text.startsWith('|')) text = text.slice(1);
  const cells: string[] = [];
  let current = '';
  let inCode = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (ch === '\\' && text[i + 1] === '|') {
      current += '|';
      i++;
    } else if (ch === '`') {
      inCode = !inCode;
      current += ch;
    } else if (ch === '|' && !inCode) {
      cells.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  // A trailing pipe leaves an empty last cell that is not a column.
  if (current.trim() !== '' || !text.trimEnd().endsWith('|')) cells.push(current.trim());
  return cells;
}

function parseTable(lines: string[], start: number): { block: Block; next: number } {
  const head = splitRow(lines[start]);
  const align: Align[] = splitRow(lines[start + 1]).map((c) => {
    const left = c.startsWith(':');
    const right = c.endsWith(':');
    return left && right ? 'center' : right ? 'right' : left ? 'left' : null;
  });
  const columns = head.length;
  const rows: Inline[][][] = [];
  let i = start + 2;
  while (i < lines.length && !isBlank(lines[i]) && lines[i].includes('|')) {
    const cells = splitRow(lines[i]);
    // Ragged rows are padded or cut to the header's width so the table never breaks.
    const row = Array.from({ length: columns }, (_, c) => parseInline(cells[c] ?? ''));
    rows.push(row);
    i++;
  }
  return {
    block: { t: 'table', head: head.map((c) => parseInline(c)), align: Array.from({ length: columns }, (_, c) => align[c] ?? null), rows },
    next: i,
  };
}

// ---- Inline ---------------------------------------------------------------------------------------------------

const ALLOWED_SCHEMES = /^(https?:|mailto:)/i;

/** Returns the URL if it is safe to link to, else null. Control characters and unknown schemes are refused. */
export function safeHref(url: string): string | null {
  const clean = url.trim();
  // eslint-disable-next-line no-control-regex
  if (!clean || /[\u0000-\u001f\u007f\s]/.test(clean)) return null;
  return ALLOWED_SCHEMES.test(clean) ? clean : null;
}

const isWordChar = (ch: string | undefined) => !!ch && /[\p{L}\p{N}]/u.test(ch);
const isSpace = (ch: string | undefined) => ch === undefined || /\s/.test(ch);

interface Style {
  bold?: true;
  italic?: true;
  href?: string;
}

export function parseInline(text: string): Inline[] {
  if (text.length > MAX_INLINE_CHARS) return text ? [{ text }] : [];
  const out: Inline[] = [];
  scan(text, {}, out);
  return merge(out);
}

function push(out: Inline[], text: string, style: Style, code = false): void {
  if (!text) return;
  const seg: Inline = { text };
  if (style.bold) seg.bold = true;
  if (style.italic) seg.italic = true;
  if (style.href) seg.href = style.href;
  if (code) seg.code = true;
  out.push(seg);
}

function merge(list: Inline[]): Inline[] {
  const out: Inline[] = [];
  for (const seg of list) {
    const last = out[out.length - 1];
    if (last && !last.code && !seg.code && last.bold === seg.bold && last.italic === seg.italic && last.href === seg.href) {
      last.text += seg.text;
    } else {
      out.push({ ...seg });
    }
  }
  return out;
}

function findClosing(text: string, from: number, marker: string): number {
  for (let j = from; j <= text.length - marker.length; j++) {
    if (text[j] === '\\') {
      j++;
      continue;
    }
    if (text[j] === '`') {
      // Skip a code span so a marker inside it cannot close the emphasis.
      const run = /^`+/.exec(text.slice(j))![0];
      const close = text.indexOf(run, j + run.length);
      if (close !== -1) {
        j = close + run.length - 1;
        continue;
      }
    }
    if (text.startsWith(marker, j) && !isSpace(text[j - 1])) return j;
  }
  return -1;
}

function scan(text: string, style: Style, out: Inline[]): void {
  let plain = '';
  const flush = () => {
    push(out, plain, style);
    plain = '';
  };

  for (let i = 0; i < text.length; ) {
    const ch = text[i];

    if (ch === '\\' && i + 1 < text.length && /[\\`*_{}[\]()#+\-.!|>~]/.test(text[i + 1])) {
      plain += text[i + 1];
      i += 2;
      continue;
    }

    if (ch === '`') {
      const run = /^`+/.exec(text.slice(i))![0];
      const close = text.indexOf(run, i + run.length);
      if (close !== -1) {
        flush();
        let code = text.slice(i + run.length, close);
        if (code.length > 2 && code.startsWith(' ') && code.endsWith(' ') && code.trim()) code = code.slice(1, -1);
        push(out, code, style, true);
        i = close + run.length;
        continue;
      }
      plain += run;
      i += run.length;
      continue;
    }

    if (ch === '[') {
      const link = readLink(text, i);
      if (link) {
        flush();
        const href = safeHref(link.url);
        if (href && !style.href) scan(link.label, { ...style, href }, out);
        else scan(link.label, style, out); // an unsafe scheme keeps the words and drops the link
        i = link.end;
        continue;
      }
    }

    if ((ch === '*' || ch === '_') && text[i + 1] === ch && !isSpace(text[i + 2])) {
      const marker = ch + ch;
      const canOpen = ch === '*' || !isWordChar(text[i - 1]);
      const close = canOpen ? findClosing(text, i + 2, marker) : -1;
      if (close !== -1 && (ch === '*' || !isWordChar(text[close + 2]))) {
        flush();
        scan(text.slice(i + 2, close), { ...style, bold: true }, out);
        i = close + 2;
        continue;
      }
    }

    if ((ch === '*' || ch === '_') && text[i + 1] !== ch && !isSpace(text[i + 1])) {
      // Underscores inside words (snake_case identifiers such as offer_id) are never emphasis.
      const canOpen = ch === '*' || !isWordChar(text[i - 1]);
      const close = canOpen ? findClosing(text, i + 1, ch) : -1;
      if (close !== -1 && text[close + 1] !== ch && (ch === '*' || !isWordChar(text[close + 1]))) {
        flush();
        scan(text.slice(i + 1, close), { ...style, italic: true }, out);
        i = close + 1;
        continue;
      }
    }

    if (ch === 'h' && (text.startsWith('http://', i) || text.startsWith('https://', i)) && !isWordChar(text[i - 1])) {
      const match = /^https?:\/\/[^\s<>]+/.exec(text.slice(i))!;
      // Sentence punctuation after a bare URL is not part of it.
      const url = match[0].replace(/[.,;:!?)\]'"]+$/, '');
      const href = safeHref(url);
      if (href && !style.href && url.length > 8) {
        flush();
        push(out, url, { ...style, href });
        i += url.length;
        continue;
      }
    }

    plain += ch;
    i++;
  }
  flush();
}

/** [label](url) with balanced parentheses in the url and an optional "title". */
function readLink(text: string, start: number): { label: string; url: string; end: number } | null {
  let depth = 0;
  let close = -1;
  for (let i = start; i < text.length; i++) {
    if (text[i] === '\\') {
      i++;
      continue;
    }
    if (text[i] === '[') depth++;
    else if (text[i] === ']' && --depth === 0) {
      close = i;
      break;
    }
  }
  if (close === -1 || text[close + 1] !== '(') return null;

  let parens = 0;
  for (let j = close + 1; j < text.length; j++) {
    if (text[j] === '(') parens++;
    else if (text[j] === ')' && --parens === 0) {
      const target = text.slice(close + 2, j).trim();
      const url = target.replace(/\s+["'][^"']*["']$/, '').replace(/^<(.*)>$/, '$1');
      return { label: text.slice(start + 1, close), url, end: j + 1 };
    }
  }
  return null;
}

/** Plain text of inline segments (used for copy and for tests). */
export function inlineText(list: Inline[]): string {
  return list.map((s) => s.text).join('');
}
