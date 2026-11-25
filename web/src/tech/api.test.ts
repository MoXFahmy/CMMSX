import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchBundle, fetchMe, fillReport, Offline, refreshProblem, sendChange, ServerError, SignedOut, uploadTimeoutMs } from './api';
import type { QueuedChange } from './outbox';

const change = (extra: Partial<QueuedChange> = {}): QueuedChange => ({
  clientId: 'c1', userId: 'ann', seq: 1, at: '2026-11-20T08:00:00Z', type: 'comment.add', workOrderId: 'w1', payload: { body: 'Hi' }, state: 'waiting', tries: 0, ...extra,
});

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

describe('api', () => {
  it('gives a big video time to go up on a slow connection', () => {
    expect(uploadTimeoutMs(0)).toBe(60_000);
    expect(uploadTimeoutMs(50 * 1024 * 1024)).toBeGreaterThan(15 * 60_000);
  });

  it('tells a file upload that timed out apart from no signal', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', (_url: string, init: RequestInit) => new Promise((_, reject) => {
      init.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
    }));
    const c: QueuedChange = { clientId: 'c1', userId: 'u', seq: 1, at: '2026-11-20T08:00:00Z', type: 'photo.add', workOrderId: 'w1',
      payload: { kind: 'file', fileName: 'v.mp4' }, state: 'waiting', tries: 0, fileKey: 'f' };
    const r = sendChange(c, new Blob(['x']));
    await vi.advanceTimersByTimeAsync(61_000);
    expect(await r).toEqual({ kind: 'retry', timedOut: true });
    vi.stubGlobal('fetch', async () => { throw new TypeError('Failed to fetch'); });
    expect(await sendChange(c, new Blob(['x']))).toEqual({ kind: 'retry' });
    vi.useRealTimers();
  });

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

  it('asks for the jobs only if they changed since the copy on the phone', async () => {
    let sent: string | null = null;
    vi.stubGlobal('fetch', async (_url: string, init: RequestInit) => {
      sent = new Headers(init.headers).get('If-None-Match');
      return new Response(null, { status: 304 });
    });
    expect(await fetchBundle('"v1"')).toBeNull();
    expect(sent).toBe('"v1"');
  });

  it('returns new jobs with their version', async () => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ jobs: [] }), { status: 200, headers: { ETag: '"v2"' } }));
    expect(await fetchBundle('"v1"')).toEqual({ data: { jobs: [] }, etag: '"v2"' });
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

  it('a photo for a step carries the step text, a photo for nothing does not', async () => {
    let form: FormData | undefined;
    vi.stubGlobal('fetch', async (_url: string, init: RequestInit) => { form = init.body as FormData; return new Response(JSON.stringify({ status: 'done' }), { status: 200 }); });
    await sendChange(change({ fileKey: 'c1', payload: { kind: 'photo', fileName: 'p.jpg', taskId: 't1', stepText: 'Check belt tension' } }), new Blob(['x'], { type: 'image/jpeg' }));
    expect([form?.get('taskId'), form?.get('stepText')]).toEqual(['t1', 'Check belt tension']);
    await sendChange(change({ fileKey: 'c1', payload: { kind: 'photo', fileName: 'p.jpg' } }), new Blob(['x'], { type: 'image/jpeg' }));
    expect(form?.has('stepText')).toBe(false);
    await sendChange(change({ fileKey: 'c1', payload: { kind: 'photo', fileName: 'p.jpg', taskId: 't1', stepText: 'x'.repeat(900) } }), new Blob(['x'], { type: 'image/jpeg' }));
    expect(String(form?.get('stepText')).length).toBe(500);
  });

  it('a number beyond the range comes back as a refusal with its message, not a retry', async () => {
    const message = 'Enter a number between -99,999,999,999,999 and 99,999,999,999,999.';
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ status: 'rejected', message }), { status: 200 }));
    expect(await sendChange(change({ type: 'task.update', payload: { taskId: 't1', value: 1e15 } }))).toEqual({ kind: 'rejected', message, code: undefined });
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

  it('says the AI didn\'t answer when the server or the CDN gives up, instead of a bare error', async () => {
    const noAnswer = "The AI assistant didn't answer. Try again in a moment.";
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ title: noAnswer }), { status: 502, headers: { 'Content-Type': 'application/problem+json' } }));
    await expect(fillReport('w1', 'notes')).rejects.toThrow(noAnswer);
    vi.stubGlobal('fetch', async () => new Response('<html>504 Gateway Timeout</html>', { status: 504 }));
    await expect(fillReport('w1', 'notes')).rejects.toThrow(noAnswer);
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ title: 'This month\'s AI allowance (2) is used up.' }), { status: 429 }));
    await expect(fillReport('w1', 'notes')).rejects.toThrow('This month\'s AI allowance (2) is used up.');
  });

  it('a 401 with X-Password-Expired says the password expired (0.25.0)', async () => {
    vi.stubGlobal('fetch', async () => new Response('{}', { status: 401, headers: { 'X-Password-Expired': '1' } }));
    const e = await fetchMe().catch((x: unknown) => x);
    expect(e).toBeInstanceOf(SignedOut);
    expect((e as SignedOut).passwordExpired).toBe(true);
    expect((e as SignedOut).revoked).toBe(false);
    const r = await sendChange({ clientId: 'c1', userId: 'u1', seq: 1, at: '2026-11-24T08:00:00Z', type: 'comment.add', payload: {}, state: 'waiting', tries: 0 });
    expect(r).toEqual({ kind: 'signedOut', revoked: false, passwordExpired: true });
    vi.unstubAllGlobals();
  });

  it('a plain 401 is just signed out', async () => {
    vi.stubGlobal('fetch', async () => new Response('{}', { status: 401, headers: { 'X-Session-Revoked': '1' } }));
    const e = (await fetchMe().catch((x: unknown) => x)) as SignedOut;
    expect([e.revoked, e.passwordExpired]).toEqual([true, false]);
    vi.unstubAllGlobals();
  });
});
