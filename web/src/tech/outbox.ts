/**
 * The technician's queue of changes made on the phone (with or without signal) and the loop that sends them. Pure logic:
 * storage and the network are passed in, so it is unit-tested (outbox.test.ts) and IndexedDB lives in store.ts.
 *
 * Rules: oldest first; a change the server took is removed; one it refused stays as "failed" with the reason until the
 * technician retries or discards it; a network or server error stops the loop and backs off (a change the server fails on
 * 5 times is set aside as failed, so it can't hold up the rest); a signed-out or "someone else is signed in" answer stops
 * the loop and touches nothing; only the signed-in user's changes are ever sent.
 */

export type QueuedChange = {
  /** The id the server uses to apply the change once, even if it is sent twice. */
  clientId: string;
  /** Who made it: it is only ever sent while this user is signed in. */
  userId: string;
  /** Order of entry. */
  seq: number;
  /** When the technician made it (ISO). */
  at: string;
  type: string;
  workOrderId?: string;
  payload: unknown;
  /** A photo or signature kept with the change (files store). */
  fileKey?: string;
  state: 'waiting' | 'sending' | 'failed';
  message?: string;
  code?: string;
  tries: number;
  nextTryAt?: number;
};

export interface OutboxStore {
  list(userId: string): Promise<QueuedChange[]>;
  put(c: QueuedChange): Promise<void>;
  remove(clientId: string): Promise<void>;
  file(key: string): Promise<Blob | undefined>;
  removeFile(key: string): Promise<void>;
}

export type SendResult =
  | { kind: 'done'; message?: string }
  | { kind: 'rejected'; message: string; code?: string }
  | { kind: 'retry'; serverError?: boolean }
  | { kind: 'signedOut'; revoked: boolean }
  /** The change was made by someone other than who is signed in now: keep it for its owner. */
  | { kind: 'otherUser' };

/**
 * stoppedBy: 'retry' = a send just failed (no signal); 'serverError' = the server failed (there is signal); 'backoff' = only
 * waiting for the next try; 'signedOut' = sign in again; 'otherUser' = someone else is signed in now.
 */
export type DrainResult = { sent: number; failed: number; stoppedBy?: 'retry' | 'serverError' | 'backoff' | 'signedOut' | 'otherUser' };

/** A change the server fails on this many times in a row is set aside as failed (Retry or Discard on the Sync page). */
export const SERVER_TRIES = 5;

/** Wait before the next try: 5 s, 10 s, 20 s … at most 5 minutes. */
export const backoffMs = (tries: number) => Math.min(5_000 * 2 ** tries, 300_000);

export async function drain(
  store: OutboxStore,
  userId: string,
  send: (c: QueuedChange, file?: Blob) => Promise<SendResult>,
  now: () => number,
  onNote?: (c: QueuedChange, message: string) => void,
  options: { ignoreBackoff?: boolean } = {},
): Promise<DrainResult> {
  const queue = (await store.list(userId)).filter((c) => c.userId === userId).sort((a, b) => a.seq - b.seq);
  let sent = 0;
  let failed = 0;
  for (const c of queue) {
    if (c.state === 'failed') { failed++; continue; }
    // Signal came back, or the technician asked: send now instead of sitting out the wait.
    if (!options.ignoreBackoff && c.nextTryAt !== undefined && c.nextTryAt > now()) return { sent, failed, stoppedBy: 'backoff' };
    const file = c.fileKey ? await store.file(c.fileKey) : undefined;
    await store.put({ ...c, state: 'sending' });
    const r = await send(c, file);
    switch (r.kind) {
      case 'done':
        await store.remove(c.clientId);
        if (c.fileKey) await store.removeFile(c.fileKey);
        if (r.message) onNote?.(c, r.message);
        sent++;
        break;
      case 'rejected':
        await store.put({ ...c, state: 'failed', message: r.message, code: r.code });
        failed++;
        break;
      case 'retry':
        if (r.serverError && c.tries + 1 >= SERVER_TRIES) {
          await store.put({ ...c, state: 'failed', tries: c.tries + 1, nextTryAt: undefined, code: 'serverError',
            message: `The server couldn't save this change (it failed ${SERVER_TRIES} times). Retry later, or discard it.` });
          failed++;
          break;
        }
        await store.put({ ...c, state: 'waiting', tries: c.tries + 1, nextTryAt: now() + backoffMs(c.tries) });
        return { sent, failed, stoppedBy: r.serverError ? 'serverError' : 'retry' };
      case 'signedOut':
      case 'otherUser':
        await store.put({ ...c, state: 'waiting' });
        return { sent, failed, stoppedBy: r.kind };
    }
  }
  return { sent, failed };
}

/**
 * Puts a change that couldn't be sent back in the queue, in its place; `force` applies it over a newer change (a checklist
 * conflict). It gets a new id: the server keeps its answer per id (so a repeat isn't applied twice), and the refused
 * attempt applied nothing, so the old id would only get the old answer back.
 */
export function retryNow(c: QueuedChange, force: boolean, newClientId: string): QueuedChange {
  const payload = force ? { ...(c.payload as object), force: true } : c.payload;
  return { ...c, clientId: newClientId, payload, state: 'waiting', tries: 0, message: undefined, code: undefined, nextTryAt: undefined };
}
