import { describe, expect, it } from 'vitest';
import { alreadySigned, staleCompletions } from './complete';
import type { QueuedChange } from './outbox';

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
