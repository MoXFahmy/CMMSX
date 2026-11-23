import type { AiReportResult } from '@/components/aiReport';
import type { TechBundle, TechJob } from './types';

/** "Fill with AI" shows only when the server says AI is on for this person (a 0.14 server says nothing, so it stays hidden). */
export const aiEnabled = (b?: TechBundle | null) => b?.ai?.enabled === true;

/** The store a suggested part would come from: its planned line's store, else the job site's default store. */
export function partStore(job: TechJob, partId: string): string | null {
  return job.plannedLines?.find((l) => l.partId === partId && l.storeId)?.storeId ?? job.defaultStoreId ?? null;
}

/**
 * The usual hours and part changes for what the technician kept ticked, queued before the completion so stock rules, offline
 * sending and conflicts work as for any other change. Parts need stock rights and a store; nothing unticked is sent.
 */
export function aiChanges(job: TechJob, r: AiReportResult, ticks: { hours: boolean; parts: string[] }, canIssue: boolean, today: string):
  { type: 'labor.add' | 'part.use'; payload: object }[] {
  const out: { type: 'labor.add' | 'part.use'; payload: object }[] = [];
  if (ticks.hours && r.hours) out.push({ type: 'labor.add', payload: { workDate: today, hours: r.hours, overtimeHours: 0, notes: null } });
  if (!canIssue) return out;
  for (const p of r.parts.filter((x) => ticks.parts.includes(x.partId))) {
    const storeId = partStore(job, p.partId);
    if (!storeId) continue;
    const line = job.plannedLines?.find((l) => l.partId === p.partId && (l.storeId ?? job.defaultStoreId) === storeId);
    out.push({ type: 'part.use', payload: { partId: p.partId, storeId, quantity: p.quantity, ...(line ? { lineId: line.id } : {}) } });
  }
  return out;
}
