import type { AccessWindow, ContractorLine, PmScheduleType, SlaView, WorkOrderStatus, WorkPriority } from '@/api/types';

/** hours: the person's planned hours, else their share of the estimate; plannedHours: their own planned hours only. */
export type BoardAssignee = { employeeId: string; hours?: number | null; plannedHours?: number | null };
export type BoardJob = {
  id: string; number: string; title: string; priority: WorkPriority; status: WorkOrderStatus; siteId: string;
  assetId?: string | null; assetNumber?: string | null; assetName?: string | null;
  /** The planned start, else the due day (null: no date yet). */
  day?: string | null; plannedStart?: string | null; dueAt?: string | null; sla: SlaView; estimatedHours?: number | null; assignees: BoardAssignee[];
  /** 0.10.0: when the work may be done, whether its day misses every window, and "only while stopped". */
  accessWindows?: AccessWindow[] | null; outsideWindows?: boolean; onlyWhenStopped?: boolean;
  /** 0.19.0: the expected stop of the work template it came from. */
  expectedStopHours?: number | null;
};
/** Time away or extra time that applies to a person on a day (0.19.0). */
export type Slot = { typeId: string; name: string; color?: string | null; kind: 'Away' | 'Extra'; hours?: number | null };
/** A person's day (0.19.0): hours available after time away and extra time, planned hours from all their visible work. */
export type BoardDay = { day: string; available: number; planned: number; slots: Slot[] };
/** hours: working hours on each weekday, Sunday first (0.9.0; older servers send none); days: each day of the range (0.19.0). */
export type BoardPerson = { id: string; name: string; tradeIds?: string[]; hours?: number[]; days?: BoardDay[] };
/** A contractor visit (0.19.0): a contractor line on active work, on its visit day, else the work order's day. */
export type Visit = {
  lineId: string; workOrderId: string; number: string; title: string; siteId: string; companyId: string; companyName: string;
  categoryId?: string | null; categoryName?: string | null; person?: string | null; plannedHours: number; hoursDone: number;
  doneOn?: string | null; plannedOn?: string | null; day?: string | null;
};
export type ContractorBoard = { companies: { id: string; name: string }[]; visits: Visit[]; undated: Visit[] };
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

/** A person's day from the server (0.19.0), if it sent one. */
export function dayOf(data: Board, personId: string, day: string): BoardDay | undefined {
  return data.people.find((p) => p.id === personId)?.days?.find((d) => d.day === day);
}

/** A person's hours available on a day: the server's (after time away and extra time), else their weekly pattern, else the organization's. */
export function capacityOn(data: Board, personId: string, day: string): number {
  const fromServer = dayOf(data, personId, day);
  if (fromServer) return fromServer.available;
  const week = data.people.find((p) => p.id === personId)?.hours;
  return week?.length === 7 ? week[new Date(`${day}T00:00:00Z`).getUTCDay()] : data.capacityHours;
}

/** A person's planned hours on a day, from the server: all their visible work, whatever the board's filters (0.19.0). */
export function plannedOn(data: Board, personId: string, day: string): number {
  return dayOf(data, personId, day)?.planned ?? 0;
}

/** company → day → visits; visits without a day stay out (they are in the tray). */
export function cellsByContractor(visits: Visit[]): Map<string, Map<string, Visit[]>> {
  const cells = new Map<string, Map<string, Visit[]>>();
  for (const v of visits) if (v.day) add(cells, v.companyId, v.day, v);
  return cells;
}

/**
 * What dropping a visit does (0.19.0): another day of its company's row sets its visit day (the tray clears it); another company's
 * row is refused, since the company is changed on the work order. Null when nothing changes.
 */
export function planVisitMove(v: Visit, toCompany: string, toDay: string): { lineId: string; day: string | null } | 'other-company' | 'work-order-day' | null {
  if (toCompany !== v.companyId && toDay !== TRAY) return 'other-company';
  // Its day is the work order's (no visit date of its own): there is nothing for the tray to clear.
  if (toDay === TRAY && !v.plannedOn) return v.day ? 'work-order-day' : null;
  const day = toDay === TRAY ? null : toDay;
  return day === (v.day ?? null) ? null : { lineId: v.lineId, day };
}

/** "Assign to me" (0.19.0): on work nobody is on yet, for someone who may do the work and is an employee. */
export function canAssignToMe(job: BoardJob, row: string, canExecute: boolean, employeeId?: string | null): boolean {
  return row === UNASSIGNED && canExecute && !!employeeId && job.assignees.length === 0;
}

/**
 * The contractor line as "Record the visit" saves it (0.19.0): the whole line, with only the hours done, the date, the person and
 * the notes recorded. A visit with no hours done gets no done date.
 */
export function recordVisitBody(line: ContractorLine, input: { hours: number; doneOn: string | null; person: string | null; notes: string | null }) {
  return {
    supplierId: line.supplierId, categoryId: line.categoryId ?? null, contactId: line.contactId ?? null,
    personName: line.contactId ? null : input.person ?? line.personName ?? null, plannedHours: line.plannedHours, rate: null,
    hoursDone: input.hours, doneOn: input.hours > 0 ? input.doneOn : null, notes: input.notes ?? line.notes ?? null, plannedOn: line.plannedOn ?? null,
  };
}
