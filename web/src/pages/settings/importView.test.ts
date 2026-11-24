import { describe, expect, it } from 'vitest';
import type { ImportResult, SheetResult } from '@/api/types';
import { canImport, fileKey, jobLabel, totals } from './importView';

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

  it('jobLabel says where a background job stands (0.26.0)', () => {
    const t = ((k: string, o?: Record<string, unknown>) => (o ? `${k} ${JSON.stringify(o)}` : k)) as never;
    const job = { status: 'Queued', save: false, sheet: null, rowsDone: 0, rowsTotal: 3000, waitingBehind: 2 } as never;
    expect(jobLabel(job, t)).toBe('import.waiting');
    expect(jobLabel({ ...(job as object), waitingBehind: 0 } as never, t)).toBe('import.starting');
    const running = { status: 'Running', save: false, sheet: 'Assets', rowsDone: 1240, rowsTotal: 3000, waitingBehind: 0 } as never;
    expect(jobLabel(running, t)).toBe('import.checking {"sheet":"import.sheets.Assets","done":"1,240","total":"3,000"}');
    expect(jobLabel({ ...(running as object), save: true } as never, t)).toBe('import.importing {"sheet":"import.sheets.Assets","done":"1,240","total":"3,000"}');
    expect(jobLabel({ ...(running as object), status: 'Done' } as never, t)).toBe('');
  });
});
