import { describe, expect, it } from 'vitest';
import { available, findPart, queuedCount, searchParts, shortBy, stillReserved } from './stock';
import type { StockPart, TechStock } from './types';
import type { QueuedChange } from './outbox';

const part = (over: Partial<StockPart> = {}): StockPart => ({
  id: 'p1', partNumber: 'BRG-1', name: 'Bearing 6204', unitOfMeasure: 'EA', barcode: '590123', isShelfItem: false, allowNegativeStock: false,
  levels: [{ storeId: 's1', onHand: 5, reserved: 3, bin: 'A1' }], ...over,
});
const stock = (parts: StockPart[]): TechStock => ({ stores: [], parts, cancelReasons: [], pmTasks: [], counts: [], purchaseOrders: null });
const q = (seq: number, at: string, payload: unknown): QueuedChange => ({ clientId: String(seq), userId: 'u', seq, at, type: 'count.line', payload, state: 'waiting', tries: 0 });

describe('phone stock', () => {
  it('counts what the store can give a job', () => {
    expect(available(part(), 's1')).toBe(2);
    expect(available(part(), 's1', 2)).toBe(4); // this job's own reservation is its to use
    expect(available(part(), 's2')).toBe(0);
    expect(available(part({ levels: [{ storeId: 's1', onHand: 1, reserved: 4 }] }), 's1')).toBe(0);
  });

  it('says how much the store is short', () => {
    expect(shortBy(part(), 's1', 2)).toBe(0);
    expect(shortBy(part(), 's1', 5)).toBe(3);
    expect(shortBy(part({ allowNegativeStock: true }), 's1', 9)).toBe(0);
  });

  it('finds parts by number, barcode or words', () => {
    const s = stock([part(), part({ id: 'p2', partNumber: 'FLT-9', name: 'Air filter', barcode: null })]);
    expect(findPart(s, 'brg-1')?.id).toBe('p1');
    expect(findPart(s, '590123')?.id).toBe('p1');
    expect(findPart(s, 'nothing')).toBeUndefined();
    expect(searchParts(s, 'filter').map((p) => p.id)).toEqual(['p2']);
    expect(searchParts(s, 'flt').map((p) => p.id)).toEqual(['p2']);
  });

  it('works out what a planned line still reserves', () => {
    expect(stillReserved({ id: 'l', partId: 'p1', storeId: 's1', quantity: 3, issued: 1 })).toBe(2);
    expect(stillReserved({ id: 'l', partId: 'p1', storeId: 's1', quantity: 3, issued: 5 })).toBe(0);
  });

  it('takes the latest queued count of a part', () => {
    const queue = [q(1, '2026-11-22T10:00:00Z', { countId: 'c', partId: 'p1', quantity: 4 }), q(2, '2026-11-22T09:00:00Z', { countId: 'c', partId: 'p1', quantity: 9 }),
      q(3, '2026-11-22T11:00:00Z', { countId: 'other', partId: 'p1', quantity: 1 })];
    expect(queuedCount(queue, 'c', 'p1')).toBe(4);
    expect(queuedCount(queue, 'c', 'p2')).toBeUndefined();
  });
});
