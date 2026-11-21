import { afterEach, describe, expect, it } from 'vitest';
import { applyLanguage } from '@/language';
import { downFor, groupPicked } from './downtime';

afterEach(() => applyLanguage('en'));

describe('downtime', () => {
  it('downFor says how long in plain units', () => {
    const now = new Date('2026-11-21T12:00:00Z');
    expect(downFor('2026-11-21T09:45:00Z', now)).toBe('2 h 15 min');
    applyLanguage('ar');
    expect(downFor('2026-11-21T09:45:00Z', now)).toBe('2 س 15 د');
  });

  it("groupPicked puts the asset's parts first", () => {
    const { onAsset, others } = groupPicked([{ id: 'a' }, { id: 'b', onAsset: 2 }, { id: 'c', onAsset: null }]);
    expect(onAsset.map((p) => p.id)).toEqual(['b']);
    expect(others.map((p) => p.id)).toEqual(['a', 'c']);
  });
});
