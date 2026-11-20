import type { PmScheduleType, SlaView, WorkOrderStatus, WorkPriority } from '@/api/types';

/** hours: the person's planned hours, else their share of the estimate; plannedHours: their own planned hours only. */
export type BoardAssignee = { employeeId: string; hours?: number | null; plannedHours?: number | null };
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
/** Where a card dragged from a tray (earlier days, no date) comes from. */
export const TRAY = 'tray';

export type Move = { workOrderId: string; day?: string; fromEmployeeId?: string; toEmployeeId?: string; plannedHours?: number | null };

/**
 * What dropping a card does, and how to undo it. By person: a new day re-plans it; another row hands that person's part
 * over (from a tray: a one-person job goes to the new person, a job with several keeps them); Nobody yet takes the person
 * off. By asset: only the day. Null when nothing changes.
 */
export function planMove(job: BoardJob, sourceRow: string, targetRow: string, targetDay: string, view: 'person' | 'asset'): { move: Move; undo: Move } | null {
  const move: Move = { workOrderId: job.id };
  const undo: Move = { workOrderId: job.id };
  if (targetDay !== job.day) {
    move.day = targetDay;
    const before = job.plannedStart ?? job.day;
    if (before) undo.day = before;
  }
  if (view === 'person' && targetRow !== sourceRow) {
    const people = job.assignees.map((a) => a.employeeId);
    const fromTray = sourceRow === TRAY;
    const from = fromTray ? (people.length === 1 ? people[0] : undefined) : sourceRow !== UNASSIGNED ? sourceRow : undefined;
    const to = targetRow !== UNASSIGNED && (!fromTray || people.length <= 1) ? targetRow : undefined;
    if (from === undefined || from !== to) {
      if (from) {
        move.fromEmployeeId = from;
        undo.toEmployeeId = from;
        const hours = job.assignees.find((a) => a.employeeId === from)?.plannedHours;
        if (hours != null) undo.plannedHours = hours; // putting the person back puts back their planned hours
      }
      if (to && !people.includes(to)) { move.toEmployeeId = to; undo.fromEmployeeId = to; }
    }
  }
  return move.day || move.fromEmployeeId || move.toEmployeeId ? { move, undo } : null;
}

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

/**
 * person (or UNASSIGNED) → day → jobs and upcoming PMs; a job with two people is in both rows. A job whose people aren't
 * on the board (left, another site or trade) is under Nobody yet, so it can't disappear.
 */
export function cellsByPerson(data: Board): Map<string, Map<string, (BoardJob | ForecastRow)[]>> {
  const shown = new Set(data.people.map((p) => p.id));
  const cells = new Map<string, Map<string, (BoardJob | ForecastRow)[]>>();
  const place = (people: string[], day: string, item: BoardJob | ForecastRow) => {
    const rows = people.filter((id) => shown.has(id));
    for (const row of rows.length > 0 ? rows : [UNASSIGNED]) add<BoardJob | ForecastRow>(cells, row, day, item);
  };
  for (const j of data.jobs) if (j.day) place(j.assignees.map((a) => a.employeeId), j.day, j);
  for (const p of data.pms) place(p.assigneeIds ?? [], p.date, p);
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
