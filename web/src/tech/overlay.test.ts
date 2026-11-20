import { describe, expect, it } from 'vitest';
import { withQueued } from './overlay';
import type { QueuedChange } from './outbox';
import type { TechJob } from './types';

const job: TechJob = {
  id: 'w1', number: 'WO-1', title: 'Pump', type: 'Corrective', priority: 'High', status: 'Open', siteId: 's', roundAssets: [], plannedParts: [], files: [],
  tasks: [{ id: 't1', sequence: 1, description: 'Check oil', isDone: false }], comments: [], myLabor: [],
};
const q = (seq: number, type: string, payload: unknown, extra: Partial<QueuedChange> = {}): QueuedChange =>
  ({ clientId: `c${seq}`, userId: 'u', seq, at: '2026-11-20T08:00:00Z', type, workOrderId: 'w1', payload, state: 'waiting', tries: 0, ...extra });

describe('withQueued', () => {
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

  it("leaves out changes that couldn't be sent and other jobs' changes", () => {
    const j = withQueued(job, [q(1, 'status.change', { status: 'Completed' }, { state: 'failed' }), q(2, 'status.change', { status: 'OnHold' }, { workOrderId: 'w2' })]);
    expect(j.status).toBe('Open');
  });
});
