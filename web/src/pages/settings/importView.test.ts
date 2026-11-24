import { describe, expect, it } from 'vitest';
import type { ImportJob, ImportResult, SheetResult } from '@/api/types';
import en from '@/locales/en.json';
import ar from '@/locales/ar.json';
import { anyActive, canImport, fileKey, jobLabel, refreshesAll, totals } from './importView';

const sheet = (counts: Partial<Pick<SheetResult, 'new' | 'changed' | 'unchanged' | 'problems'>>, key = 'Sites'): SheetResult => ({
  sheet: key, new: 0, changed: 0, unchanged: 0, problems: 0, rows: [], ignoredColumns: [], ...counts,
});
const result = (sheets: SheetResult[], saved = false): ImportResult => ({ saved, sheets, newLookups: [], notes: [], stopped: null });
const file = new File(['x'], 'data.xlsx', { lastModified: 1000 });

const job = (r: ImportResult, over: Partial<ImportJob> = {}): ImportJob => ({
  id: 'j1', save: false, status: 'Done', fileName: 'data.xlsx', createdAt: '', rowsDone: 0, rowsTotal: 0, waitingBehind: 0, result: r, ...over,
});

describe('import view', () => {
  it("canImport needs this file's own finished check with something to do", () => {
    const key = fileKey(file);
    const checked = { jobId: 'j1', key };
    expect(canImport(null, checked, file)).toBe(false);
    expect(canImport(job(result([sheet({ changed: 1 })])), { jobId: 'j1', key: 'other|1|1' }, file)).toBe(false);
    expect(canImport(job(result([sheet({ unchanged: 3, problems: 2 })])), checked, file)).toBe(false);
    expect(canImport(job(result([sheet({ changed: 1 })], true), { save: true }), checked, file)).toBe(false);
    expect(canImport(job(result([sheet({ changed: 1 })])), checked, null)).toBe(false);
    // Another check opened from the history, or this one not finished yet: no Import under it.
    expect(canImport(job(result([sheet({ changed: 1 })]), { id: 'j2' }), checked, file)).toBe(false);
    expect(canImport(job(result([sheet({ changed: 1 })]), { status: 'Running' }), checked, file)).toBe(false);
    expect(canImport(job(result([sheet({ changed: 1 })])), null, file)).toBe(false);
    expect(canImport(job(result([sheet({ changed: 1 })])), checked, file)).toBe(true);
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

  it('every sheet the server imports has a name in both languages (0.26.0)', () => {
    const sheets = ['Sites', 'CostCentres', 'Budgets', 'Locations', 'Categories', 'BrandsAndModels', 'Trades', 'PartGroups', 'Symptoms', 'Causes', 'Remedies',
      'Suppliers', 'Stores', 'Assets', 'AssetGroups', 'AssetGroupMembers', 'People', 'Parts', 'SupplierCatalogue', 'AssetParts', 'Stock', 'PmTasks',
      'MeasurementPoints', 'PmSteps', 'PmParts', 'PmSchedules'];
    for (const k of sheets) {
      expect((en.import.sheets as Record<string, string>)[k], k).toBeTruthy();
      expect((ar.import.sheets as Record<string, string>)[k], k).toBeTruthy();
    }
  });

  it('the history keeps up while any job waits or runs', () => {
    expect(anyActive([{ status: 'Done' }, { status: 'Running' }])).toBe(true);
    expect(anyActive([{ status: 'Queued' }])).toBe(true);
    expect(anyActive([{ status: 'Done' }, { status: 'Failed' }])).toBe(false);
    expect(anyActive(undefined)).toBe(false);
  });

  it('only an import this page watched finish refreshes every screen', () => {
    const done = { id: 'j1', save: true, status: 'Done' as const };
    expect(refreshesAll(done, 'j1')).toBe(true);
    expect(refreshesAll(done, null)).toBe(false); // opened from the history after it finished
    expect(refreshesAll({ ...done, save: false }, 'j1')).toBe(false);
    expect(refreshesAll({ ...done, status: 'Failed' as const }, 'j1')).toBe(false);
  });
});
