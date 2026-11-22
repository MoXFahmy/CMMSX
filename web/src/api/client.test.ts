import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, get, getAllSites, post } from './client';

afterEach(() => { vi.unstubAllGlobals(); });

describe('api client', () => {
  it('says there is no connection when a request never reaches the server (no signal)', async () => {
    vi.stubGlobal('fetch', async () => { throw new TypeError('Failed to fetch'); });
    const e = await post('/work-orders/w1/comments', { body: 'Hi' }).catch((x: unknown) => x);
    expect(e).toBeInstanceOf(ApiError);
    expect((e as ApiError).status).toBe(0);
    expect((e as ApiError).message).toMatch(/no connection/i);
    expect((e as ApiError).message).not.toMatch(/failed to fetch/i);
  });

  it('still reports the server\'s own answer when there is one', async () => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ title: 'Choose a site.' }), { status: 400 }));
    const e = await post('/work-orders', {}).catch((x: unknown) => x);
    expect((e as ApiError).message).toBe('Choose a site.');
  });

  it('sends the site switch, except on calls that ask for every site (0.9.0)', async () => {
    const store = new Map<string, string>([['cmx.site', 'cairo']]);
    vi.stubGlobal('localStorage', { getItem: (k: string) => store.get(k) ?? null, setItem: () => {}, removeItem: () => {} });
    const sent: (string | null)[] = [];
    vi.stubGlobal('fetch', async (_url: string, init: RequestInit) => { sent.push(new Headers(init.headers).get('X-Cmx-Site')); return new Response('[]', { status: 200 }); });
    await get('/sites');
    await getAllSites('/locations/hierarchy');
    expect(sent).toEqual(['cairo', 'all']);
  });
});
