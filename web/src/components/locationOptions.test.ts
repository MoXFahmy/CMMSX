import { describe, expect, it } from 'vitest';
import { locationOptions } from './LocationPicker';
import type { HierarchyLocation } from '@/api/types';

const loc = (id: string, name: string, parentId: string | null = null): HierarchyLocation =>
  ({ id, siteId: 's', parentId, code: id, name, type: 'Room', criticality: 'Medium', isActive: true, assetCount: 0 });

describe('location options', () => {
  it('lists a place whose parent the person cannot see (limited to a branch) as a top place', () => {
    // Building B1 is hidden: the person is limited to floor F1 and what is under it.
    const opts = locationOptions([loc('f1', 'Floor 1', 'b1'), loc('r1', 'Room 1', 'f1')], 's');
    expect(opts.map((o) => o.label)).toEqual(['Floor 1', 'Floor 1 › Room 1']);
  });
});
