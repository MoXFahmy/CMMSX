/**
 * Whose copy is on the phone. The phone keeps one person's jobs (and remembers who); every change in the queue belongs to
 * whoever made it. On the first contact with the server after an offline start, and on every sign-in, the phone checks
 * who is really signed in: if it is someone else, the previous person's copy is wiped (their queue stays, for them).
 */
export type IdentityDeps = {
  fetchMe(): Promise<{ userId: string; fullName: string; employeeId?: string | null }>;
  getMeta<T>(key: string): Promise<T | undefined>;
  setMeta(key: string, value: unknown): Promise<void>;
  wipeCopy(userId: string): Promise<void>;
};

/** Who is signed in now (throws Offline or SignedOut); switches the phone to them, wiping the previous person's copy. */
export async function confirmUser(deps: IdentityDeps) {
  const me = await deps.fetchMe();
  const stored = await deps.getMeta<string>('userId');
  const switched = stored !== undefined && stored !== me.userId;
  if (switched) await deps.wipeCopy(stored);
  await deps.setMeta('userId', me.userId);
  await deps.setMeta('fullName', me.fullName);
  return { me, switched };
}

/** On sign-out: nothing of that person's jobs stays on the phone, and the phone no longer opens as them offline. */
export async function forgetUser(deps: Omit<IdentityDeps, 'fetchMe'>) {
  const stored = await deps.getMeta<string>('userId');
  if (stored !== undefined) await deps.wipeCopy(stored);
  await deps.setMeta('userId', undefined);
  await deps.setMeta('fullName', undefined);
}
