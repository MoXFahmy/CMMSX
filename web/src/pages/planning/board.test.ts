import { describe, expect, it } from 'vitest';
import { cellsByAsset, cellsByPerson, days, load, NO_ASSET, UNASSIGNED, type Board, type BoardJob } from './board';

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
    const b = board({ jobs: [job('a', '2026-11-24', [{ employeeId: 'ann' }, { employeeId: 'bob' }]), job('b', '2026-11-25', [])] });
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
    const cells = cellsByPerson(board({ pms: [pm('pm1', ['ann']), pm('pm2', [])] }));
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
