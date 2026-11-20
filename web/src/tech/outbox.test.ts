import { describe, expect, it } from 'vitest';
import { backoffMs, drain, retryNow, type OutboxStore, type QueuedChange, type SendResult } from './outbox';

/** An in-memory queue, as IndexedDB would keep it. */
function memoryStore(items: QueuedChange[], files: Record<string, Blob> = {}): OutboxStore & { items: Map<string, QueuedChange>; files: Record<string, Blob> } {
  const map = new Map(items.map((c) => [c.clientId, c]));
  return {
    items: map,
    files,
    list: async (userId) => [...map.values()].filter((c) => c.userId === userId),
    put: async (c) => { map.set(c.clientId, c); },
    remove: async (id) => { map.delete(id); },
    file: async (key) => files[key],
    removeFile: async (key) => { delete files[key]; },
  };
}

const change = (clientId: string, seq: number, userId = 'u1', extra: Partial<QueuedChange> = {}): QueuedChange => ({
  clientId, userId, seq, at: '2026-11-20T08:00:00Z', type: 'comment.add', workOrderId: 'w1', payload: { body: clientId }, state: 'waiting', tries: 0, ...extra,
});

const answers = (map: Record<string, SendResult>) => {
  const sent: string[] = [];
  const send = async (c: QueuedChange) => { sent.push(c.clientId); return map[c.clientId] ?? { kind: 'done' as const }; };
  return { sent, send };
};

describe('drain', () => {
  it('sends oldest first and removes what the server took', async () => {
    const store = memoryStore([change('b', 2), change('a', 1), change('c', 3)]);
    const { sent, send } = answers({});
    const r = await drain(store, 'u1', send, () => 0);
    expect(sent).toEqual(['a', 'b', 'c']);
    expect(r).toEqual({ sent: 3, failed: 0 });
    expect(store.items.size).toBe(0);
  });

  it('keeps a rejected change as failed with the reason and goes on to the next', async () => {
    const store = memoryStore([change('a', 1), change('b', 2)]);
    const { sent, send } = answers({ a: { kind: 'rejected', message: 'This work order is cancelled.', code: undefined } });
    const r = await drain(store, 'u1', send, () => 0);
    expect(sent).toEqual(['a', 'b']);
    expect(r).toEqual({ sent: 1, failed: 1 });
    expect(store.items.get('a')).toMatchObject({ state: 'failed', message: 'This work order is cancelled.' });
    // A failed change waits for Retry or Discard: it isn't sent again by the next drain.
    const again = answers({});
    await drain(store, 'u1', again.send, () => 0);
    expect(again.sent).toEqual([]);
  });

  it('stops on retry and backs off 5 s, 10 s, 20 s … up to 5 minutes', async () => {
    expect([0, 1, 2, 3, 10].map(backoffMs)).toEqual([5_000, 10_000, 20_000, 40_000, 300_000]);
    const store = memoryStore([change('a', 1), change('b', 2)]);
    const { sent, send } = answers({ a: { kind: 'retry' } });
    const r = await drain(store, 'u1', send, () => 1_000);
    expect(r.stoppedBy).toBe('retry');
    expect(sent).toEqual(['a']);
    expect(store.items.get('a')).toMatchObject({ state: 'waiting', tries: 1, nextTryAt: 6_000 });
    // Too early: nothing is sent; once the time has come, it is.
    const early = answers({});
    expect((await drain(store, 'u1', early.send, () => 5_999)).stoppedBy).toBe('backoff');
    expect(early.sent).toEqual([]);
    const later = answers({});
    await drain(store, 'u1', later.send, () => 6_000);
    expect(later.sent).toEqual(['a', 'b']);
  });

  it('says it is only waiting (not offline) when the next try is not due, and can skip the wait', async () => {
    const store = memoryStore([change('a', 1, 'u1', { tries: 2, nextTryAt: 60_000 })]);
    const idle = answers({});
    expect((await drain(store, 'u1', idle.send, () => 1_000)).stoppedBy).toBe('backoff');
    expect(idle.sent).toEqual([]);
    const now = answers({});
    expect(await drain(store, 'u1', now.send, () => 1_000, undefined, { ignoreBackoff: true })).toEqual({ sent: 1, failed: 0 });
    expect(now.sent).toEqual(['a']);
  });

  it("never sends another user's changes", async () => {
    const store = memoryStore([change('a1', 1, 'A'), change('b1', 2, 'B'), change('a2', 3, 'A')]);
    const { sent, send } = answers({});
    await drain(store, 'B', send, () => 0);
    expect(sent).toEqual(['b1']);
    expect([...store.items.keys()].sort()).toEqual(['a1', 'a2']);
  });

  it('stops without losing anything when signed out', async () => {
    const store = memoryStore([change('a', 1), change('b', 2)]);
    const { sent, send } = answers({ a: { kind: 'signedOut', revoked: false } });
    const r = await drain(store, 'u1', send, () => 0);
    expect(r.stoppedBy).toBe('signedOut');
    expect(sent).toEqual(['a']);
    expect(store.items.get('a')).toMatchObject({ state: 'waiting', tries: 0 });
    expect(store.items.has('b')).toBe(true);
  });

  it('sends the photo with its change and forgets it once sent', async () => {
    const photo = new Blob(['jpeg']);
    const store = memoryStore([change('p', 1, 'u1', { type: 'photo.add', fileKey: 'f1' })], { f1: photo });
    let got: Blob | undefined;
    await drain(store, 'u1', async (_c, file) => { got = file; return { kind: 'done' }; }, () => 0);
    expect(got).toBe(photo);
    expect(store.files.f1).toBeUndefined();
  });

  it('reports a note the server sent with a done change', async () => {
    const store = memoryStore([change('a', 1)]);
    const notes: string[] = [];
    await drain(store, 'u1', async () => ({ kind: 'done', message: 'This job was already completed.' }), () => 0, (_c, m) => notes.push(m));
    expect(notes).toEqual(['This job was already completed.']);
  });
});

describe('retryNow', () => {
  it('puts a failed change back in the queue, with force for a conflict', () => {
    const failed = change('a', 1, 'u1', { type: 'task.update', payload: { taskId: 't', reading: '7' }, state: 'failed', message: 'Changed by Olga', code: 'conflict', tries: 3, nextTryAt: 99 });
    const plain = retryNow(failed, false, 'a2');
    expect(plain).toMatchObject({ state: 'waiting', tries: 0, message: undefined, code: undefined, nextTryAt: undefined });
    // The server keeps its answer per id, and nothing was applied: a retry goes out under a new id, keeping its place.
    expect([plain.clientId, plain.seq]).toEqual(['a2', 1]);
    expect((plain.payload as { force?: boolean }).force).toBeUndefined();
    expect((retryNow(failed, true, 'a3').payload as { force?: boolean }).force).toBe(true);
  });
});
