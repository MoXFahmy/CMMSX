import { describe, expect, it } from 'vitest';
import { alreadySigned, staleCompletions, needsLabour, safetyOpen, stepProblems } from './complete';
import type { QueuedChange } from './outbox';
import type { TechStep } from './types';

const q = (clientId: string, extra: Partial<QueuedChange>): QueuedChange => ({
  clientId, userId: 'u', seq: 1, at: '2026-11-20T08:00:00Z', type: 'status.change', workOrderId: 'w1', payload: { status: 'Completed' }, state: 'failed', tries: 0, ...extra,
});

describe('completing again', () => {
  it('knows who already signed (sent, or still waiting to be sent)', () => {
    const files = [{ isSignature: true, signedBy: 'Tess Tech' }, { isSignature: false, signedBy: null }];
    const queue = [q('s1', { type: 'photo.add', state: 'waiting', payload: { kind: 'signature', signedBy: 'Sam Site' } })];
    expect(alreadySigned('Tess Tech', files, queue, 'w1')).toBe(true);
    expect(alreadySigned('Sam Site', files, queue, 'w1')).toBe(true);
    expect(alreadySigned('Sam Site', files, queue, 'other-job')).toBe(false);
    expect(alreadySigned('Nobody', files, queue, 'w1')).toBe(false);
  });

  it('finds the earlier completions of this job that were refused', () => {
    const queue = [q('a', {}), q('b', { state: 'waiting' }), q('c', { workOrderId: 'w2' }), q('d', { payload: { status: 'OnHold' } })];
    expect(staleCompletions(queue, 'w1').map((c) => c.clientId)).toEqual(['a']);
  });
});

describe('needsLabour', () => {
  it('is satisfied by anyone\'s hours, as on the server', () => {
    expect(needsLabour({ hasLabor: true, myLabor: [] })).toBe(false);
    expect(needsLabour({ hasLabor: false, myLabor: [{ workDate: '2026-11-21', hours: 1, overtimeHours: 0 }] })).toBe(false);
    expect(needsLabour({ hasLabor: false, myLabor: [] })).toBe(true);
  });
  it('falls back to my hours on a copy saved before the server said', () => {
    expect(needsLabour({ myLabor: [] })).toBe(true);
  });
});

describe('checklist rules before completing (0.13.0)', () => {
  const job = {
    files: [] as { workOrderTaskId?: string | null }[],
    tasks: [
      { id: 'a', sequence: 1, description: 'Lock out', isDone: true, type: 'Check' as const, answer: 'Ok' as const, beforeStart: true },
      { id: 'b', sequence: 2, description: 'Temp', isDone: false, type: 'Number' as const },
      { id: 'c', sequence: 3, description: 'Belt', isDone: true, type: 'Check' as const, answer: 'Ok' as const, photoRequired: true },
    ],
  };
  it('needs a photo on photo steps, sent or waiting', () => {
    expect(stepProblems(job, [], { readings: false })).toEqual({ safety: [], photos: ['#3 Belt'], readings: [] });
    expect(stepProblems({ ...job, files: [{ workOrderTaskId: 'c' }] }, [], { readings: false }).photos).toEqual([]);
    const waiting = q('p1', { type: 'photo.add', state: 'waiting', payload: { kind: 'photo', taskId: 'c' } });
    expect(stepProblems(job, [waiting], { readings: false }).photos).toEqual([]);
  });
  it('needs readings when the organization asks', () => {
    expect(stepProblems(job, [], { readings: true }).readings).toEqual(['#2 Temp']);
  });
  it('knows the safety steps still open, per asset on a round', () => {
    const tasks = [
      { id: 's1', sequence: 1, description: 'Lock out A', isDone: false, type: 'Check' as const, beforeStart: true, assetId: 'A' },
      { id: 's2', sequence: 2, description: 'Lock out B', isDone: true, type: 'Check' as const, answer: 'Ok' as const, beforeStart: true, assetId: 'B' },
      { id: 's3', sequence: 3, description: 'Permit', isDone: true, type: 'Check' as const, answer: 'NotOk' as const, beforeStart: true },
    ];
    expect(safetyOpen(tasks, 'A').map((x) => x.id)).toEqual(['s1', 's3']);
    expect(safetyOpen(tasks, 'B').map((x) => x.id)).toEqual(['s3']);
  });
});

describe("completing needs every safety step (a round's assets too)", () => {
  const step = (id: string, extra: Partial<TechStep>): TechStep => ({ id, sequence: Number(id.slice(1)), description: id, isDone: false, reading: null, remarks: null, assetId: null, ...extra } as TechStep);
  it('lists safety steps not answered, or not OK, on any asset', () => {
    const tasks = [
      step('s1', { beforeStart: true, assetId: 'A' }), step('s2', { beforeStart: true, assetId: 'B', type: 'Check', answer: 'Ok', isDone: true }),
      step('s3', { beforeStart: true, assetId: 'C', type: 'Check', answer: 'NotOk', isDone: true }), step('s4', { assetId: 'A' }),
    ];
    expect(stepProblems({ tasks, files: [] }, [], {}).safety).toEqual(['#1 s1', '#3 s3']);
  });
});

