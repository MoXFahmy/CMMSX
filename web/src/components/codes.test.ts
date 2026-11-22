import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import { codeOptions, orderCodes, type CodePair } from './codes';

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
