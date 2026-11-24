import type { TFunction } from 'i18next';
import type { ImportJob, ImportResult } from '@/api/types';

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

/** "Checking Assets… 1,240 of 3,000 rows" while a background job runs (0.26.0); empty once it has finished. */
export function jobLabel(job: Pick<ImportJob, 'status' | 'save' | 'sheet' | 'rowsDone' | 'rowsTotal' | 'waitingBehind'>, t: TFunction): string {
  if (job.status === 'Queued') return job.waitingBehind > 0 ? t('import.waiting') : t('import.starting');
  if (job.status !== 'Running') return '';
  const n = (v: number) => v.toLocaleString('en-US');
  const sheet = job.sheet ? t(`import.sheets.${job.sheet}`) : '';
  return t(job.save ? 'import.importing' : 'import.checking', { sheet, done: n(job.rowsDone), total: n(job.rowsTotal) });
}
