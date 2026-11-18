import { SearchResult } from '../../core/api/models';

/** One line of the question set: the question, and optionally the source it should find (`question => expected`). */
export interface TuningQuestion {
  text: string;
  /** Text that must appear in the citation of a kept source ("What's New", "part 4"), or `none` when nothing should be found. */
  expected: string | null;
}

export const MAX_QUESTIONS = 50;

/** Reads the question set: one question per line, `#` starts a comment, ` => ` separates the expected source. */
export function parseQuestions(raw: string): TuningQuestion[] {
  const out: TuningQuestion[] = [];
  for (const line of raw.replace(/\r\n/g, '\n').split('\n')) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) continue;
    const arrow = trimmed.indexOf('=>');
    const text = (arrow >= 0 ? trimmed.slice(0, arrow) : trimmed).trim();
    const expected = arrow >= 0 ? trimmed.slice(arrow + 2).trim() : '';
    if (text) out.push({ text, expected: expected || null });
    if (out.length === MAX_QUESTIONS) break;
  }
  return out;
}

/** Every score the search saw: the kept sources and the dropped candidates. */
export function allScores(r: SearchResult): number[] {
  return [...r.sources.map((s) => s.score), ...r.dropped.map((d) => d.score)];
}

export function bestScore(r: SearchResult): number | null {
  const scores = allScores(r);
  return scores.length ? Math.max(...scores) : null;
}

export function weakestKept(r: SearchResult): number | null {
  return r.sources.length ? Math.min(...r.sources.map((s) => s.score)) : null;
}

/** How many candidates score at least `threshold`: what the minimum score would let through (ignoring duplicates and the budget). */
export function countAtOrAbove(r: SearchResult, threshold: number): number {
  return allScores(r).filter((s) => s >= threshold).length;
}

export type ExpectedOutcome =
  | { kind: 'none' }
  | { kind: 'kept'; label: string; rank: number; score: number }
  | { kind: 'dropped'; reason: string; score: number }
  | { kind: 'missing' }
  | { kind: 'nothing-ok' }
  | { kind: 'nothing-fail'; kept: number };

const includes = (citation: string, expected: string) => citation.toLowerCase().includes(expected.toLowerCase());

/** Did retrieval find the source the question was written for? Kept counts best; a drop says why it was lost. */
export function checkExpected(r: SearchResult, expected: string | null): ExpectedOutcome {
  if (!expected) return { kind: 'none' };
  if (expected.toLowerCase() === 'none') return r.sources.length === 0 ? { kind: 'nothing-ok' } : { kind: 'nothing-fail', kept: r.sources.length };

  const byScore = [...r.sources].sort((a, b) => b.score - a.score);
  const kept = byScore.findIndex((s) => includes(s.citation, expected));
  if (kept >= 0) return { kind: 'kept', label: byScore[kept].label, rank: kept + 1, score: byScore[kept].score };

  const dropped = [...r.dropped].sort((a, b) => b.score - a.score).find((d) => includes(d.citation, expected));
  return dropped ? { kind: 'dropped', reason: dropped.reason, score: dropped.score } : { kind: 'missing' };
}

export const isHit = (o: ExpectedOutcome): boolean => o.kind === 'kept' || o.kind === 'nothing-ok';
export const isMiss = (o: ExpectedOutcome): boolean => o.kind === 'dropped' || o.kind === 'missing' || o.kind === 'nothing-fail';

export interface TuningRow {
  question: TuningQuestion;
  state: 'waiting' | 'running' | 'done' | 'failed';
  result: SearchResult | null;
  /** Plain-language reason when the search failed. */
  error: string | null;
}

export interface TuningSummary {
  total: number;
  done: number;
  failed: number;
  /** Questions for which something was kept: the model would be called. */
  answered: number;
  /** Questions with a candidate at or above the tried minimum score. */
  answeredAtThreshold: number;
  expectedTotal: number;
  expectedHits: number;
  /** Median of the best scores, a quick read of how strong the matches are. */
  medianBest: number | null;
}

export function summarise(rows: TuningRow[], threshold: number): TuningSummary {
  const done = rows.filter((r) => r.state === 'done' && r.result);
  const best = done.map((r) => bestScore(r.result!)).filter((s): s is number => s !== null).sort((a, b) => a - b);
  const expected = done.filter((r) => r.question.expected).map((r) => checkExpected(r.result!, r.question.expected));
  return {
    total: rows.length,
    done: done.length,
    failed: rows.filter((r) => r.state === 'failed').length,
    answered: done.filter((r) => r.result!.sources.length > 0).length,
    answeredAtThreshold: done.filter((r) => countAtOrAbove(r.result!, threshold) > 0).length,
    expectedTotal: expected.length,
    expectedHits: expected.filter(isHit).length,
    medianBest: best.length ? best[Math.floor((best.length - 1) / 2)] : null,
  };
}

const csv = (value: string | number | null): string => {
  const text = value === null ? '' : String(value);
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
};

/** The results as CSV, for a spreadsheet or the tuning notes. */
export function toCsv(rows: TuningRow[], version: string, threshold: number): string {
  const header = ['version', 'question', 'expected', 'candidates', 'kept', 'best_score', 'weakest_kept', `at_or_above_${threshold}`, 'expected_result', 'dropped_below_min', 'dropped_duplicate', 'dropped_budget'];
  const lines = [header.join(',')];
  for (const row of rows) {
    const r = row.result;
    const outcome = r ? checkExpected(r, row.question.expected) : null;
    const dropped = (reason: string) => (r ? r.dropped.filter((d) => d.reason === reason).length : null);
    lines.push(
      [
        version,
        row.question.text,
        row.question.expected,
        r?.candidates ?? null,
        r?.sources.length ?? null,
        r ? bestScore(r) : null,
        r ? weakestKept(r) : null,
        r ? countAtOrAbove(r, threshold) : null,
        outcome ? describeOutcome(outcome) : row.error,
        dropped('BelowMinScore'),
        dropped('NearDuplicate'),
        dropped('OverBudget'),
      ]
        .map(csv)
        .join(','),
    );
  }
  return lines.join('\n');
}

const pct = (score: number) => `${Math.round(score * 100)}%`;

/** The outcome in words, for the table and the CSV. */
export function describeOutcome(o: ExpectedOutcome): string {
  switch (o.kind) {
    case 'none':
      return '';
    case 'kept':
      return `found as ${o.label} (best-scoring source number ${o.rank}, ${pct(o.score)})`;
    case 'dropped':
      return `lost: ${o.reason} (${pct(o.score)})`;
    case 'missing':
      return 'not among the candidates';
    case 'nothing-ok':
      return 'nothing found, as expected';
    case 'nothing-fail':
      return `${o.kept} source(s) found, but none were expected`;
  }
}
