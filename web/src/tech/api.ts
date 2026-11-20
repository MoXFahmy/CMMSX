import type { QueuedChange, SendResult } from './outbox';
import type { TechBundle, TechPlaces } from './types';

/** Thrown when the server can't be reached or answers 5xx (the phone keeps working from its copy). */
export class Offline extends Error {}
/** Thrown on 401: the sign-in ended, or (revoked) this device was signed out. */
export class SignedOut extends Error { constructor(public revoked: boolean) { super('Signed out'); } }

async function call(path: string, init?: RequestInit): Promise<Response> {
  let res: Response;
  try { res = await fetch(`/api${path}`, { credentials: 'same-origin', ...init, headers: { Accept: 'application/json', ...(init?.headers ?? {}) } }); }
  catch { throw new Offline(); }
  if (res.status === 401) throw new SignedOut(res.headers.get('X-Session-Revoked') === '1');
  if (res.status >= 500) throw new Offline();
  return res;
}

const title = async (res: Response) => { try { return ((await res.json()) as { title?: string }).title ?? `Request failed (${res.status})`; } catch { return `Request failed (${res.status})`; } };

export async function fetchBundle(): Promise<TechBundle> {
  const res = await call('/tech/bundle');
  if (!res.ok) throw new Error(await title(res));
  return res.json();
}

/** The places of my sites, or null when the phone's copy (etag) is still current. */
export async function fetchPlaces(etag?: string): Promise<{ data: TechPlaces; etag?: string } | null> {
  const res = await call('/tech/places', { headers: etag ? { 'If-None-Match': etag } : {} });
  if (res.status === 304) return null;
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
      form.set('at', c.at);
      if (c.workOrderId) form.set('workOrderId', c.workOrderId);
      const p = c.payload as { kind: 'photo' | 'signature'; signedBy?: string; fileName?: string };
      form.set('kind', p.kind);
      if (p.signedBy) form.set('signedBy', p.signedBy);
      if (!file) return { kind: 'rejected', message: 'The photo is no longer on this phone.' };
      form.set('file', file, p.fileName ?? `${p.kind}.jpg`);
      res = await call('/tech/files', { method: 'POST', body: form });
    } else {
      res = await call('/tech/actions', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ clientId: c.clientId, at: c.at, type: c.type, workOrderId: c.workOrderId, payload: c.payload }),
      });
    }
    if (!res.ok) return { kind: 'rejected', message: await title(res) };
    const a = (await res.json()) as { status: 'done' | 'rejected'; message?: string; code?: string };
    return a.status === 'done' ? { kind: 'done', message: a.message } : { kind: 'rejected', message: a.message ?? 'Not accepted.', code: a.code };
  } catch (e) {
    if (e instanceof SignedOut) return { kind: 'signedOut', revoked: e.revoked };
    return { kind: 'retry' };
  }
}
