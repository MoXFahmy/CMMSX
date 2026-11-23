import type { WorkOrderStatus } from '@/api/types';

/** A work order on a person's day (0.19.0) with their labour entries for that day. */
export type TimesheetRow = {
  workOrderId: string; number: string; title: string; status: WorkOrderStatus; closed: boolean;
  entries: { id: string; hours: number; overtimeHours: number; notes?: string | null }[];
};
export type TimesheetDay = {
  employee: { id: string; name: string }; date: string;
  available?: { pattern: number; available: number; slots: { name: string; color?: string | null; kind: 'Away' | 'Extra'; hours?: number | null }[] } | null;
  rows: TimesheetRow[]; hours: number; overtimeHours: number;
};
export type WeekCell = { day?: string; hours: number; overtimeHours: number; available: number };
export type TimesheetWeek = { days: string[]; people: { id: string; name: string; days: WeekCell[] }[] };

const round = (n: number) => Math.round(n * 100) / 100;

/** The day's hours and overtime across its work orders. */
export function dayTotals(rows: TimesheetRow[]): { hours: number; overtimeHours: number } {
  const all = rows.flatMap((r) => r.entries);
  return { hours: round(all.reduce((a, e) => a + e.hours, 0)), overtimeHours: round(all.reduce((a, e) => a + e.overtimeHours, 0)) };
}

/** A week cell's state: more logged than available, less than available on a working day, or as expected. */
export function weekCell(c: WeekCell): 'over' | 'under' | 'ok' {
  const logged = c.hours + c.overtimeHours;
  if (logged > c.available + 0.001) return 'over';
  if (c.available > 0 && logged < c.available - 0.001) return 'under';
  return 'ok';
}
