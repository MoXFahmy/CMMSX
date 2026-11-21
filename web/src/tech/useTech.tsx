import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { fetchBundle, fetchMe, fetchPlaces, logout, Offline, refreshProblem, sendChange, ServerError, SignedOut } from './api';
import { confirmUser, forgetUser, type IdentityDeps } from './identity';
import { drain, retryNow, type QueuedChange, type SendResult } from './outbox';
import { stillShown, type SentChange } from './overlay';
import { enqueue, getMeta, idbOutboxStore, loadBundle, loadPlaces, queued, saveBundle, savePlaces, setMeta, techDb, wipeCopy } from './store';
import type { TechBundle, TechPlaces } from './types';
import { registerTechWorker } from './worker';

/** starting → ready; or why the technician view can't show jobs. */
export type Phase = 'starting' | 'ready' | 'signedOut' | 'revoked' | 'locked' | 'noCopy';

const LOCK_AFTER_MS = 30 * 24 * 3600_000;
const identity: IdentityDeps = { fetchMe, getMeta, setMeta, wipeCopy };
const REFRESH_EVERY_MS = 15 * 60_000;
const DRAIN_EVERY_MS = 30_000;
/** "full" when this device last chose the full app over the technician view (the Android app opens /tech). */
export const MODE_KEY = 'cmms.tech.mode';

type Note = { at: string; text: string };
type TechState = {
  phase: Phase; userId?: string; fullName?: string; bundle?: TechBundle; places?: TechPlaces; queue: QueuedChange[]; online: boolean;
  /** Changes the server took that the phone's copy doesn't show yet (shown on top of it like waiting ones). */
  sent: SentChange[];
  lastRefresh?: number; notes: Note[]; busy: boolean;
  /** Why the last refresh failed, when it wasn't just missing signal ('server': the server failed). */
  problem?: string;
  refresh(): Promise<void>; drainNow(now?: boolean): Promise<void>;
  add(change: { type: string; workOrderId?: string; payload: unknown }, file?: { blob: Blob; fileName: string }): Promise<void>;
  retry(c: QueuedChange, force?: boolean): Promise<void>; discard(c: QueuedChange): Promise<void>;
  /** Signs this device out and wipes the jobs on it (the queue waits for its owner); false when there is no signal. */
  signOut(): Promise<boolean>;
};

const Ctx = createContext<TechState | null>(null);
export const useTech = () => useContext(Ctx)!;

