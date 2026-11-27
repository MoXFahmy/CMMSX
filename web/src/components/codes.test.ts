import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import { codeFilterOptions, codeOptions, lookupName, orderCodes, type CodePair } from './codes';

const codes = [{ id: 'a', code: 'A', name: 'Alpha' }, { id: 'b', code: 'B', name: 'Beta' }, { id: 'c', code: 'C', name: 'Gamma' }];
const pairs: CodePair[] = [{ first: 's', second: 'c', count: 5 }, { first: 's', second: 'a', count: 2 }, { first: 'x', second: 'b', count: 9 }];
const t = ((k: string) => k) as unknown as TFunction;

describe('code ordering', () => {
  it('puts codes used with the first choice first, by count, then the rest by code', () => {
    const r = orderCodes(codes, pairs, 's');
    expect(r.top.map((c) => c.id)).toEqual(['c', 'a']);
    expect(r.rest.map((c) => c.id)).toEqual(['b']);
  });
  it('lists everything by code when nothing is chosen yet', () => {
    expect(orderCodes([codes[2], codes[0], codes[1]], pairs, null)).toEqual({ top: [], rest: codes });
  });
  it('makes picker groups, without an empty group', () => {
    expect(codeOptions(codes, pairs, 's', t)).toEqual([
      { group: 'codes.mostUsed', items: [{ value: 'c', label: 'C · Gamma' }, { value: 'a', label: 'A · Alpha' }] },
      { group: 'codes.all', items: [{ value: 'b', label: 'B · Beta' }] },
    ]);
    expect(codeOptions(codes, pairs, undefined, t)).toEqual([{ value: 'a', label: 'A · Alpha' }, { value: 'b', label: 'B · Beta' }, { value: 'c', label: 'C · Gamma' }]);
  });
});

describe("code labels in the reader's language (0.28.0-2: the filter value stays the id, only the label changes)", () => {
  const list = [{ id: 'a', code: 'LK', name: 'Leak', nameAr: 'تسريب' }, { id: 'b', code: 'NZ', name: 'Noise', nameAr: null }];

  it('names a code in Arabic for Arabic readers, in English for the others', () => {
    expect(lookupName(list, 'a', 'ar')).toBe('تسريب');
    expect(lookupName(list, 'a', 'en')).toBe('Leak');
    expect(lookupName(list, 'b', 'ar')).toBe('Noise');
    expect(lookupName(list, 'zz', 'ar')).toBe('…');
    expect(lookupName(undefined, 'a', 'ar')).toBe('…');
  });

  it("labels filter options with the reader's name and keeps the ids as values", () => {
    expect(codeFilterOptions(list, 'None', 'ar')).toEqual([
      { value: 'none', label: 'None' }, { value: 'a', label: 'LK · تسريب' }, { value: 'b', label: 'NZ · Noise' },
    ]);
    expect(codeFilterOptions(list, 'None', 'en').map((o) => o.label)).toEqual(['None', 'LK · Leak', 'NZ · Noise']);
  });
});
