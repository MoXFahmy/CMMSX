import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchMe, Offline, refreshProblem, sendChange, ServerError, SignedOut } from './api';
import type { QueuedChange } from './outbox';

const change = (extra: Partial<QueuedChange> = {}): QueuedChange => ({
  clientId: 'c1', userId: 'ann', seq: 1, at: '2026-11-20T08:00:00Z', type: 'comment.add', workOrderId: 'w1', payload: { body: 'Hi' }, state: 'waiting', tries: 0, ...extra,
});

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

describe('api', () => {
  it('gives up on "who is signed in" after a few seconds without an answer (weak signal)', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', (_url: string, init: RequestInit) => new Promise((_, reject) => {
      init.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
    }));
    const me = fetchMe();
    const check = expect(me).rejects.toBeInstanceOf(Offline);
    await vi.advanceTimersByTimeAsync(6_000);
    await check;
  });

  it('a queued change says who made it', async () => {
    let body = '';
    vi.stubGlobal('fetch', async (_url: string, init: RequestInit) => { body = String(init.body); return new Response(JSON.stringify({ status: 'done' }), { status: 200 }); });
    expect(await sendChange(change())).toEqual({ kind: 'done', message: undefined });
    expect(JSON.parse(body).userId).toBe('ann');
  });

  it('a photo says who took it', async () => {
    let form: FormData | undefined;
    vi.stubGlobal('fetch', async (_url: string, init: RequestInit) => { form = init.body as FormData; return new Response(JSON.stringify({ status: 'done' }), { status: 200 }); });
    await sendChange(change({ fileKey: 'c1', payload: { kind: 'photo', fileName: 'p.jpg' } }), new Blob(['x'], { type: 'image/jpeg' }));
    expect(form?.get('userId')).toBe('ann');
  });

  it('a change made by someone else than who is signed in now comes back as otherUser', async () => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ status: 'otherUser', code: 'otherUser' }), { status: 409 }));
    expect(await sendChange(change())).toEqual({ kind: 'otherUser' });
  });

  it('a server error is told apart from no signal', async () => {
    vi.stubGlobal('fetch', async () => new Response('', { status: 500 }));
    expect(await sendChange(change())).toEqual({ kind: 'retry', serverError: true });
    vi.stubGlobal('fetch', async () => { throw new TypeError('Failed to fetch'); });
    expect(await sendChange(change())).toEqual({ kind: 'retry' });
  });

  it('when the phone knows it has no signal, answers "offline" at once without trying the network', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    vi.stubGlobal('navigator', { onLine: false });
    await expect(fetchMe()).rejects.toBeInstanceOf(Offline);
    expect(fetch).not.toHaveBeenCalled();
  });
});

describe('refreshProblem', () => {
  it('says why the jobs could not be refreshed, in the server\'s words', () => {
    expect(refreshProblem(new Error("Your account isn't linked to an employee. Ask an administrator to link it under People.")))
      .toBe("Your account isn't linked to an employee. Ask an administrator to link it under People.");
  });
  it('tells a server failure apart from no signal', () => {
    expect(refreshProblem(new ServerError())).toBe('server');
    expect(refreshProblem(new Offline())).toBeNull();
    expect(refreshProblem(new SignedOut(false))).toBeNull();
  });
});
