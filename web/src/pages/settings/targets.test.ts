import { describe, expect, it } from 'vitest';
import { inUnit, toMinutes, unitOf } from './targets';

describe('target units', () => {
  it('shows whole and half days in days, anything else in hours', () => {
    expect(unitOf(1440)).toBe('d');
    expect(unitOf(2160)).toBe('d'); // 1.5 days
    expect(unitOf(240)).toBe('h');
    expect(unitOf(90)).toBe('h');
    expect(unitOf(null)).toBe('h');
  });
  it('converts both ways', () => {
    expect(inUnit(2160, 'd')).toBe(1.5);
    expect(inUnit(90, 'h')).toBe(1.5);
    expect(inUnit(null, 'd')).toBe('');
    expect(toMinutes(1.5, 'd')).toBe(2160);
    expect(toMinutes(4, 'h')).toBe(240);
    expect(toMinutes('', 'h')).toBeNull();
    expect(toMinutes(0, 'd')).toBeNull();
  });
});
