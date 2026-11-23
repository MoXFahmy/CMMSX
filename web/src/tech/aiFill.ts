import type { AiReportResult } from '@/components/aiReport';
import type { TechBundle, TechJob } from './types';

/** "Fill with AI" shows only when the server says AI is on for this person (a 0.14 server says nothing, so it stays hidden). */
export const aiEnabled = (b?: TechBundle | null) => b?.ai?.enabled === true;

/** The store a suggested part would come from: its planned line's store, else the job site's default store. */
export function partStore(job: TechJob, partId: string): string | null {
  return job.plannedLines?.find((l) => l.partId === partId && l.storeId)?.storeId ?? job.defaultStoreId ?? null;
}

/** A part already issued to the job (on its planned line or as an extra part): ticking it again would issue it twice. */
const alreadyUsed = (job: TechJob, partId: string) =>
  (job.plannedLines ?? []).some((l) => l.partId === partId && l.issued > 0) || (job.usedParts ?? []).some((u) => u.partId === partId && u.issued > 0);

/**
 * What starts ticked after Fill with AI: hours only when I have none logged on the job yet, and parts only when they can be
 * issued (stock rights, a store) and weren't issued already — so a tap on Complete never records the same work twice.
 */
export function defaultTicks(job: TechJob, r: AiReportResult, canIssue: boolean): { hours: boolean; parts: string[] } {
  return {
    hours: !!r.hours && (job.myLabor ?? []).length === 0,
    parts: canIssue ? r.parts.filter((p) => partStore(job, p.partId) && !alreadyUsed(job, p.partId)).map((p) => p.partId) : [],
  };
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
