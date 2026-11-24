import { describe, expect, it } from 'vitest';
import { UNIT_STATE_COLOR, defaultHoursMeter, parseSerials, serialProblem } from './units';

describe('serial numbers', () => {
  it('parseSerials trims and drops blank lines', () => {
    expect(parseSerials(' A1 \n\n a2, A3 ')).toEqual(['A1', 'a2', 'A3']);
    expect(parseSerials('')).toEqual([]);
    expect(parseSerials('B7\r\nB8;B9')).toEqual(['B7', 'B8', 'B9']);
  });
  it('serialProblem', () => {
    expect(serialProblem(['A', 'A'], 2)).toBe('duplicate');
    expect(serialProblem(['A', 'a'], 2)).toBe('duplicate');
    expect(serialProblem(['A'], 2)).toBe('count');
    expect(serialProblem(['A', 'B'], 2)).toBeNull();
  });
  it('has a colour for every state', () => {
    expect(Object.keys(UNIT_STATE_COLOR).sort()).toEqual(['AtRepair', 'Fitted', 'InStore', 'Scrapped']);
  });
  it('defaultHoursMeter picks the first active hours meter', () => {
    const meters = [{ id: 'km', unit: 'km', isActive: true }, { id: 'old', unit: 'Hours', isActive: false }, { id: 'run', unit: 'h', isActive: true }, { id: 'ar', unit: 'ساعة', isActive: true }];
    expect(defaultHoursMeter(meters)).toBe('run');
    expect(defaultHoursMeter([{ id: 'ar', unit: ' ساعات ', isActive: true }])).toBe('ar');
    expect(defaultHoursMeter([{ id: 'km', unit: 'km', isActive: true }])).toBeNull();
  });
});
