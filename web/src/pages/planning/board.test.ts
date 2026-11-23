import { describe, expect, it } from 'vitest';
import { capacityOn, cellsByAsset, canAssignToMe, cellsByContractor, cellsByPerson, dayOf, days, NO_ASSET, plannedOn, planMove, planVisitMove, recordVisitBody, TRAY, UNASSIGNED, type Board, type BoardJob, type Visit } from './board';

const sla = { response: 'None', completion: 'OnTrack' } as const;
const job = (id: string, day: string | null, assignees: { employeeId: string; hours?: number | null }[], assetId?: string): BoardJob => ({
  id, number: id.toUpperCase(), title: id, priority: 'Medium', status: 'Open', siteId: 's', assetId, day, sla, assignees,
});
const board = (over: Partial<Board> = {}): Board => ({ capacityHours: 8, people: [], jobs: [], earlier: [], unscheduled: [], pms: [], ...over });

describe('planning board layout', () => {
  it('lists the days of the range', () => {
    expect(days('2026-11-23', '2026-11-29')).toEqual(['2026-11-23', '2026-11-24', '2026-11-25', '2026-11-26', '2026-11-27', '2026-11-28', '2026-11-29']);
    expect(days('2026-12-31', '2027-01-01')).toEqual(['2026-12-31', '2027-01-01']);
  });

  it('puts a job in the row of each person on it, on its day; nobody on it → Unassigned', () => {
    const b = board({ people: [{ id: 'ann', name: 'Ann' }, { id: 'bob', name: 'Bob' }], jobs: [job('a', '2026-11-24', [{ employeeId: 'ann' }, { employeeId: 'bob' }]), job('b', '2026-11-25', [])] });
    const cells = cellsByPerson(b);
    expect(cells.get('ann')?.get('2026-11-24')?.map((j) => j.id)).toEqual(['a']);
    expect(cells.get('bob')?.get('2026-11-24')?.map((j) => j.id)).toEqual(['a']);
    expect(cells.get(UNASSIGNED)?.get('2026-11-25')?.map((j) => j.id)).toEqual(['b']);
  });

  it('reads each person\'s planned hours per day from the server (0.19.0: all their visible work, whatever the filters)', () => {
    const b = board({
      people: [{ id: 'ann', name: 'Ann', days: [{ day: '2026-11-24', available: 8, planned: 9, slots: [] }] }],
      jobs: [job('a', '2026-11-24', [{ employeeId: 'ann', hours: 3 }])],
    });
    expect(plannedOn(b, 'ann', '2026-11-24')).toBe(9);
    expect(plannedOn(b, 'ann', '2026-11-25')).toBe(0);
  });

  it('puts upcoming PMs in the rows of the people they will go to, else Unassigned', () => {
    const pm = (id: string, assigneeIds: string[]) => ({ date: '2026-11-26', id, code: id, name: id, siteId: 's', siteName: 'Plant', type: 'FixedInterval' as const, overdue: false, isNext: true, assigneeIds });
    const cells = cellsByPerson(board({ people: [{ id: 'ann', name: 'Ann' }], pms: [pm('pm1', ['ann']), pm('pm2', [])] }));
    expect(cells.get('ann')?.get('2026-11-26')?.length).toBe(1);
    expect(cells.get(UNASSIGNED)?.get('2026-11-26')?.length).toBe(1);
  });

  it('puts jobs and upcoming PMs in their asset\'s row; no asset → its own row', () => {
    const b = board({
      jobs: [job('a', '2026-11-24', [], 'pump'), job('b', '2026-11-24', [])],
      pms: [{ date: '2026-11-26', id: 'pm1', code: 'PM-1', name: 'Pump PM', assetId: 'pump', siteId: 's', siteName: 'Plant', type: 'FixedInterval', overdue: false, isNext: true }],
    });
    const cells = cellsByAsset(b);
    expect(cells.get('pump')?.get('2026-11-24')?.length).toBe(1);
    expect(cells.get('pump')?.get('2026-11-26')?.length).toBe(1);
    expect(cells.get(NO_ASSET)?.get('2026-11-24')?.length).toBe(1);
  });
});

