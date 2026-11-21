import { describe, expect, it } from 'vitest';
import en from './en.json';
import ar from './ar.json';

type Tree = { [k: string]: string | Tree };
const PLURAL = /_(zero|one|two|few|many|other)$/;

/** key path → text, with plural forms grouped under their base key. */
function flatten(t: Tree, prefix = ''): Map<string, string[]> {
  const out = new Map<string, string[]>();
  for (const [k, v] of Object.entries(t)) {
    if (typeof v !== 'string') { for (const [kk, vv] of flatten(v, `${prefix}${k}.`)) out.set(kk, vv); continue; }
    const key = prefix + k.replace(PLURAL, '');
    out.set(key, [...(out.get(key) ?? []), v]);
  }
  return out;
}
const names = (texts: string[]) => new Set(texts.flatMap((s) => [...s.matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1])));

describe('Arabic wording', () => {
  const english = flatten(en as Tree);
  const arabic = flatten(ar as Tree);

  it('has every English phrase', () => {
    expect([...english.keys()].filter((k) => !arabic.has(k))).toEqual([]);
  });

  it('keeps the values each phrase shows (names, numbers, dates)', () => {
    const wrong = [...english].filter(([k, texts]) => {
      const want = names(texts); want.delete('count'); // Arabic plural forms may spell out one and two
      const got = names(arabic.get(k) ?? []); got.delete('count');
      return [...want].some((n) => !got.has(n)) || [...got].some((n) => !want.has(n));
    }).map(([k]) => k);
    expect(wrong).toEqual([]);
  });
});
