import { describe, expect, it, vi } from 'vitest';
import { imagesSettled } from './printWait';

type Img = { complete: boolean; onload: (() => void) | null; onerror: (() => void) | null };
const img = (complete = false): Img => ({ complete, onload: null, onerror: null });

describe('imagesSettled', () => {
  it('resolves when every image has loaded or failed', async () => {
    const a = img(), b = img(), done = vi.fn();
    void imagesSettled([a, b], 5000).then(done);
    a.onload!(); await Promise.resolve();
    expect(done).not.toHaveBeenCalled();
    b.onerror!(); await new Promise((r) => setTimeout(r, 0));
    expect(done).toHaveBeenCalled();
  });

  it('does not wait forever for an image that never answers', async () => {
    vi.useFakeTimers();
    const done = vi.fn();
    void imagesSettled([img()], 5000).then(done);
    await vi.advanceTimersByTimeAsync(5000);
    expect(done).toHaveBeenCalled();
    vi.useRealTimers();
  });

  it('ignores images that are already there', async () => {
    await expect(imagesSettled([img(true)], 5000)).resolves.toBeUndefined();
  });
});
