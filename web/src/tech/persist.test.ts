import { afterEach, describe, expect, it, vi } from 'vitest';
import { keepData } from './persist';

afterEach(() => vi.unstubAllGlobals());

describe('keepData', () => {
  it('asks the browser once to keep the saved jobs and changes', async () => {
    const persist = vi.fn(async () => true);
    vi.stubGlobal('navigator', { storage: { persisted: async () => false, persist } });
    expect(await keepData()).toBe(true);
    expect(persist).toHaveBeenCalledTimes(1);
  });

  it('does not ask again when the data is already kept', async () => {
    const persist = vi.fn(async () => true);
    vi.stubGlobal('navigator', { storage: { persisted: async () => true, persist } });
    expect(await keepData()).toBe(true);
    expect(persist).not.toHaveBeenCalled();
  });

  it('copes with a browser that cannot', async () => {
    vi.stubGlobal('navigator', {});
    expect(await keepData()).toBe(false);
  });
});
