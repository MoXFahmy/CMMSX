import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import type { ReportDef } from '@/api/types';
import { printFilters } from './printFilters';

const t = ((k: string, v?: Record<string, unknown>) => (v ? `${k}${JSON.stringify(v)}` : k)) as unknown as TFunction;
const def: ReportDef = { key: 'x', category: 'Work', title: 'X', description: '', permission: '', params: [
  { name: 'range', type: 'dateRange' }, { name: 'siteId', type: 'site' },
  { name: 'groupBy', type: 'select', options: [{ value: 'month', label: 'Month' }, { value: 'trade', label: 'Trade' }], default: 'month' },
  { name: 'year', type: 'year' }, { name: 'assetId', type: 'asset' }, { name: 'components', type: 'bool' }, { name: 'days', type: 'number' },
] };

describe('printed report filters (0.14.0)', () => {
  it('writes one line per choice', () => {
    const lines = printFilters(def, { from: '2026-01-01', to: '2026-06-30', siteId: 's1', groupBy: 'trade', year: '2026', assetId: 'a1', components: 'true', days: '90' },
      { site: 'Cairo', asset: 'P-1 · Pump' }, t);
    expect(lines).toHaveLength(6);
    expect(lines[0]).toMatch(/^rep\.printPeriod\{"from":".+","to":".+"\}$/);
    expect(lines.slice(1)).toEqual(['rep.printSite: Cairo', 'rep.groupBy: Trade', 'rep.year: 2026', 'wo.asset: P-1 · Pump · rep.withComponents', 'rep.idleDays: 90']);
  });
  it('says all sites when none is chosen, and leaves out what a report doesn\'t ask', () => {
    expect(printFilters({ ...def, params: [{ name: 'siteId', type: 'site' }] }, {}, {}, t)).toEqual(['rep.printSite: common.all']);
  });
});
