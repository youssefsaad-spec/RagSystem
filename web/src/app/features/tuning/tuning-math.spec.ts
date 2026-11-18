import { SearchResult } from '../../core/api/models';
import {
  allScores,
  bestScore,
  checkExpected,
  countAtOrAbove,
  describeOutcome,
  isHit,
  isMiss,
  MAX_QUESTIONS,
  parseQuestions,
  summarise,
  toCsv,
  TuningRow,
  weakestKept,
} from './tuning-math';

const result = (overrides: Partial<SearchResult> = {}): SearchResult => ({
  version: 'v3',
  question: 'q',
  candidates: 4,
  sources: [
    { label: 'S1', citation: 'Guide, part 2', score: 0.82, estTokens: 100, chunkId: 'a', content: '' },
    { label: 'S2', citation: "What's New, part 1", score: 0.91, estTokens: 100, chunkId: 'b', content: '' },
  ],
  dropped: [
    { citation: 'Guide, part 7', score: 0.55, reason: 'BelowMinScore', chunkId: 'c' },
    { citation: 'Guide, part 3', score: 0.7, reason: 'NearDuplicate', chunkId: 'd' },
  ],
  ...overrides,
});

const row = (r: SearchResult | null, expected: string | null = null, text = 'q'): TuningRow => ({
  question: { text, expected },
  state: r ? 'done' : 'failed',
  result: r,
  error: r ? null : 'It failed.',
});

describe('parseQuestions', () => {
  it('reads one question per line, skips blanks and notes, and splits the expected source', () => {
    const q = parseQuestions(['# a note', '', '  How do I log in?  ', "What changed? => What's New", 'Capital of France => none', 'x =>'].join('\r\n'));
    expect(q).toEqual([
      { text: 'How do I log in?', expected: null },
      { text: 'What changed?', expected: "What's New" },
      { text: 'Capital of France', expected: 'none' },
      { text: 'x', expected: null },
    ]);
  });

  it('ignores a line that has only an expected source, and stops at the limit', () => {
    expect(parseQuestions('=> part 1')).toEqual([]);
    expect(parseQuestions(Array.from({ length: MAX_QUESTIONS + 10 }, (_, i) => `q${i}`).join('\n'))).toHaveLength(MAX_QUESTIONS);
  });
});

describe('score helpers', () => {
  it('reads best, weakest kept, all scores and the count at a threshold', () => {
    const r = result();
    expect(allScores(r).sort()).toEqual([0.55, 0.7, 0.82, 0.91]);
    expect(bestScore(r)).toBe(0.91);
    expect(weakestKept(r)).toBe(0.82);
    expect(countAtOrAbove(r, 0.6)).toBe(3);
    expect(countAtOrAbove(r, 0.5)).toBe(4);
    expect(countAtOrAbove(r, 0.95)).toBe(0);
  });

  it('copes with a search that found nothing at all', () => {
    const empty = result({ sources: [], dropped: [], candidates: 0 });
    expect(bestScore(empty)).toBeNull();
    expect(weakestKept(empty)).toBeNull();
  });
});

describe('checkExpected', () => {
  it('finds the expected source among the kept ones and gives its rank by score', () => {
    const o = checkExpected(result(), "what's new");
    expect(o).toEqual({ kind: 'kept', label: 'S2', rank: 1, score: 0.91 });
    expect(isHit(o)).toBe(true);
    expect(checkExpected(result(), 'Guide, part 2')).toMatchObject({ kind: 'kept', rank: 2 });
  });

  it('says why a source was lost when it was dropped, and when it was not even a candidate', () => {
    expect(checkExpected(result(), 'part 7')).toEqual({ kind: 'dropped', reason: 'BelowMinScore', score: 0.55 });
    expect(isMiss(checkExpected(result(), 'part 7'))).toBe(true);
    expect(checkExpected(result(), 'Authentication')).toEqual({ kind: 'missing' });
  });

  it('"none" expects that nothing is kept', () => {
    expect(checkExpected(result({ sources: [] }), 'none')).toEqual({ kind: 'nothing-ok' });
    expect(checkExpected(result(), 'NONE')).toEqual({ kind: 'nothing-fail', kept: 2 });
    expect(checkExpected(result(), null)).toEqual({ kind: 'none' });
  });

  it('describes every outcome in words', () => {
    expect(describeOutcome({ kind: 'kept', label: 'S2', rank: 1, score: 0.91 })).toBe('found as S2 (best-scoring source number 1, 91%)');
    expect(describeOutcome({ kind: 'dropped', reason: 'OverBudget', score: 0.7 })).toBe('lost: OverBudget (70%)');
    expect(describeOutcome({ kind: 'missing' })).toBe('not among the candidates');
    expect(describeOutcome({ kind: 'nothing-ok' })).toBe('nothing found, as expected');
    expect(describeOutcome({ kind: 'nothing-fail', kept: 2 })).toBe('2 source(s) found, but none were expected');
    expect(describeOutcome({ kind: 'none' })).toBe('');
  });
});

describe('summarise', () => {
  it('counts answered questions, the effect of another minimum score, expected hits and the median best score', () => {
    const none = result({ sources: [], dropped: [{ citation: 'Guide, part 1', score: 0.5, reason: 'BelowMinScore', chunkId: 'x' }] });
    const rows = [row(result(), "What's New"), row(none, 'none'), row(none, 'Guide'), row(null)];
    const s = summarise(rows, 0.45);
    expect(s).toMatchObject({ total: 4, done: 3, failed: 1, answered: 1, answeredAtThreshold: 3, expectedTotal: 3, expectedHits: 2 });
    expect(s.medianBest).toBe(0.5); // best scores 0.5, 0.5, 0.91
    expect(summarise(rows, 0.6).answeredAtThreshold).toBe(1);
  });

  it('has no median before anything ran', () => {
    expect(summarise([], 0.6).medianBest).toBeNull();
  });
});

describe('toCsv', () => {
  it('writes a header and one line per question, quoting what needs it', () => {
    const csv = toCsv([row(result(), "What's New", 'Hello, "world"?'), row(null, null, 'Broken')], 'v3', 0.6).split('\n');
    expect(csv[0]).toBe('version,question,expected,candidates,kept,best_score,weakest_kept,at_or_above_0.6,expected_result,dropped_below_min,dropped_duplicate,dropped_budget');
    expect(csv[1]).toBe(`v3,"Hello, ""world""?",What's New,4,2,0.91,0.82,3,"found as S2 (best-scoring source number 1, 91%)",1,1,0`);
    expect(csv[2]).toBe('v3,Broken,,,,,,,It failed.,,,');
  });
});
