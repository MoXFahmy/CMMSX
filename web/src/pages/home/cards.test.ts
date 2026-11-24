import { describe, expect, it } from 'vitest';
import { canUseCard, CLASSIC_LAYOUT, gridRows, homeChoice, type HomeCard, type HomeCardType } from './cards';

const c = (type: HomeCardType, tiles?: HomeCard['tiles']): HomeCard => ({ type, tiles });
const only = (...perms: string[]) => (p: string) => perms.includes(p);
const types = (rows: HomeCard[][]) => rows.map((r) => r.map((x) => x.type));

describe('home cards', () => {
  it('pairs half cards and keeps full cards alone', () => {
    expect(types(gridRows([c('my-work'), c('late-work'), c('assets-down'), c('work-summary'), c('low-stock')])))
      .toEqual([['my-work', 'late-work'], ['assets-down'], ['work-summary'], ['low-stock']]);
    expect(types(gridRows([]))).toEqual([]);
  });

  it('checks what each card needs', () => {
    expect(canUseCard(c('work-summary'), only('dashboard.view'), {})).toBe(false);
    expect(canUseCard(c('work-summary'), only('dashboard.view', 'workorders.view'), {})).toBe(true);
    expect(canUseCard(c('my-approvals'), only('purchasing.approve'), {})).toBe(true);
    expect(canUseCard(c('my-approvals'), only('purchasing.approveRequests'), {})).toBe(true);
    expect(canUseCard(c('my-approvals'), only('purchasing.view'), {})).toBe(false);
    expect(canUseCard(c('my-work'), only('workorders.view'), { employeeId: null })).toBe(false);
    expect(canUseCard(c('my-work'), only('workorders.view'), { employeeId: 'e1' })).toBe(true);
    expect(canUseCard(c('shortcuts', []), only(), {})).toBe(false);
    expect(canUseCard(c('shortcuts', [{ label: 'Go', color: 'blue', to: '/assets' }]), only(), {})).toBe(true);
  });

  it('homeChoice falls back when no card is usable', () => {
    const cards = [c('my-work'), c('low-stock')];
    expect(homeChoice(cards, only('requests.create', 'dashboard.view'), {})).toEqual({ kind: 'classic' });
    expect(homeChoice(cards, only('requests.create'), {})).toEqual({ kind: 'empty' });
    expect(homeChoice([], only('dashboard.view'), {})).toEqual({ kind: 'classic' });
    expect(homeChoice(cards, only('inventory.view', 'workorders.view'), { employeeId: 'e1' })).toEqual({ kind: 'role', cards });
    expect(homeChoice([c('low-stock'), c('my-work'), c('late-work')], only('workorders.view'), {})).toEqual({ kind: 'role', cards: [c('late-work')] });
  });

  it("the classic layout is today's dashboard in order", () => {
    expect(CLASSIC_LAYOUT).toEqual(['work-summary', 'work-by-site', 'urgent-work', 'asset-summary', 'assets-by-status', 'assets-by-category',
      'assets-by-site', 'warranty-soon', 'recent-assets']);
  });
});
