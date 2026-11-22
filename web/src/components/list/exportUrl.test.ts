import { afterEach, describe, expect, it, vi } from 'vitest';
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

  it('carries the site switch, which a download link cannot send as a header (0.9.0)', () => {
    vi.stubGlobal('localStorage', { getItem: (k: string) => (k === 'cmx.site' ? 'cairo' : null), setItem: () => {}, removeItem: () => {} });
    const url = exportUrl('/work-orders/export', {}, [{ accessor: 'title', title: 'Title' }] as ListColumn<{ title: string }>[], 'csv');
    expect(new URLSearchParams(url.split('?')[1]).get('cmxSite')).toBe('cairo');
  });
});

afterEach(() => { vi.unstubAllGlobals(); });