describe('jobs on people the board does not show', () => {
  it('go to Nobody yet instead of disappearing (inactive, other site, other trade)', () => {
    const b = board({ people: [{ id: 'ann', name: 'Ann' }], jobs: [job('a', '2026-11-24', [{ employeeId: 'gone' }]), job('b', '2026-11-24', [{ employeeId: 'ann' }, { employeeId: 'gone' }])],
      pms: [{ date: '2026-11-25', id: 'pm1', code: 'PM-1', name: 'PM', siteId: 's', siteName: 'Plant', type: 'FixedInterval', overdue: false, isNext: true, assigneeIds: ['gone'] }] });
    const cells = cellsByPerson(b);
    expect(cells.get(UNASSIGNED)?.get('2026-11-24')?.map((j) => (j as BoardJob).id)).toEqual(['a']);
    expect(cells.get('ann')?.get('2026-11-24')?.map((j) => (j as BoardJob).id)).toEqual(['b']);
    expect(cells.get(UNASSIGNED)?.get('2026-11-25')?.length).toBe(1);
  });
});

describe('what a drop does', () => {
  const lee = job('j', '2026-11-16', [{ employeeId: 'lee' }]);
  const pair = job('p', '2026-11-16', [{ employeeId: 'lee' }, { employeeId: 'sam' }]);

  it('from a tray onto the same person only re-plans the day', () => {
    expect(planMove(lee, TRAY, 'lee', '2026-11-24', 'person')?.move).toEqual({ workOrderId: 'j', day: '2026-11-24' });
  });
  it('from a tray onto someone else hands a one-person job over', () => {
    const p = planMove(lee, TRAY, 'sam', '2026-11-24', 'person');
    expect(p?.move).toEqual({ workOrderId: 'j', day: '2026-11-24', fromEmployeeId: 'lee', toEmployeeId: 'sam' });
    expect(p?.undo).toEqual({ workOrderId: 'j', day: '2026-11-16', fromEmployeeId: 'sam', toEmployeeId: 'lee' });
  });
  it('from a tray, a job with several people keeps them', () => {
    expect(planMove(pair, TRAY, 'ann', '2026-11-24', 'person')?.move).toEqual({ workOrderId: 'p', day: '2026-11-24' });
  });
  it('between rows: hand over, take off, give to someone', () => {
    const onBoard = job('k', '2026-11-24', [{ employeeId: 'lee' }]);
    expect(planMove(onBoard, 'lee', 'sam', '2026-11-24', 'person')?.move).toEqual({ workOrderId: 'k', fromEmployeeId: 'lee', toEmployeeId: 'sam' });
    expect(planMove(onBoard, 'lee', UNASSIGNED, '2026-11-24', 'person')?.move).toEqual({ workOrderId: 'k', fromEmployeeId: 'lee' });
    const nobody = job('n', '2026-11-24', []);
    expect(planMove(nobody, UNASSIGNED, 'sam', '2026-11-24', 'person')?.move).toEqual({ workOrderId: 'n', toEmployeeId: 'sam' });
  });
  it('onto a row whose person is already on the job: that person keeps it alone', () => {
    const both = job('b', '2026-11-24', [{ employeeId: 'lee' }, { employeeId: 'sam' }]);
    expect(planMove(both, 'lee', 'sam', '2026-11-24', 'person')?.move).toEqual({ workOrderId: 'b', fromEmployeeId: 'lee' });
  });
  it('the asset view only moves days; a drop where it started does nothing', () => {
    expect(planMove(lee, 'pump', 'compressor', '2026-11-25', 'asset')?.move).toEqual({ workOrderId: 'j', day: '2026-11-25' });
    expect(planMove(job('s', '2026-11-24', [{ employeeId: 'lee' }]), 'lee', 'lee', '2026-11-24', 'person')).toBeNull();
  });
});

describe('undo puts back what was there', () => {
  it('a person taken off gets their planned hours back', () => {
    const j: BoardJob = { ...job('h', '2026-11-24', []), assignees: [{ employeeId: 'lee', hours: 4, plannedHours: 4 }] };
    expect(planMove(j, 'lee', UNASSIGNED, '2026-11-24', 'person')?.undo).toEqual({ workOrderId: 'h', toEmployeeId: 'lee', plannedHours: 4 });
    expect(planMove(j, 'lee', 'sam', '2026-11-24', 'person')?.undo).toEqual({ workOrderId: 'h', fromEmployeeId: 'sam', toEmployeeId: 'lee', plannedHours: 4 });
  });
});

describe('each person\'s capacity (0.9.0)', () => {
  it('comes from their working hours for that weekday', () => {
    const hours = [8, 8, 8, 8, 8, 0, 0]; // Sunday to Thursday
    const b = board({ people: [{ id: 'ann', name: 'Ann', hours }, { id: 'old', name: 'Old app' }] });
    expect(capacityOn(b, 'ann', '2026-11-27')).toBe(0); // a Friday: any planned hours are over
    expect(capacityOn(b, 'ann', '2026-11-22')).toBe(8); // a Sunday
    expect(capacityOn(b, 'old', '2026-11-27')).toBe(8); // no hours from the server: the organization's hours per day
  });
});

