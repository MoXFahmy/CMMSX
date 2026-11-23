import { describe, expect, it } from 'vitest';
import type { AiReportResult } from '@/components/aiReport';
import { aiChanges, aiEnabled, partStore } from './aiFill';
import type { TechBundle, TechJob } from './types';

const job = (over: Partial<TechJob> = {}): TechJob => ({
  id: 'j1', plannedLines: [{ id: 'l1', partId: 'p1', storeId: 's1', quantity: 2, issued: 0 }], defaultStoreId: null, ...over,
} as unknown as TechJob);

const r: AiReportResult = {
  workDone: 'Replaced the seal', causeDetails: null, symptom: null, cause: null, remedy: null, hours: 1.5,
  parts: [{ partId: 'p1', partNumber: 'P1', name: 'Seal', quantity: 2 }, { partId: 'p2', partNumber: 'P2', name: 'Bearing', quantity: 1 }],
};

describe('Fill with AI on the phone', () => {
  it('shows only when the bundle says AI is on (a 0.14 server says nothing)', () => {
    expect(aiEnabled(undefined)).toBe(false);
    expect(aiEnabled({} as TechBundle)).toBe(false);
    expect(aiEnabled({ ai: { enabled: false } } as TechBundle)).toBe(false);
    expect(aiEnabled({ ai: { enabled: true } } as TechBundle)).toBe(true);
  });

  it('takes a part from its planned line\'s store, else the job\'s default store', () => {
    expect(partStore(job(), 'p1')).toBe('s1');
    expect(partStore(job({ defaultStoreId: 's9' }), 'p2')).toBe('s9');
    expect(partStore(job({ defaultStoreId: null }), 'p2')).toBeNull();
    expect(partStore(job({ plannedLines: [{ id: 'l1', partId: 'p1', storeId: null, quantity: 1, issued: 0 }], defaultStoreId: 's9' }), 'p1')).toBe('s9');
  });

  it('queues the ticked hours and parts as the usual changes', () => {
    expect(aiChanges(job(), r, { hours: true, parts: ['p1'] }, true, '2026-11-23')).toEqual([
      { type: 'labor.add', payload: { workDate: '2026-11-23', hours: 1.5, overtimeHours: 0, notes: null } },
      { type: 'part.use', payload: { partId: 'p1', storeId: 's1', quantity: 2, lineId: 'l1' } },
    ]);
  });

  it('queues nothing unticked, and no parts without stock rights or a store', () => {
    expect(aiChanges(job(), r, { hours: false, parts: [] }, true, '2026-11-23')).toEqual([]);
    expect(aiChanges(job(), r, { hours: false, parts: ['p1'] }, false, '2026-11-23')).toEqual([]);
    expect(aiChanges(job(), r, { hours: false, parts: ['p2'] }, true, '2026-11-23')).toEqual([]);
    expect(aiChanges(job({ defaultStoreId: 's9' }), r, { hours: false, parts: ['p2'] }, true, '2026-11-23'))
      .toEqual([{ type: 'part.use', payload: { partId: 'p2', storeId: 's9', quantity: 1 } }]);
  });

  it('a suggestion without hours never logs hours', () => {
    expect(aiChanges(job(), { ...r, hours: null, parts: [] }, { hours: true, parts: [] }, true, '2026-11-23')).toEqual([]);
  });
});
