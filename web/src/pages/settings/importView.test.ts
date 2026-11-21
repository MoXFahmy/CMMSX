import { describe, expect, it } from 'vitest';
import type { ImportResult, SheetResult } from '@/api/types';
import { canImport, fileKey, totals } from './importView';

const sheet = (counts: Partial<Pick<SheetResult, 'new' | 'changed' | 'unchanged' | 'problems'>>, key = 'Sites'): SheetResult => ({
  sheet: key, new: 0, changed: 0, unchanged: 0, problems: 0, rows: [], ignoredColumns: [], ...counts,
});
const result = (sheets: SheetResult[], saved = false): ImportResult => ({ saved, sheets, newLookups: [], notes: [], stopped: null });
const file = new File(['x'], 'data.xlsx', { lastModified: 1000 });

describe('import view', () => {
  it('canImport needs a check of the same file with something to do', () => {
    const key = fileKey(file);
    expect(canImport(null, key, file)).toBe(false);
    expect(canImport(result([sheet({ changed: 1 })]), 'other|1|1', file)).toBe(false);
    expect(canImport(result([sheet({ unchanged: 3, problems: 2 })]), key, file)).toBe(false);
    expect(canImport(result([sheet({ changed: 1 })], true), key, file)).toBe(false);
    expect(canImport(result([sheet({ changed: 1 })]), key, null)).toBe(false);
    expect(canImport(result([sheet({ changed: 1 })]), key, file)).toBe(true);
  });

  it('totals adds the sheets up', () => {
    const r = result([sheet({ new: 1, changed: 2, unchanged: 3 }), sheet({ new: 4, problems: 5 }, 'Assets')]);
    expect(totals(r)).toEqual({ new: 5, changed: 2, unchanged: 3, problems: 5 });
  });
});