describe('availability on the board (0.19.0)', () => {
  const holiday = { typeId: 't', name: 'Holiday', color: '#40c057', kind: 'Away' as const, hours: null };
  it('reads the server\'s day: available hours after time away', () => {
    const b = board({ people: [{ id: 'ann', name: 'Ann', hours: [8, 8, 8, 8, 8, 0, 0], days: [{ day: '2026-11-22', available: 0, planned: 2, slots: [holiday] }] }] });
    expect(dayOf(b, 'ann', '2026-11-22')?.slots[0].name).toBe('Holiday');
    expect(capacityOn(b, 'ann', '2026-11-22')).toBe(0); // the pattern says 8; the holiday wins
    expect(dayOf(b, 'ann', '2026-11-23')).toBeUndefined();
  });
});

describe('contractor visits (0.19.0)', () => {
  const visit = (lineId: string, companyId: string, day: string | null): Visit => ({
    lineId, workOrderId: `wo-${lineId}`, number: 'WO-1', title: 'Lift', siteId: 's', companyId, companyName: companyId, plannedHours: 4, hoursDone: 0, day,
  });
  it('groups visits by company, then day', () => {
    const cells = cellsByContractor([visit('a', 'lift', '2026-11-24'), visit('b', 'lift', '2026-11-24'), visit('c', 'fire', '2026-11-25'), visit('d', 'fire', null)]);
    expect(cells.get('lift')?.get('2026-11-24')?.map((v) => v.lineId)).toEqual(['a', 'b']);
    expect(cells.get('fire')?.get('2026-11-25')?.map((v) => v.lineId)).toEqual(['c']);
    expect([...(cells.get('fire')?.keys() ?? [])]).toEqual(['2026-11-25']);
  });
  it('moves a visit to another day of its company, never to another company', () => {
    const v = visit('a', 'lift', '2026-11-24');
    expect(planVisitMove(v, 'lift', '2026-11-26')).toEqual({ lineId: 'a', day: '2026-11-26' });
    expect(planVisitMove(v, 'fire', '2026-11-26')).toBe('other-company');
    expect(planVisitMove(v, 'lift', '2026-11-24')).toBeNull();
    expect(planVisitMove({ ...v, plannedOn: '2026-11-24' }, 'lift', TRAY)).toEqual({ lineId: 'a', day: null });
    // Its day is the work order's: there is no visit date to take away, so the tray says why instead of "moved".
    expect(planVisitMove(v, 'lift', TRAY)).toBe('work-order-day');
  });
});

describe('Assign to me (0.19.0)', () => {
  it('shows only on unassigned work for someone who can do it and is an employee', () => {
    const j = job('a', '2026-11-24', []);
    expect(canAssignToMe(j, UNASSIGNED, true, 'emp')).toBe(true);
    expect(canAssignToMe(j, UNASSIGNED, true, null)).toBe(false); // an account not linked to an employee
    expect(canAssignToMe(j, UNASSIGNED, false, 'emp')).toBe(false);
    expect(canAssignToMe(j, 'ann', true, 'emp')).toBe(false);
    expect(canAssignToMe(job('b', '2026-11-24', [{ employeeId: 'ann' }]), UNASSIGNED, true, 'emp')).toBe(false);
  });
});

describe('recording a contractor visit (0.19.0)', () => {
  const line = { id: 'l', supplierId: 'c', companyName: 'Lift', categoryId: 'k', contactId: null, personName: 'Omar', plannedHours: 4, hoursDone: 0,
    doneOn: null, rate: 150, cost: 0, plannedCost: 600, notes: 'Bring the hoist', plannedOn: '2026-11-26' };
  it('never stamps a done date on a visit with no hours done', () => {
    expect(recordVisitBody(line, { hours: 0, doneOn: '2026-11-23', person: null, notes: 'Changed' }).doneOn).toBeNull();
    expect(recordVisitBody({ ...line, doneOn: '2026-11-20', hoursDone: 2 }, { hours: 0, doneOn: null, person: null, notes: null }).doneOn).toBeNull();
  });
  it('sends the whole line with only what was recorded changed', () => {
    expect(recordVisitBody(line, { hours: 3, doneOn: '2026-11-23', person: 'Sami', notes: null })).toEqual({
      supplierId: 'c', categoryId: 'k', contactId: null, personName: 'Sami', plannedHours: 4, rate: null, hoursDone: 3, doneOn: '2026-11-23',
      notes: 'Bring the hoist', plannedOn: '2026-11-26',
    });
  });
});
