import type { TFunction } from 'i18next';

/** A pair of codes recorded together on completed work (0.14.0): a symptom and a cause, or a cause and a remedy. */
export type CodePair = { first: string; second: string; count: number };
export type CodePairs = { symptomCause: CodePair[]; causeRemedy: CodePair[] };

const byCode = (a: { code: string }, b: { code: string }) => a.code.localeCompare(b.code, undefined, { numeric: true });

/**
 * Smart ordering (0.14.0): the codes most often recorded with `first` (the chosen symptom, or cause), most used first; then the
 * others by code. With nothing chosen, everything by code. The caller decides which codes are offered (active ones, and the current one).
 */
export function orderCodes<T extends { id: string; code: string }>(codes: T[], pairs: CodePair[], first?: string | null): { top: T[]; rest: T[] } {
  if (!first) return { top: [], rest: [...codes].sort(byCode) };
  const counts = new Map(pairs.filter((p) => p.first === first).map((p) => [p.second, p.count]));
  return {
    top: codes.filter((c) => counts.has(c.id)).sort((a, b) => counts.get(b.id)! - counts.get(a.id)! || byCode(a, b)),
    rest: codes.filter((c) => !counts.has(c.id)).sort(byCode),
  };
}

/** Picker data: "Most used" then "All" groups when there are paired codes, a plain list otherwise. */
export function codeOptions(codes: { id: string; code: string; name: string }[], pairs: CodePair[], first: string | null | undefined, t: TFunction) {
  const item = (c: { id: string; code: string; name: string }) => ({ value: c.id, label: `${c.code} · ${c.name}` });
  const { top, rest } = orderCodes(codes, pairs, first);
  if (top.length === 0) return rest.map(item);
  return [{ group: t('codes.mostUsed'), items: top.map(item) }, ...(rest.length > 0 ? [{ group: t('codes.all'), items: rest.map(item) }] : [])];
}
