import { describe, expect, it } from 'vitest';
import type { CostCentre } from '@/api/types';
import { spread, treeRows } from './costCentres';

const cc = (id: string, code: string, parentId: string | null = null): CostCentre => ({ id, code, name: code, parentId, isActive: true, path: code });

describe('cost-centre tree rows', () => {
  it('puts parents first with their children under them, by code', () => {
    const rows = treeRows([cc('l2', 'LINE2', 'p'), cc('f', 'FAC'), cc('p', 'PROD'), cc('l1', 'LINE1', 'p'), cc('k', 'PACK', 'l1')]);
    expect(rows.map((r) => [r.code, r.depth])).toEqual([['FAC', 0], ['PROD', 0], ['LINE1', 1], ['PACK', 2], ['LINE2', 1]]);
  });
  it('shows a cost centre whose parent is hidden at the top', () => {
    const rows = treeRows([cc('l1', 'LINE1', 'hidden'), cc('f', 'FAC')]);
    expect(rows.map((r) => [r.code, r.depth])).toEqual([['FAC', 0], ['LINE1', 0]]);
  });
});

describe('spreading a year over months', () => {
  it('rounds to cents and puts the rest on the last month', () => {
    const m = spread(1000);
    expect(m).toHaveLength(12);
    expect(m.slice(0, 11)).toEqual(Array(11).fill(83.33));
    expect(m[11]).toBe(83.37);
  });
  it('gives zeros for nothing', () => expect(spread(0)).toEqual(Array(12).fill(0)));
  it('always adds up to the total', () => {
    for (const total of [1, 7.77, 100, 12345.67, 99999.99]) expect(Math.round(spread(total).reduce((a, b) => a + b, 0) * 100) / 100).toBe(total);
  });
});
