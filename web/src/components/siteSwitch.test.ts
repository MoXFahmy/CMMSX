import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getSite, setSite, siteHeader, SITE_KEY } from './siteSwitch';

/** A browser's storage, for tests that run without one. */
function memoryStorage(): Storage {
  const m = new Map<string, string>();
  return {
    get length() { return m.size; }, clear: () => m.clear(), key: (i) => [...m.keys()][i] ?? null,
    getItem: (k) => m.get(k) ?? null, setItem: (k, v) => { m.set(k, String(v)); }, removeItem: (k) => { m.delete(k); },
  };
}

describe('site switch', () => {
  beforeEach(() => vi.stubGlobal('localStorage', memoryStorage()));
  afterEach(() => vi.unstubAllGlobals());

  it('remembers the chosen site', () => {
    setSite('abc');
    expect(getSite()).toBe('abc');
    expect(localStorage.getItem(SITE_KEY)).toBe('abc');
  });

  it('clears it with null', () => {
    setSite('abc');
    setSite(null);
    expect(getSite()).toBeNull();
  });

  it('works without storage (private mode)', () => {
    vi.stubGlobal('localStorage', { getItem: () => { throw new Error('denied'); }, setItem: () => { throw new Error('denied'); }, removeItem: () => { throw new Error('denied'); } });
    expect(() => setSite('x')).not.toThrow();
    expect(getSite()).toBeNull();
  });

  it('sends the header only when a site is chosen', () => {
    expect(siteHeader()).toEqual({});
    setSite('s1');
    expect(siteHeader()).toEqual({ 'X-Cmx-Site': 's1' });
  });
});
