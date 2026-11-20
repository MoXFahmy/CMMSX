import type { PmScheduleType, SlaView, WorkOrderStatus, WorkPriority } from '@/api/types';

export type BoardAssignee = { employeeId: string; hours?: number | null };
export type BoardJob = {
  id: string; number: string; title: string; priority: WorkPriority; status: WorkOrderStatus; siteId: string;
  assetId?: string | null; assetNumber?: string | null; assetName?: string | null;
  /** The planned start, else the due day (null: no date yet). */
  day?: string | null; plannedStart?: string | null; dueAt?: string | null; sla: SlaView; estimatedHours?: number | null; assignees: BoardAssignee[];
};
export type BoardPerson = { id: string; name: string; tradeIds?: string[] };
/** An upcoming PM occurrence (not generated yet). */
export type ForecastRow = {
  date: string; id: string; code: string; name: string; assetId?: string | null; assetNumber?: string | null; siteId: string; siteName: string;
  type: PmScheduleType; taskName?: string | null; cost?: number | null; overdue: boolean; isNext: boolean;
  /** The people its work order will go to (the schedule's technicians). */
  assigneeIds?: string[];
};
export type Board = { capacityHours: number; people: BoardPerson[]; jobs: BoardJob[]; earlier: BoardJob[]; unscheduled: BoardJob[]; pms: ForecastRow[] };

/** Row key for jobs nobody is on yet. */
export const UNASSIGNED = 'unassigned';
/** Row key for jobs and PMs without an asset (location work, PM rounds). */
export const NO_ASSET = 'none';

export const isPm = (x: BoardJob | ForecastRow): x is ForecastRow => 'date' in x;

/** Every day from → to (YYYY-MM-DD, both included), without time-zone shifts. */
export function days(from: string, to: string): string[] {
  const out: string[] = [];
  const d = new Date(`${from}T00:00:00Z`);
  const end = new Date(`${to}T00:00:00Z`);
  while (d <= end) { out.push(d.toISOString().slice(0, 10)); d.setUTCDate(d.getUTCDate() + 1); }
  return out;
}

function add<T>(cells: Map<string, Map<string, T[]>>, row: string, day: string, item: T) {
  const r = cells.get(row) ?? new Map<string, T[]>();
  r.set(day, [...(r.get(day) ?? []), item]);
  cells.set(row, r);
}

/** person (or UNASSIGNED) → day → jobs and upcoming PMs; a job with two people is in both rows. */
export function cellsByPerson(data: Board): Map<string, Map<string, (BoardJob | ForecastRow)[]>> {
  const cells = new Map<string, Map<string, (BoardJob | ForecastRow)[]>>();
  for (const j of data.jobs) {
    if (!j.day) continue;
    if (j.assignees.length === 0) add<BoardJob | ForecastRow>(cells, UNASSIGNED, j.day, j);
    for (const a of j.assignees) add<BoardJob | ForecastRow>(cells, a.employeeId, j.day, j);
  }
  for (const p of data.pms) {
    const people = p.assigneeIds ?? [];
    if (people.length === 0) add<BoardJob | ForecastRow>(cells, UNASSIGNED, p.date, p);
    for (const id of people) add<BoardJob | ForecastRow>(cells, id, p.date, p);
  }
  return cells;
}

/** asset (or NO_ASSET) → day → jobs and upcoming PMs. */
export function cellsByAsset(data: Board): Map<string, Map<string, (BoardJob | ForecastRow)[]>> {
  const cells = new Map<string, Map<string, (BoardJob | ForecastRow)[]>>();
  for (const j of data.jobs) if (j.day) add<BoardJob | ForecastRow>(cells, j.assetId ?? NO_ASSET, j.day, j);
  for (const p of data.pms) add<BoardJob | ForecastRow>(cells, p.assetId ?? NO_ASSET, p.date, p);
  return cells;
}

/** person → day → planned hours (jobs on the board only; the trays count in nobody's day). */
export function load(data: Board): Map<string, Map<string, number>> {
  const hours = new Map<string, Map<string, number>>();
  for (const j of data.jobs) {
    if (!j.day) continue;
    for (const a of j.assignees) {
      if (a.hours == null) continue;
      const r = hours.get(a.employeeId) ?? new Map<string, number>();
      r.set(j.day, (r.get(j.day) ?? 0) + a.hours);
      hours.set(a.employeeId, r);
    }
  }
  return hours;
}
