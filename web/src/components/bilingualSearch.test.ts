import { describe, expect, it } from 'vitest';
import { matchesPlace } from './bilingualSearch';

describe('matchesPlace (0.28.0: a place is found by either of its names)', () => {
  it('finds a place by the name not shown', () => {
    expect(matchesPlace({ name: 'مضخة التبريد 2', otherName: 'Chiller pump 2' }, 'chiller')).toBe(true);
  });

  it('finds a place by the name shown', () => {
    expect(matchesPlace({ name: 'مضخة التبريد 2', otherName: 'Chiller pump 2' }, 'التبريد')).toBe(true);
  });

  it('ignores case', () => {
    expect(matchesPlace({ name: 'Chiller pump 2' }, 'CHILLER')).toBe(true);
  });

  it('does not match a place without that text', () => {
    expect(matchesPlace({ name: 'Pump' }, 'مض')).toBe(false);
  });

  it('matches everything on an empty search', () => {
    expect(matchesPlace({ name: 'Pump', otherName: null }, '  ')).toBe(true);
  });

  it('also looks at the label shown (number and path)', () => {
    expect(matchesPlace({ name: 'Pump', label: 'A-001 · Pump' }, 'a-001')).toBe(true);
  });
});
