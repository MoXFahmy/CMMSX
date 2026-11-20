import { describe, expect, it } from 'vitest';
import { Offline } from './api';
import { confirmUser, forgetUser, type IdentityDeps } from './identity';

type Me = { userId: string; fullName: string; employeeId?: string | null };

function phone(stored?: string, me?: Me | Error) {
  const meta = new Map<string, unknown>(stored ? [['userId', stored], ['fullName', 'Ann Previous']] : []);
  const wiped: string[] = [];
  const deps: IdentityDeps = {
    fetchMe: async () => { if (me instanceof Error) throw me; return me!; },
    getMeta: async <T,>(k: string) => meta.get(k) as T | undefined,
    setMeta: async (k: string, v: unknown) => { if (v === undefined) meta.delete(k); else meta.set(k, v); },
    wipeCopy: async (id: string) => { wiped.push(id); },
  };
  return { meta, wiped, deps };
}

describe('confirmUser', () => {
  it("switches to whoever is signed in now and wipes the previous person's copy", async () => {
    const p = phone('ann', { userId: 'bob', fullName: 'Bob Next', employeeId: 'e2' });
    const r = await confirmUser(p.deps);
    expect(r).toMatchObject({ switched: true, me: { userId: 'bob' } });
    expect(p.wiped).toEqual(['ann']);
    expect(p.meta.get('userId')).toBe('bob');
    expect(p.meta.get('fullName')).toBe('Bob Next');
  });

  it('keeps the copy when the same person is still signed in', async () => {
    const p = phone('ann', { userId: 'ann', fullName: 'Ann Previous', employeeId: 'e1' });
    expect((await confirmUser(p.deps)).switched).toBe(false);
    expect(p.wiped).toEqual([]);
  });

  it('records who is signed in on a phone that had no copy yet', async () => {
    const p = phone(undefined, { userId: 'bob', fullName: 'Bob Next' });
    expect((await confirmUser(p.deps)).switched).toBe(false);
    expect(p.meta.get('userId')).toBe('bob');
  });

  it('changes nothing when the server cannot be reached', async () => {
    const p = phone('ann', new Offline());
    await expect(confirmUser(p.deps)).rejects.toBeInstanceOf(Offline);
    expect(p.wiped).toEqual([]);
    expect(p.meta.get('userId')).toBe('ann');
  });
});

describe('forgetUser', () => {
  it('signing out wipes the copy and forgets who used the phone', async () => {
    const p = phone('ann');
    await forgetUser(p.deps);
    expect(p.wiped).toEqual(['ann']);
    expect(p.meta.has('userId')).toBe(false);
    expect(p.meta.has('fullName')).toBe(false);
  });
});
