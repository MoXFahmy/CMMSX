import { describe, expect, it } from 'vitest';
import { stillShown, unitsWithQueued, withQueued } from './overlay';
import type { QueuedChange } from './outbox';
import type { StockUnit, TechJob } from './types';

const job: TechJob = {
  id: 'w1', number: 'WO-1', title: 'Pump', type: 'Corrective', priority: 'High', status: 'Open', siteId: 's', roundAssets: [], plannedParts: [], files: [],
  tasks: [{ id: 't1', sequence: 1, description: 'Check oil', isDone: false }], comments: [], myLabor: [],
};
const q = (seq: number, type: string, payload: unknown, extra: Partial<QueuedChange> = {}): QueuedChange =>
  ({ clientId: `c${seq}`, userId: 'u', seq, at: '2026-11-20T08:00:00Z', type, workOrderId: 'w1', payload, state: 'waiting', tries: 0, ...extra });

describe('withQueued', () => {
  it('shows parts used and given back at once, so nobody marks them used twice', () => {
    const lines = { ...job, plannedLines: [{ id: 'l1', partId: 'p1', storeId: 's1', quantity: 5, issued: 1 }], usedParts: [] };
    const j = withQueued(lines, [
      q(1, 'part.use', { partId: 'p1', storeId: 's1', quantity: 2, lineId: 'l1' }),
      q(2, 'part.use', { partId: 'p2', storeId: 's1', quantity: 3 }),
      q(3, 'part.return', { partId: 'p2', storeId: 's1', quantity: 1 }),
      q(4, 'stock.issue', { partId: 'p1', storeId: 's1', quantity: 1, workOrderId: 'w1' }),
      q(5, 'part.return', { partId: 'p1', storeId: 's1', quantity: 1 }),
    ]);
    expect(j.plannedLines![0].issued).toBe(3);
    expect(j.usedParts).toEqual([{ partId: 'p2', storeId: 's1', issued: 2 }]);
  });

  it('shows parts as asked for, not used, for people who may not issue stock', () => {
    const lines = { ...job, plannedLines: [{ id: 'l1', partId: 'p1', storeId: 's1', quantity: 1, issued: 0 }] };
    const j = withQueued(lines, [
      q(1, 'part.use', { partId: 'p1', storeId: 's1', quantity: 2, lineId: 'l1' }),
      q(2, 'part.use', { partId: 'p2', storeId: 's1', quantity: 1 }),
    ], { canIssue: false });
    expect(j.plannedLines!.map((l) => [l.partId, l.quantity, l.issued])).toEqual([['p1', 2, 0], ['p2', 1, 0]]);
    expect(j.usedParts ?? []).toEqual([]);
  });

  it('shows waiting changes on the job at once', () => {
    const j = withQueued(job, [
      q(1, 'task.update', { taskId: 't1', isDone: true, reading: '42' }),
      q(2, 'labor.add', { workDate: '2026-11-20', hours: 2, overtimeHours: 0 }),
      q(3, 'status.change', { status: 'InProgress' }),
      q(4, 'photo.add', { kind: 'photo' }, { fileKey: 'f' }),
      q(5, 'photo.add', { kind: 'signature' }, { fileKey: 'g' }),
    ]);
    expect(j.tasks[0]).toMatchObject({ isDone: true, reading: '42' });
    expect(j.myLabor[0].hours).toBe(2);
    expect(j.status).toBe('InProgress');
    expect([j.pendingPhotos, j.pendingSignatures]).toEqual([1, 1]);
  });

  it('shows typed answers with their level, and photos waiting on a step', () => {
    const typed: TechJob = { ...job, tasks: [
      { id: 't1', sequence: 1, description: 'Temp', isDone: false, type: 'Number', measurementPointId: 'p1' },
      { id: 't2', sequence: 2, description: 'Belt', isDone: false, type: 'Check' },
      { id: 't3', sequence: 3, description: 'Notes', isDone: false, type: 'Text' },
    ] };
    const j = withQueued(typed, [
      q(1, 'task.update', { taskId: 't1', value: 33 }), q(2, 'task.update', { taskId: 't2', answer: 'NotOk' }), q(3, 'task.update', { taskId: 't3', text: 'worn' }),
      q(4, 'photo.add', { kind: 'photo', taskId: 't2' }, { fileKey: 'f' }),
    ], { points: [{ id: 'p1', alarmHigh: 32 }] });
    expect(j.tasks[0]).toMatchObject({ value: 33, isDone: true, level: 'AlarmHigh' });
    expect(j.tasks[1]).toMatchObject({ answer: 'NotOk', isDone: true });
    expect(j.tasks[2]).toMatchObject({ textValue: 'worn', isDone: true });
    expect(j.stepPhotos).toEqual({ t2: 1 });
    const cleared = withQueued(typed, [q(1, 'task.update', { taskId: 't1', value: 33 }), q(2, 'task.update', { taskId: 't1', isDone: false })], { points: [{ id: 'p1', alarmHigh: 32 }] });
    expect(cleared.tasks[0]).toMatchObject({ value: null, isDone: false, level: null });
  });

  it('shows the cause and remedy of a queued completion (0.14.0), and keeps codes a completion leaves out', () => {
    const coded = { ...job, failureCodeId: 'c0', remedyId: 'r0' } as typeof job;
    expect(withQueued(coded, [q(1, 'status.change', { status: 'Completed', failureCodeId: 'c1', remedyId: 'r1' })])).toMatchObject({ status: 'Completed', failureCodeId: 'c1', remedyId: 'r1' });
    expect(withQueued(coded, [q(1, 'status.change', { status: 'Completed' })])).toMatchObject({ failureCodeId: 'c0', remedyId: 'r0' });
  });

  it("leaves out changes that couldn't be sent and other jobs' changes", () => {
    const j = withQueued(job, [q(1, 'status.change', { status: 'Completed' }, { state: 'failed' }), q(2, 'status.change', { status: 'OnHold' }, { workOrderId: 'w2' })]);
    expect(j.status).toBe('Open');
  });
});

