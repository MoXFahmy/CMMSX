import { describe, expect, it } from 'vitest';
import { dayRows, myDayChanges } from './day';
import type { QueuedChange } from './outbox';
import type { TechBundle, TechJob } from './types';

const job = (id: string, plannedStart: string | null, myLabor: TechJob['myLabor'] = []): TechJob =>
  ({ id, number: id.toUpperCase(), title: `Job ${id}`, type: 'Corrective', priority: 'Medium', status: 'Open', siteId: 's', plannedStart, roundAssets: [], tasks: [],
     plannedParts: [], files: [], comments: [], myLabor } as TechJob);
const bundle = (jobs: TechJob[], myDay?: TechBundle['myDay']): TechBundle =>
  ({ employeeId: 'me', userId: 'u', jobs, failureCodes: [], completionRules: { notes: false, labour: false, allSteps: false, failureCodeOnBreakdown: false }, myDay } as TechBundle);
const queued = (workOrderId: string, workDate: string, hours: number, employeeId?: string): QueuedChange =>
  ({ clientId: `c-${workOrderId}`, userId: 'u', seq: 1, at: '2026-11-23T08:00:00Z', type: 'labor.add', workOrderId, payload: { workDate, hours, overtimeHours: 0, employeeId }, state: 'waiting', tries: 0 });

describe('My day on the phone (0.19.0)', () => {
  const day = '2026-11-23';
  const myDay: TechBundle['myDay'] = {
    labour: [
      { workOrderId: 'a', number: 'A', title: 'Job a', workDate: day, hours: 1, overtimeHours: 0 },
      { workOrderId: 'done', number: 'DONE', title: 'Finished job', workDate: day, hours: 2, overtimeHours: 0.5 },
      { workOrderId: 'done', number: 'DONE', title: 'Finished job', workDate: '2026-11-20', hours: 4, overtimeHours: 0 },
    ],
    available: [{ day, hours: 6, away: ['Meeting'] }],
  };
  it('lists my jobs of the day, a finished job from my hours (read-only), and my available hours', () => {
    const r = dayRows(bundle([job('a', day, [{ workDate: day, hours: 1, overtimeHours: 0 }]), job('b', day), job('c', '2026-11-24')], myDay), day, []);
    expect(r.rows.map((x) => [x.workOrderId, x.hours, x.readOnly])).toEqual([['a', 1, false], ['b', 0, false], ['done', 2, true]]);
    expect([r.hours, r.overtimeHours, r.available]).toEqual([3, 0.5, 6]);
  });
  it('counts hours waiting to be sent for that day only, and not someone else\'s', () => {
    const r = dayRows(bundle([job('a', '2026-11-20'), job('b', day)], myDay), day, [queued('a', day, 2), queued('b', '2026-11-22', 5), queued('b', day, 3, 'other')]);
    expect(r.rows.find((x) => x.workOrderId === 'a')?.hours).toBe(2);
    expect(r.rows.find((x) => x.workOrderId === 'b')?.hours).toBe(0);
  });
  it('has no available hours without the server\'s figure', () => {
    expect(dayRows(bundle([job('a', day)]), day, []).available).toBeNull();
  });
});

describe('My day counts what was sent and leaves out what failed (0.19.0)', () => {
  const day = '2026-11-23';
  it('ignores a change that failed to send', () => {
    const failed = { ...queued('a', day, 4), state: 'failed' as const };
    expect(dayRows(bundle([job('a', day)]), day, [failed]).hours).toBe(0);
  });
  it('takes sent changes not yet in the copy, then those waiting, without the failed ones', () => {
    const sent = [{ ...queued('a', day, 2), clientId: 's1', sentAt: 1 }];
    const waiting = [queued('b', day, 1), { ...queued('c', day, 3), clientId: 'f1', state: 'failed' as const }];
    expect(myDayChanges(sent, waiting).map((c) => c.clientId)).toEqual(['s1', 'c-b']);
  });
});
