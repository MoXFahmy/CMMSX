import { describe, expect, it } from 'vitest';
import { CLASSIC_LAYOUT } from '@/pages/home/cards';
import { addableCards, classicCards, isSafeLink, missingFor, moveCard } from './roleHome';

describe('role home editor', () => {
  it('offers only cards not on the page yet', () => {
    const left = addableCards([{ type: 'my-work' }, { type: 'shortcuts', tiles: [] }]);
    expect(left).not.toContain('my-work');
    expect(left).not.toContain('shortcuts');
    expect(left[0]).toBe('work-summary');
    expect(left).toHaveLength(16);
  });

  it('names what a role is missing for a card', () => {
    expect(missingFor('work-summary', ['dashboard.view'])).toEqual(['workorders.view']);
    expect(missingFor('work-summary', ['dashboard.view', 'workorders.view'])).toBeNull();
    expect(missingFor('my-approvals', ['purchasing.approveRequests'])).toBeNull();
    expect(missingFor('my-approvals', [])).toEqual(['purchasing.approve', 'purchasing.approveRequests']);
    expect(missingFor('shortcuts', [])).toBeNull();
  });

  it('moves cards and stops at the ends', () => {
    const cards = [{ type: 'my-work' as const }, { type: 'late-work' as const }, { type: 'low-stock' as const }];
    expect(moveCard(cards, 1, -1).map((c) => c.type)).toEqual(['late-work', 'my-work', 'low-stock']);
    expect(moveCard(cards, 1, 1).map((c) => c.type)).toEqual(['my-work', 'low-stock', 'late-work']);
    expect(moveCard(cards, 0, -1)).toBe(cards);
    expect(moveCard(cards, 2, 1)).toBe(cards);
  });

  it("starts from today's dashboard", () => {
    expect(classicCards().map((c) => c.type)).toEqual(CLASSIC_LAYOUT);
  });

  it('accepts only CMX addresses as tile links', () => {
    expect(isSafeLink('/assets?downNow=true')).toBe(true);
    expect(isSafeLink('/')).toBe(true);
    for (const bad of ['https://x', '//x', '/\\x', 'javascript:x', 'assets', '', '/a:b', '/' + 'a'.repeat(300)]) expect(isSafeLink(bad)).toBe(false);
    expect(isSafeLink('/work-orders?view=mine&t=10:30')).toBe(true);
  });
});
