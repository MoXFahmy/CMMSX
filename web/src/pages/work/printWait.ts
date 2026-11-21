type Loadable = Pick<HTMLImageElement, 'complete' | 'onload' | 'onerror'>;

/** Resolves when every image still loading has loaded or failed, or after `ms` at most (a stalled image never answers). */
export function imagesSettled(images: Loadable[], ms: number): Promise<void> {
  const pending = images.filter((i) => !i.complete);
  if (pending.length === 0) return Promise.resolve();
  const each = pending.map((i) => new Promise<void>((r) => { i.onload = i.onerror = () => r(); }));
  return Promise.race([Promise.all(each).then(() => undefined), new Promise<void>((r) => setTimeout(r, ms))]);
}
