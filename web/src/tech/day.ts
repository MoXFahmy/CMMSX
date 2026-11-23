import type { QueuedChange } from './outbox';
import type { TechBundle } from './types';

export type DayRow = { workOrderId: string; number: string; title: string; hours: number; overtimeHours: number; readOnly: boolean };

const round = (n: number) => Math.round(n * 100) / 100;

/**
 * My day on the phone (0.19.0): my jobs dated that day or carrying my hours that day, jobs no longer on the phone (completed since) from
 * the bundle's last 30 days of my hours (read-only), and my hours waiting to be sent for that day. The total is against the hours the
 * server says I have that day (null without its figure).
 */
export function dayRows(bundle: TechBundle, date: string, pending: QueuedChange[]): {
  rows: DayRow[]; hours: number; overtimeHours: number; available: number | null;
} {
  const rows = new Map<string, DayRow>();
  for (const j of bundle.jobs) {
    const mine = j.myLabor.filter((l) => l.workDate === date);
    if ((j.plannedStart ?? j.dueDate) !== date && mine.length === 0) continue;
    rows.set(j.id, { workOrderId: j.id, number: j.number, title: j.title, readOnly: false,
      hours: mine.reduce((a, l) => a + l.hours, 0), overtimeHours: mine.reduce((a, l) => a + l.overtimeHours, 0) });
  }
  for (const l of bundle.myDay?.labour ?? []) {
    if (l.workDate !== date || bundle.jobs.some((j) => j.id === l.workOrderId)) continue;
    const r = rows.get(l.workOrderId) ?? { workOrderId: l.workOrderId, number: l.number, title: l.title, hours: 0, overtimeHours: 0, readOnly: true };
    rows.set(l.workOrderId, { ...r, hours: r.hours + l.hours, overtimeHours: r.overtimeHours + l.overtimeHours });
  }
  for (const c of pending) {
    if (c.type !== 'labor.add' || !c.workOrderId) continue;
    const p = c.payload as { workDate?: string; hours?: number; overtimeHours?: number; employeeId?: string };
    if (p.workDate !== date || (p.employeeId && p.employeeId !== bundle.employeeId)) continue;
    const job = bundle.jobs.find((j) => j.id === c.workOrderId);
    const r = rows.get(c.workOrderId) ?? (job ? { workOrderId: job.id, number: job.number, title: job.title, hours: 0, overtimeHours: 0, readOnly: false } : undefined);
    if (r) rows.set(c.workOrderId, { ...r, hours: r.hours + Number(p.hours ?? 0), overtimeHours: r.overtimeHours + Number(p.overtimeHours ?? 0) });
  }
  const list = [...rows.values()].map((r) => ({ ...r, hours: round(r.hours), overtimeHours: round(r.overtimeHours) }));
  const available = bundle.myDay?.available.find((d) => d.day === date)?.hours ?? null;
  return {
    rows: list, hours: round(list.reduce((a, r) => a + r.hours, 0)), overtimeHours: round(list.reduce((a, r) => a + r.overtimeHours, 0)), available,
  };
}
