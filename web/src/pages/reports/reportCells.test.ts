import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import { planText, rowLink, treeRows } from './reportCells';

const t = ((k: string) => k.replace('rep.plan.', '')) as unknown as TFunction;

describe('report cells (0.14.0)', () => {
  it('writes a month-plan cell as its counts', () => {
    expect(planText({ done: 2, late: 1, missed: 0, cancelled: 0, planned: 1 }, t)).toBe('done 2 · late 1 · planned 1');
    expect(planText({ done: 0, late: 0, missed: 0, cancelled: 0, planned: 0 }, t)).toBe('');
    expect(planText(null, t)).toBe('');
  });
  it('opens what a row is about', () => {
    expect(rowLink({ listQuery: 'symptomId=s1&inFailureTree=true', assetId: 'a1' }, 'failure-tree')).toBe('/work-orders?symptomId=s1&inFailureTree=true');
    expect(rowLink({ workOrderId: 'w1', assetId: 'a1' }, 'asset-work-history')).toBe('/work-orders/w1');
    expect(rowLink({ requestId: 'r1' }, 'responsiveness')).toBe('/work-requests/r1');
    expect(rowLink({ partId: 'p1' }, 'stock-reorder')).toBe('/inventory/parts/p1');
    expect(rowLink({ assetId: 'a1' }, 'reliability')).toBe('/assets/a1');
    expect(rowLink({ id: 'o1' }, 'open-purchase-orders')).toBe('/purchasing/orders/o1');
    expect(rowLink({ id: 'x' }, 'wo-backlog')).toBeNull();
  });
  it('charts the top level of a tree only', () => {
    const rows = [{ node: 'S1', level: 0 }, { node: 'C1', level: 1 }, { node: 'R1', level: 2 }, { node: 'S2', level: 0 }];
    expect(treeRows(rows).map((r) => r.node)).toEqual(['S1', 'S2']);
    expect(treeRows([{ month: 'Jan' }])).toEqual([{ month: 'Jan' }]);
  });
});
