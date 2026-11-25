import { describe, expect, it } from 'vitest';
import { windowDate, windowProblem } from '@/components/accessWindows';
import { usableRecipients } from './requestForm';

describe('request form', () => {
  it('Request form clears recipients the new site cannot use and sends a cleared window date as empty', () => {
    // The new site's people: only these can stay on the list.
    const people = [{ id: 'b', name: 'Bo' }, { id: 'c', name: 'Cy' }];
    expect(usableRecipients(['a', 'b', 'c'], people)).toEqual(['b', 'c']);
    expect(usableRecipients(['a'], people)).toEqual([]);
    expect(usableRecipients([], people)).toEqual([]);
    // While the new site's people are still loading nobody can stay.
    expect(usableRecipients(['a', 'b'], undefined)).toEqual([]);

    // A cleared picker is an empty value, not a made-up date, and the form says so before sending.
    expect(windowDate(null)).toBe('');
    expect(windowDate('2026-11-25 08:00:00')).toBe(new Date('2026-11-25T08:00:00').toISOString());
    expect(windowProblem([{ start: '', end: '2026-11-25T10:00:00Z', note: '' }])).toBe('accessWindows.fillDates');
    expect(windowProblem([{ start: '2026-11-25T08:00:00Z', end: '', note: '' }])).toBe('accessWindows.fillDates');
    expect(windowProblem([{ start: '2026-11-25T08:00:00Z', end: '2026-11-25T10:00:00Z', note: '' }])).toBeNull();
  });
});
