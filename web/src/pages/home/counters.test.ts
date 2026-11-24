import { describe, expect, it } from 'vitest';
import { counterAccess, counterLink, counterRequest, moveInOrder, type PinnedView } from './counters';

const view = (query: string, list: PinnedView['list'] = 'work-orders'): PinnedView => ({ id: 'v1', list, name: 'Mine', query });

describe('counters', () => {
  it('counterAccess says no access without the list permission', () => {
    expect(counterAccess('work-orders', () => false)).toBe(false);
    expect(counterAccess('work-orders', (p) => p === 'workorders.view')).toBe(true);
    expect(counterAccess('work-requests', (p) => p === 'requests.create')).toBe(true);
  });

  it('counts with the list query and one row', () => {
    const r = counterRequest(view('tab=all&view=mine'), () => true);
    expect(r.path).toBe('/work-orders');
    expect(r.query).toEqual({ mine: 'true', sort: 'createdAt', dir: 'desc', page: 1, pageSize: 1 });
    expect(counterRequest(view('low=true', 'parts'), () => true).path).toBe('/parts');
  });

  it("opens the list without the person's default view", () => {
    expect(counterLink(view(''))).toEqual({ to: '/work-orders', state: { noDefaultView: true } });
    expect(counterLink(view('view=mine'))).toEqual({ to: '/work-orders?view=mine', state: { noDefaultView: true } });
    expect(counterLink(view('status=Active', 'assets')).to).toBe('/assets?status=Active');
  });

  it('moves a tile earlier or later', () => {
    expect(moveInOrder(['a', 'b', 'c'], 'b', -1)).toEqual(['b', 'a', 'c']);
    expect(moveInOrder(['a', 'b', 'c'], 'b', 1)).toEqual(['a', 'c', 'b']);
    expect(moveInOrder(['a', 'b', 'c'], 'a', -1)).toEqual(['a', 'b', 'c']);
    expect(moveInOrder(['a', 'b', 'c'], 'c', 1)).toEqual(['a', 'b', 'c']);
    expect(moveInOrder(['a', 'b'], 'x', 1)).toEqual(['a', 'b']);
  });
});
