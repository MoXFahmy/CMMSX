import { openDB, type DBSchema, type IDBPDatabase } from 'idb';
import type { OutboxStore, QueuedChange } from './outbox';
import type { TechBundle, TechPlaces, TechStock } from './types';

/**
 * What the phone keeps (IndexedDB "cmms-tech"): per user, the last bundle and places, the queue of changes and their
 * photos, and a few markers (last contact with the server). Survives closing the app and restarting the phone.
 */
interface TechDb extends DBSchema {
  bundle: { key: string; value: { userId: string; savedAt: number; etag?: string; data: TechBundle } };
  places: { key: string; value: { userId: string; savedAt: number; etag?: string; data: TechPlaces } };
  outbox: { key: string; value: QueuedChange; indexes: { byUser: string } };
  files: { key: string; value: Blob };
  meta: { key: string; value: unknown };
}

let opening: Promise<IDBPDatabase<TechDb>> | null = null;
export const techDb = () => (opening ??= openDB<TechDb>('cmms-tech', 1, {
  upgrade(db) {
    db.createObjectStore('bundle', { keyPath: 'userId' });
    db.createObjectStore('places', { keyPath: 'userId' });
    db.createObjectStore('outbox', { keyPath: 'clientId' }).createIndex('byUser', 'userId');
    db.createObjectStore('files');
    db.createObjectStore('meta');
  },
}));

export async function saveBundle(userId: string, data: TechBundle, etag?: string) { await (await techDb()).put('bundle', { userId, savedAt: Date.now(), etag, data }); }
export async function loadBundle(userId: string) { return (await techDb()).get('bundle', userId); }
export async function savePlaces(userId: string, data: TechPlaces, etag?: string) { await (await techDb()).put('places', { userId, savedAt: Date.now(), etag, data }); }
export async function loadPlaces(userId: string) { return (await techDb()).get('places', userId); }

/** Deletes the phone's copy of jobs and places (after a remote sign-out); the queue is kept for its owner. */
export async function wipeCopy(userId: string) {
  const db = await techDb();
  await db.delete('bundle', userId);
  await db.delete('places', userId);
  await db.delete('meta', `notes:${userId}`);
  await db.delete('meta', `stock:${userId}`);
}

/** The phone's copy of parts and stock (0.11.0), kept with the other small things. */
export async function saveStock(userId: string, data: TechStock, etag?: string) { await setMeta(`stock:${userId}`, { savedAt: Date.now(), etag, data }); }
export async function loadStock(userId: string) { return getMeta<{ savedAt: number; etag?: string; data: TechStock }>(`stock:${userId}`); }

export async function getMeta<T>(key: string): Promise<T | undefined> { return (await (await techDb()).get('meta', key)) as T | undefined; }
export async function setMeta(key: string, value: unknown) {
  const db = await techDb();
  if (value === undefined) await db.delete('meta', key); else await db.put('meta', value, key);
}

/** Adds a change to the queue (with its photo, if any), numbered after everything already queued. */
export async function enqueue(c: Omit<QueuedChange, 'seq' | 'state' | 'tries'>, file?: Blob) {
  const db = await techDb();
  const seq = ((await getMeta<number>('seq')) ?? 0) + 1;
  await setMeta('seq', seq);
  if (file && c.fileKey) await db.put('files', file, c.fileKey);
  await db.put('outbox', { ...c, seq, state: 'waiting', tries: 0 });
}

export async function queued(userId: string) { return (await techDb()).getAllFromIndex('outbox', 'byUser', userId); }

export function idbOutboxStore(): OutboxStore {
  return {
    list: (userId) => queued(userId),
    put: async (c) => { await (await techDb()).put('outbox', c); },
    remove: async (id) => { await (await techDb()).delete('outbox', id); },
    file: async (key) => (await techDb()).get('files', key),
    removeFile: async (key) => { await (await techDb()).delete('files', key); },
  };
}