describe('stillShown', () => {
  const sent = [
    { ...q(1, 'task.update', { taskId: 't1', isDone: true }), sentAt: 100 },
    { ...q(2, 'photo.add', { kind: 'photo' }, { fileKey: 'f' }), sentAt: 100 },
    { ...q(3, 'comment.add', { body: 'Done' }), sentAt: 300 },
  ];
  it('keeps sent changes on screen until a copy fetched after them arrives', () => {
    expect(stillShown(sent, 200).map((c) => c.clientId)).toEqual(['c3']);
    expect(stillShown(sent, undefined).map((c) => c.clientId)).toEqual(['c1', 'c3']);
  });
  it('does not count a sent photo as still waiting', () => {
    expect(stillShown(sent, undefined).some((c) => c.type === 'photo.add')).toBe(false);
  });
});

describe('serial units on the phone (0.24.0)', () => {
  const spare: StockUnit = { id: 'u1', assetNumber: 'AST-9', serialNumber: 'SN-1', partId: 'p1', storeId: 's1' };
  const onJob = { ...job, asset: { id: 'a1', assetNumber: 'CH-2', name: 'Chiller 2' }, defaultStoreId: 's1', units: [] };
  it('a queued fit moves the unit onto the job', () => {
    const queue = [q(1, 'unit.fit', { unitId: 'u1', assetId: 'a1', serialNumber: 'SN-1', assetNumber: 'AST-9', partId: 'p1', partName: 'Pump' })];
    expect(withQueued(onJob, queue).units).toEqual([{ id: 'u1', assetNumber: 'AST-9', serialNumber: 'SN-1', partId: 'p1', partName: 'Pump', fittedToId: 'a1' }]);
    expect(unitsWithQueued([spare], queue, [onJob])).toEqual([]);
  });
  it('a queued removal to store puts it back in stock', () => {
    const fitted = { ...onJob, units: [{ id: 'u1', assetNumber: 'AST-9', serialNumber: 'SN-1', partId: 'p1', partName: 'Pump', fittedToId: 'a1' }] };
    const toStore = [q(1, 'unit.remove', { unitId: 'u1', outcome: 'Store' })];
    expect(withQueued(fitted, toStore).units).toEqual([]);
    expect(unitsWithQueued([], toStore, [fitted])).toEqual([spare]);
    // To repair or scrap, it doesn't come back to the store.
    expect(unitsWithQueued([], [q(1, 'unit.remove', { unitId: 'u1', outcome: 'Repair' })], [fitted])).toEqual([]);
  });
});
