import type { ImportResult } from '@/api/types';

/** Tells one chosen file from another (a check is only good for the file it read). */
export const fileKey = (f: File): string => `${f.name}|${f.size}|${f.lastModified}`;

/** Import is offered only after a check of this same file found something to add or change, and not yet saved. */
export function canImport(result: ImportResult | null, checkedKey: string | null, file: File | null): boolean {
  if (!result || !file || result.saved || result.stopped || checkedKey !== fileKey(file)) return false;
  const t = totals(result);
  return t.new + t.changed > 0;
}

export function totals(result: ImportResult): { new: number; changed: number; unchanged: number; problems: number } {
  return result.sheets.reduce((a, s) => ({ new: a.new + s.new, changed: a.changed + s.changed, unchanged: a.unchanged + s.unchanged, problems: a.problems + s.problems }),
    { new: 0, changed: 0, unchanged: 0, problems: 0 });
}
