import type { TFunction } from 'i18next';
import type { ImportJob, ImportResult } from '@/api/types';

/** Tells one chosen file from another (a check is only good for the file it read). */
export const fileKey = (f: File): string => `${f.name}|${f.size}|${f.lastModified}`;

/** The check this page sent, for which file. */
export type Checked = { jobId: string; key: string };

/**
 * Import is offered only under the finished check this page sent for the file still chosen, when it found something to add
 * or change (never under another check opened from the history, nor before the check is done).
 */
export function canImport(job: ImportJob | null | undefined, checked: Checked | null, file: File | null): boolean {
  const result = job?.result;
  if (!job || !result || !checked || !file || job.id !== checked.jobId || job.save || job.status !== 'Done') return false;
  if (result.saved || result.stopped || checked.key !== fileKey(file)) return false;
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

/** Whether any job in the history is waiting or running: the history then keeps itself up to date (0.26.1). */
export const anyActive = (jobs: Pick<ImportJob, 'status'>[] | null | undefined): boolean =>
  (jobs ?? []).some((j) => j.status === 'Queued' || j.status === 'Running');

/**
 * Every screen is refreshed only for an import this page watched finish (0.26.1); opening an import that had already
 * finished from the history changes nothing.
 */
export const refreshesAll = (job: Pick<ImportJob, 'id' | 'save' | 'status'> | null | undefined, watched: string | null): boolean =>
  !!job && job.save && job.status === 'Done' && job.id === watched;
