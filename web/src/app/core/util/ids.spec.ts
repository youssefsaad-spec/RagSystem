import { isGuid } from './ids';
import { formatDuration, formatRelative, toDate, truncate } from './format';

describe('isGuid', () => {
  it('accepts a GUID and rejects the rest', () => {
    expect(isGuid('019a8c0e-7b8d-7c3a-9b1e-0123456789ab')).toBe(true);
    expect(isGuid('not-a-guid')).toBe(false);
    expect(isGuid('019a8c0e-7b8d-7c3a-9b1e-0123456789ab/../x')).toBe(false);
    expect(isGuid(undefined)).toBe(false);
  });
});

describe('format helpers', () => {
  it('formats durations', () => {
    expect(formatDuration(5)).toBe('5 s');
    expect(formatDuration(60)).toBe('1 min');
    expect(formatDuration(75)).toBe('1 min 15 s');
    expect(formatDuration(3700)).toBe('1 h 1 min');
    expect(formatDuration(-3)).toBe('0 s');
  });
  it('never shows "Invalid Date"', () => {
    expect(toDate('garbage')).toBeNull();
    expect(formatRelative('garbage')).toBe('');
  });
  it('truncates on a clean boundary', () => {
    expect(truncate('a   b\n c', 20)).toBe('a b c');
    expect(truncate('x'.repeat(100), 10)).toHaveLength(10);
  });
});
