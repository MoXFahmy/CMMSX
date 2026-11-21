import { describe, expect, it } from 'vitest';
import { exportUrl } from './ListTools';
import type { ListColumn } from './types';

type Po = { supplier: string; site: string };

describe('exportUrl', () => {
  it('puts each field in the file once, even when two shown columns both carry it', () => {
    const cols = [
      { accessor: 'supplier', title: 'Supplier', export: [{ key: 'supplierName' }, { key: 'siteName', title: 'Site' }] },
      { accessor: 'site', title: 'Site', export: [{ key: 'siteName' }] },
    ] as ListColumn<Po>[];
    const sp = new URLSearchParams(exportUrl('/purchase-orders/export', {}, cols, 'csv').split('?')[1]);
    expect(sp.getAll('c')).toEqual(['supplierName', 'siteName']);
    expect(sp.getAll('t')).toEqual(['Supplier', 'Site']);
  });
});
