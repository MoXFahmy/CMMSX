import i18n from '@/i18n';
import type { QueuedChange, SendResult } from './outbox';
import type { TechBundle, TechPlaces, TechStock } from './types';

/** Thrown when the server can't be reached, doesn't answer in time, or answers 5xx (the phone keeps working from its copy). */
export class Offline extends Error {}
/** A 5xx answer: there is signal, the server failed. */
export class ServerError extends Offline {}
/** The request got no answer in time (a slow connection rather than none). */
export class TimedOut extends Offline {}
/** Thrown on 401: the sign-in ended, or (revoked) this device was signed out. */
export class SignedOut extends Error { constructor(public revoked: boolean) { super('Signed out'); } }

/**
 * What to tell the technician when refreshing the jobs failed: the server's reason (e.g. the account isn't linked to an
 * employee), 'server' when the server failed, or null when there is simply no signal or the sign-in ended (shown elsewhere).
 */
export function refreshProblem(e: unknown): string | null {
  if (e instanceof ServerError) return 'server';
  if (e instanceof Offline || e instanceof SignedOut) return null;
  return e instanceof Error && e.message ? e.message : 'server';
}

/** How long to wait for an answer: with one bar of signal a request can hang for minutes; the phone's copy is better. */
const QUICK_MS = 5_000;
const SEND_MS = 20_000;
const LOAD_MS = 30_000;
const UPLOAD_MS = 60_000;

/** A file upload's time limit: a minute, plus time for the file at about 50 kB/s (a 50 MB video gets about 18 minutes). */
export const uploadTimeoutMs = (bytes: number) => UPLOAD_MS + Math.ceil(bytes / 50);

async function call(path: string, init: RequestInit | undefined, timeoutMs: number): Promise<Response> {
  // The phone says there is no network (airplane mode): don't wait for a request that can't go anywhere.
  if (typeof navigator !== 'undefined' && navigator.onLine === false) throw new Offline();
  let res: Response;
  const abort = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; abort.abort(); }, timeoutMs);
  try { res = await fetch(`/api${path}`, { credentials: 'same-origin', ...init, signal: abort.signal, headers: { Accept: 'application/json', 'Accept-Language': i18n.language || 'en', ...(init?.headers ?? {}) } }); }
  catch { throw timedOut ? new TimedOut() : new Offline(); }
  finally { clearTimeout(timer); }
  if (res.status === 401) throw new SignedOut(res.headers.get('X-Session-Revoked') === '1');
  if (res.status >= 500) throw new ServerError();
  return res;
}

const title = async (res: Response) => { try { return ((await res.json()) as { title?: string }).title ?? `Request failed (${res.status})`; } catch { return `Request failed (${res.status})`; } };

/** Who is signed in (throws Offline or SignedOut). */
export async function fetchMe(): Promise<{ userId: string; employeeId?: string | null; fullName: string }> {
  const res = await call('/auth/me', undefined, QUICK_MS);
  if (!res.ok) throw new Error(await title(res));
  const me = (await res.json()) as { user: { id: string; fullName: string }; employeeId?: string | null };
  return { userId: me.user.id, employeeId: me.employeeId, fullName: me.user.fullName };
}

/** My jobs, or null when the phone's copy (etag) is still current. */
export async function fetchBundle(etag?: string): Promise<{ data: TechBundle; etag?: string } | null> {
  const res = await call('/tech/bundle', { headers: etag ? { 'If-None-Match': etag } : {} }, LOAD_MS);
  if (res.status === 304) return null;
  if (!res.ok) throw new Error(await title(res));
  return { data: await res.json(), etag: res.headers.get('ETag') ?? undefined };
}

/** The places of my sites, or null when the phone's copy (etag) is still current. */
export async function fetchPlaces(etag?: string): Promise<{ data: TechPlaces; etag?: string } | null> {
  const res = await call('/tech/places', { headers: etag ? { 'If-None-Match': etag } : {} }, LOAD_MS);
  if (res.status === 304) return null;
  if (!res.ok) throw new Error(await title(res));
  return { data: await res.json(), etag: res.headers.get('ETag') ?? undefined };
}

/** Parts and stock of my sites (0.11.0), or null when the phone's copy (etag) is still current; null too on an older server. */
export async function fetchStock(etag?: string): Promise<{ data: TechStock; etag?: string } | null> {
  const res = await call('/tech/stock', { headers: etag ? { 'If-None-Match': etag } : {} }, LOAD_MS);
  if (res.status === 304 || res.status === 404 || res.status === 405) return null;
  if (!res.ok) throw new Error(await title(res));
  return { data: await res.json(), etag: res.headers.get('ETag') ?? undefined };
}

/** Sends one queued change (and its photo); maps the answer for the sync loop. */
export async function sendChange(c: QueuedChange, file?: Blob): Promise<SendResult> {
  try {
    let res: Response;
    if (c.fileKey) {
      const form = new FormData();
      form.set('clientId', c.clientId);
      form.set('userId', c.userId);
      form.set('at', c.at);
      if (c.workOrderId) form.set('workOrderId', c.workOrderId);
      const p = c.payload as { kind: 'photo' | 'signature' | 'file'; signedBy?: string; fileName?: string };
      form.set('kind', p.kind);
      if (p.signedBy) form.set('signedBy', p.signedBy);
      if (!file) return { kind: 'rejected', message: 'The photo is no longer on this phone.' };
      form.set('file', file, p.fileName ?? `${p.kind}.jpg`);
      res = await call('/tech/files', { method: 'POST', body: form }, uploadTimeoutMs(file.size));
    } else {
      res = await call('/tech/actions', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ clientId: c.clientId, at: c.at, type: c.type, workOrderId: c.workOrderId, payload: c.payload, userId: c.userId }),
      }, SEND_MS);
    }
    if (res.status === 409) {
      const a = (await res.clone().json().catch(() => ({}))) as { code?: string };
      if (a.code === 'otherUser') return { kind: 'otherUser' };
    }
    if (!res.ok) return { kind: 'rejected', message: await title(res) };
    const a = (await res.json()) as { status: 'done' | 'rejected'; message?: string; code?: string };
    return a.status === 'done' ? { kind: 'done', message: a.message } : { kind: 'rejected', message: a.message ?? 'Not accepted.', code: a.code };
  } catch (e) {
    if (e instanceof SignedOut) return { kind: 'signedOut', revoked: e.revoked };
    if (e instanceof ServerError) return { kind: 'retry', serverError: true };
    return e instanceof TimedOut && c.fileKey ? { kind: 'retry', timedOut: true } : { kind: 'retry' };
  }
}

/** Signs this device out on the server (throws Offline when that can't be done now). */
/** The phone's Firebase token for push notifications (0.11.0); ignored when there is no signal (sent again next start). */
export async function sendPushToken(token: string): Promise<void> {
  try { await call('/tech/push-token', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) }, SEND_MS); } catch { /* next start */ }
}

export async function logout(): Promise<void> {
  await call('/auth/logout', { method: 'POST' }, SEND_MS);
}