export function TechProvider({ children }: { children: ReactNode }) {
  const [phase, setPhase] = useState<Phase>('starting');
  const [userId, setUserId] = useState<string>();
  const [fullName, setFullName] = useState<string>();
  const [bundle, setBundle] = useState<TechBundle>();
  const [places, setPlaces] = useState<TechPlaces>();
  const [queue, setQueue] = useState<QueuedChange[]>([]);
  const [online, setOnline] = useState(true);
  const [lastRefresh, setLastRefresh] = useState<number>();
  const [notes, setNotes] = useState<Note[]>([]);
  const [busy, setBusy] = useState(false);
  const [sentRaw, setSent] = useState<SentChange[]>([]);
  const [problem, setProblem] = useState<string>();
  const [fetchedAt, setFetchedAt] = useState<number>();
  const draining = useRef(false);
  const user = useRef<string | undefined>(undefined);
  /** Whether the server has confirmed who is signed in since the view opened (not yet after an offline start). */
  const confirmed = useRef(false);

  const reloadQueue = useCallback(async () => { if (user.current) setQueue(await queued(user.current)); }, []);

  /** The server said this device was signed out: forget the jobs and places on the phone (the queue stays with its owner). */
  const revoked = useCallback(async () => {
    if (user.current) await wipeCopy(user.current);
    setBundle(undefined); setPlaces(undefined); setSent([]); setPhase('revoked');
  }, []);

  const contact = useCallback(async () => { setOnline(true); await setMeta('lastContactAt', Date.now()); }, []);

  /**
   * Before anything is sent or fetched: is the person whose copy this is still the one signed in? After an offline start
   * the phone only knows who used it last; if someone else signs in, it switches to them (wiping the other copy).
   */
  const ensureUser = useCallback(async (): Promise<boolean> => {
    if (confirmed.current) return true;
    try {
      const { me } = await confirmUser(identity);
      confirmed.current = true;
      if (!me.employeeId) { window.location.replace('/'); return false; }
      if (me.userId !== user.current) {
        user.current = me.userId; setUserId(me.userId); setFullName(me.fullName);
        setBundle(undefined); setPlaces(undefined); setSent([]);
        setNotes((await getMeta<Note[]>(`notes:${me.userId}`)) ?? []);
        await reloadQueue();
      }
      await contact();
      return true;
    } catch (e) {
      if (e instanceof Offline) setOnline(false);
      else if (e instanceof SignedOut) { if (e.revoked) await revoked(); else setPhase('signedOut'); }
      return false;
    }
  }, [contact, reloadQueue, revoked]);

  const refresh = useCallback(async () => {
    if (!(await ensureUser())) return;
    const id = user.current;
    if (!id) return;
    setBusy(true);
    try {
      const started = Date.now();
      const b = await fetchBundle();
      await saveBundle(id, b);
      setBundle(b);
      setFetchedAt(started);
      setSent((s) => stillShown(s, started));
      const saved = await loadPlaces(id);
      const p = await fetchPlaces(saved?.etag);
      if (p) { await savePlaces(id, p.data, p.etag); setPlaces(p.data); } else if (saved) setPlaces(saved.data);
      await contact();
      setLastRefresh(Date.now());
      setProblem(undefined);
    } catch (e) {
      setProblem(refreshProblem(e) ?? undefined);
      if (e instanceof Offline && !(e instanceof ServerError)) setOnline(false);
      else if (e instanceof SignedOut) { if (e.revoked) await revoked(); else setPhase('signedOut'); }
    } finally { setBusy(false); }
  }, [contact, ensureUser, revoked]);

  /** Sends the queue; `now` skips the wait between tries (start, signal back, "Refresh now", a new change). */
  const drainNow = useCallback(async (now = false) => {
    if (draining.current || !(await ensureUser())) return;
    const id = user.current;
    if (!id || draining.current) return;
    draining.current = true;
    let last: SendResult | undefined;
    try {
      const r = await drain(idbOutboxStore(), id, async (c, f) => (last = await sendChange(c, f)), Date.now, (_c, text) => {
        setNotes((n) => { const next = [{ at: new Date().toISOString(), text }, ...n].slice(0, 20); void setMeta(`notes:${id}`, next); return next; });
      }, { ignoreBackoff: now, onSent: (c) => setSent((s) => [...s, { ...c, sentAt: Date.now() }]) });
      if (r.sent > 0) { await contact(); void refresh(); }
      if (r.stoppedBy === 'retry') setOnline(false);
      if (r.stoppedBy === 'serverError') await contact(); // there is signal; the server failed
      if (r.stoppedBy === 'signedOut' && last?.kind === 'signedOut') { if (last.revoked) await revoked(); else setPhase('signedOut'); }
      if (r.stoppedBy === 'otherUser') confirmed.current = false; // someone else signed in meanwhile: check again next time
    } finally {
      draining.current = false;
      await reloadQueue();
    }
  }, [contact, ensureUser, refresh, reloadQueue, revoked]);

  // Start: who is signed in (with signal) or who used this phone last (without).
  useEffect(() => {
    void registerTechWorker();
    (async () => {
      try {
        // Records who is signed in before anything else (wiping someone else's copy), even if this goes on to the full app.
        const { me } = await confirmUser(identity);
        confirmed.current = true;
        if (!me.employeeId || localStorage.getItem(MODE_KEY) === 'full') { window.location.replace('/'); return; }
        user.current = me.userId; setUserId(me.userId); setFullName(me.fullName);
        await contact();
        const cached = await loadBundle(me.userId);
        if (cached) setBundle(cached.data);
        setNotes((await getMeta<Note[]>(`notes:${me.userId}`)) ?? []);
        await reloadQueue();
        setPhase('ready');
        await refresh();
        await drainNow(true);
      } catch (e) {
        if (e instanceof SignedOut && !e.revoked) { window.location.replace('/login?next=%2Ftech%2F'); return; }
        const id = await getMeta<string>('userId');
        user.current = id; setUserId(id); setFullName(await getMeta<string>('fullName') ?? undefined);
        if (e instanceof SignedOut) { await revoked(); return; }
        setOnline(false);
        if (!id) { setPhase('noCopy'); return; }
        await reloadQueue();
        if (Date.now() - ((await getMeta<number>('lastContactAt')) ?? 0) > LOCK_AFTER_MS) { setPhase('locked'); return; }
        const cached = await loadBundle(id);
        if (!cached) { setPhase('noCopy'); return; }
        setBundle(cached.data); setPlaces((await loadPlaces(id))?.data); setLastRefresh(cached.savedAt);
        setNotes((await getMeta<Note[]>(`notes:${id}`)) ?? []);
        setPhase('ready');
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Keep sending and refreshing while the view is open; try at once when the phone says the signal is back.
  useEffect(() => {
    if (phase !== 'ready') return;
    const send = window.setInterval(() => { void drainNow(); }, DRAIN_EVERY_MS);
    const fresh = window.setInterval(() => { void refresh(); }, REFRESH_EVERY_MS);
    const back = () => { void drainNow(true); void refresh(); };
    const gone = () => setOnline(false); // the phone lost its network: show "Offline" and hide the full-app links at once
    window.addEventListener('online', back);
    window.addEventListener('offline', gone);
    return () => { window.clearInterval(send); window.clearInterval(fresh); window.removeEventListener('online', back); window.removeEventListener('offline', gone); };
  }, [phase, drainNow, refresh]);

  const add = useCallback<TechState['add']>(async (change, file) => {
    const id = user.current;
    if (!id) return;
    const clientId = crypto.randomUUID();
    await enqueue({ clientId, userId: id, at: new Date().toISOString(), ...change, fileKey: file ? clientId : undefined,
      payload: file ? { ...(change.payload as object), fileName: file.fileName } : change.payload }, file?.blob);
    await reloadQueue();
    void drainNow(true);
  }, [drainNow, reloadQueue]);

  const retry = useCallback(async (c: QueuedChange, force = false) => {
    const store = idbOutboxStore();
    await store.put(retryNow(c, force, crypto.randomUUID())); // same place, same photo (fileKey), new id
    await store.remove(c.clientId);
    await reloadQueue();
    void drainNow(true);
  }, [drainNow, reloadQueue]);

  const discard = useCallback(async (c: QueuedChange) => {
    const store = idbOutboxStore();
    await store.remove(c.clientId);
    if (c.fileKey) await store.removeFile(c.fileKey);
    await reloadQueue();
  }, [reloadQueue]);

  const signOut = useCallback(async () => {
    try { await logout(); } catch (e) { if (!(e instanceof SignedOut)) return false; }
    await forgetUser(identity);
    window.location.replace('/login?next=%2Ftech%2F');
    return true;
  }, []);

  const sent = useMemo(() => stillShown(sentRaw, fetchedAt), [sentRaw, fetchedAt]);
  const value = useMemo<TechState>(() => ({ phase, userId, fullName, bundle, places, queue, sent, online, lastRefresh, notes, busy, problem, refresh, drainNow, add, retry, discard, signOut }),
    [phase, userId, fullName, bundle, places, queue, sent, online, lastRefresh, notes, busy, problem, refresh, drainNow, add, retry, discard, signOut]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/** Opens the database early so the first screen doesn't wait for it. */
export const warmUp = () => { void techDb(); };
