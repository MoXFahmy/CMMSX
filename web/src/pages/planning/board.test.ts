import { describe, expect, it } from 'vitest';
import { cellsByAsset, cellsByPerson, days, load, NO_ASSET, planMove, TRAY, UNASSIGNED, type Board, type BoardJob } from './board';

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

  it('adds up each person\'s hours per day, leaving the trays out', () => {
    const b = board({
      jobs: [job('a', '2026-11-24', [{ employeeId: 'ann', hours: 3 }, { employeeId: 'bob', hours: 2 }]), job('b', '2026-11-24', [{ employeeId: 'ann', hours: 6 }])],
      earlier: [job('c', '2026-11-01', [{ employeeId: 'ann', hours: 5 }])],
      unscheduled: [job('d', null, [{ employeeId: 'ann', hours: 4 }])],
    });
    const hours = load(b);
    expect(hours.get('ann')?.get('2026-11-24')).toBe(9);
    expect(hours.get('bob')?.get('2026-11-24')).toBe(2);
    expect(hours.get('ann')?.get('2026-11-01')).toBeUndefined();
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

