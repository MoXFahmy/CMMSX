import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, post } from './client';

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
});
