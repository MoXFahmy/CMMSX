import { describe, expect, it } from 'vitest';
import { isOutside, windowProblem } from './accessWindows';

const w = (start: string, end: string, note?: string) => ({ start, end, note });

describe('access windows', () => {
  it('finds what is wrong with a list', () => {
    expect(windowProblem([])).toBeNull();
    expect(windowProblem([w('2026-11-23T06:00:00Z', '2026-11-23T10:00:00Z')])).toBeNull();
    expect(windowProblem([w('2026-11-23T10:00:00Z', '2026-11-23T10:00:00Z')])).toBe('accessWindows.endAfterStart');
    expect(windowProblem(Array.from({ length: 11 }, (_, i) => w(`2026-11-${10 + i}T06:00:00Z`, `2026-11-${10 + i}T07:00:00Z`)))).toBe('accessWindows.tooMany');
    expect(windowProblem([w('2026-11-23T06:00:00Z', '2026-11-23T10:00:00Z', 'x'.repeat(201))])).toBe('accessWindows.noteTooLong');
  });

  it('says when a planned day misses every window', () => {
    const list = [w('2026-11-23T06:00:00Z', '2026-11-23T10:00:00Z')];
    expect(isOutside([], '2026-11-24', 'UTC')).toBe(false);
    expect(isOutside(list, '2026-11-23', 'UTC')).toBe(false);
    expect(isOutside(list, '2026-11-24', 'UTC')).toBe(true);
    // 23:30 UTC on the 22nd is after midnight on the 23rd in Cairo (UTC+2).
    expect(isOutside([w('2026-11-22T23:30:00Z', '2026-11-22T23:45:00Z')], '2026-11-23', 'Africa/Cairo')).toBe(false);
  });
});
