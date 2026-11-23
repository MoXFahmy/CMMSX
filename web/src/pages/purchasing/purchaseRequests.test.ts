import { describe, expect, it } from 'vitest';
import { draftFromShortages, isLate, orderableLines, previewGroups, proposalMode, requestStatusColor, type PurchaseRequestDetail } from './purchaseRequests';

const detail = (status: PurchaseRequestDetail['status'], ordered: boolean[]): PurchaseRequestDetail => ({
  id: 'r1', number: 'PR-00001', status, siteId: 's1', lines: ordered.map((o, i) => ({
    id: `l${i}`, lineNumber: i + 1, description: `Line ${i + 1}`, quantity: 1, ordered: o ? 1 : 0, received: 0,
    order: o ? { id: 'po1', number: 'PO-00001', lineId: 'pl1', status: 'Draft' } : null,
  })),
  comments: [], actions: {} as PurchaseRequestDetail['actions'], recipientIds: [], rowVersion: '',
} as unknown as PurchaseRequestDetail);

describe('purchase requests', () => {
  it('colours each status', () => {
    expect(requestStatusColor('Rejected')).toBe('red');
    expect(requestStatusColor('Ordered')).toBe('green');
    expect(requestStatusColor('Submitted')).toBe('blue');
    expect(requestStatusColor('PartlyOrdered')).toBe('yellow');
  });

  it('offers only approved, unordered lines for ordering', () => {
    expect(orderableLines(detail('Approved', [true, false])).map((l) => l.id)).toEqual(['l1']);
    expect(orderableLines(detail('PartlyOrdered', [true, false, false])).map((l) => l.id)).toEqual(['l1', 'l2']);
    expect(orderableLines(detail('Submitted', [false]))).toEqual([]);
    expect(orderableLines(detail('Closed', [false]))).toEqual([]);
  });

  it('counts one draft order per supplier and site', () => {
    expect(previewGroups([{ supplierId: 'a', siteId: 's' }, { supplierId: 'a', siteId: 's' }, { supplierId: 'b', siteId: 's' }, { supplierId: null, siteId: 's' }])).toBe(2);
    expect(previewGroups([{ supplierId: 'a', siteId: 's1' }, { supplierId: 'a', siteId: 's2' }])).toBe(2);
    expect(previewGroups([])).toBe(0);
  });

  it('splits short parts into one request per site with the work order and its due date', () => {
    const drafts = draftFromShortages([
      { partId: 'p1', storeId: 'st1', siteId: 's1', short: 2, workOrderId: 'w1', dueDate: '2026-11-30', description: 'Bearing', unitOfMeasure: 'EA' },
      { partId: 'p2', storeId: 'st1', siteId: 's1', short: 1.5, workOrderId: 'w2', dueDate: null, description: 'Seal', unitOfMeasure: 'EA' },
      { partId: 'p1', storeId: 'st9', siteId: 's2', short: 4, description: 'Bearing', unitOfMeasure: 'EA' },
    ]);
    expect(drafts.map((d) => d.siteId)).toEqual(['s1', 's2']);
    expect(drafts[0].lines).toHaveLength(2);
    expect(drafts[0].lines[0]).toMatchObject({ partId: 'p1', storeId: 'st1', quantity: 2, workOrderId: 'w1', neededBy: '2026-11-30' });
    expect(drafts[0].lines[1]).toMatchObject({ quantity: 1.5, workOrderId: 'w2', neededBy: null });
    expect(drafts[1].lines[0]).toMatchObject({ partId: 'p1', storeId: 'st9', quantity: 4, workOrderId: null });
  });

  it('marks requests late only while still open', () => {
    expect(isLate('2026-11-20', '2026-11-23', 'Approved')).toBe(true);
    expect(isLate('2026-11-23', '2026-11-23', 'Approved')).toBe(false);
    expect(isLate('2026-11-20', '2026-11-23', 'Ordered')).toBe(false);
    expect(isLate('2026-11-20', '2026-11-23', 'Cancelled')).toBe(false);
    expect(isLate(null, '2026-11-23', 'Draft')).toBe(false);
  });

  it('lets buyers order, requesters request and everyone else only look at the re-order proposal', () => {
    expect(proposalMode(true, false)).toBe('order');
    expect(proposalMode(true, true)).toBe('order');
    expect(proposalMode(false, true)).toBe('request');
    expect(proposalMode(false, false)).toBe('view');
  });
});
