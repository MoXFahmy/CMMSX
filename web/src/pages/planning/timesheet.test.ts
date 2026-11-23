import { describe, expect, it } from 'vitest';
import { dayTotals, weekCell, type TimesheetRow } from './timesheet';

const row = (id: string, entries: [number, number][]): TimesheetRow => ({
  workOrderId: id, number: id, title: id, status: 'Open', closed: false, entries: entries.map(([hours, overtimeHours], i) => ({ id: `${id}-${i}`, hours, overtimeHours })),
});

describe('timesheet (0.19.0)', () => {
  it('adds up the day', () => {
    expect(dayTotals([row('a', [[2, 0], [1.5, 1]]), row('b', []), row('c', [[3, 0]])])).toEqual({ hours: 6.5, overtimeHours: 1 });
  });
  it('marks a week cell over or under the hours available', () => {
    expect(weekCell({ hours: 9, overtimeHours: 0, available: 8 })).toBe('over');
    expect(weekCell({ hours: 6, overtimeHours: 1, available: 8 })).toBe('under');
    expect(weekCell({ hours: 7, overtimeHours: 1, available: 8 })).toBe('ok');
    expect(weekCell({ hours: 0, overtimeHours: 0, available: 0 })).toBe('ok'); // a day off
  });
});
