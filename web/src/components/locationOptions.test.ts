import { describe, expect, it } from 'vitest';
import { locationOptions } from './LocationPicker';
import { placeFilter } from './bilingualSearch';
import { buildTree, filterTree } from './LocationTree';
import type { ComboboxItem } from '@mantine/core';
import type { HierarchyLocation } from '@/api/types';

const loc = (id: string, name: string, parentId: string | null = null): HierarchyLocation =>
  ({ id, siteId: 's', parentId, code: id, name, type: 'Room', criticality: 'Medium', isActive: true, assetCount: 0 });

describe('location options', () => {
  it('lists a place whose parent the person cannot see (limited to a branch) as a top place', () => {
    // Building B1 is hidden: the person is limited to floor F1 and what is under it.
    const opts = locationOptions([loc('f1', 'Floor 1', 'b1'), loc('r1', 'Room 1', 'f1')], 's');
    expect(opts.map((o) => o.label)).toEqual(['Floor 1', 'Floor 1 › Room 1']);
  });

  it("carries each place's other-language name so the picker finds it by either (0.28.0-4)", () => {
    const opts = locationOptions([{ ...loc('r1', 'غرفة المعدات'), otherName: 'Plant room' }, loc('r2', 'مخزن')], 's');
    expect(opts.map((o) => o.otherName)).toEqual(['Plant room', null]);
    const filter = placeFilter(new Map(opts.map((o) => [o.value, o.otherName])));
    const found = (search: string) => (filter({ options: opts, search, limit: 50 }) as ComboboxItem[]).map((o) => o.value);
    expect(found('plant')).toEqual(['r1']);
    expect(found('المعدات')).toEqual(['r1']);
    expect(found('store')).toEqual([]);
  });

  it('filters the tree by the other name too, keeping to the leaf name', () => {
    const tree = buildTree({ sites: [{ id: 's', code: 'S', name: 'Site', isActive: true, assetCount: 0 }], locations: [{ ...loc('r1', 'غرفة المعدات'), otherName: 'Plant room' }, loc('r2', 'مخزن')] });
    expect(filterTree(tree, 'plant')[0].children.map((n) => n.id)).toEqual(['r1']);
    expect(filterTree(tree, 'مخزن')[0].children.map((n) => n.id)).toEqual(['r2']);
    expect(filterTree(tree, 'nothing')).toEqual([]);
  });
});
