import { describe, expect, it } from 'vitest';
import { apiQuery } from '@/components/list/apiQueries';
import { LIST_CARDS, listCardRequest } from './listCards';

const all = () => true;
const strip = (q: Record<string, unknown>) => Object.fromEntries(Object.entries(q).filter(([k]) => !['sort', 'dir', 'page', 'pageSize'].includes(k)));

describe('list cards', () => {
  it('ask the list endpoint what their link opens', () => {
    const my = listCardRequest('my-work', all);
    expect(my.path).toBe('/work-orders');
    expect(my.link).toBe('/work-orders?view=mine');
    expect(my.query).toMatchObject({ mine: 'true', status: ['Open', 'InProgress', 'OnHold'], page: 1, pageSize: 8, sort: 'dueDate', dir: 'asc' });
    expect(listCardRequest('unassigned-work', all).query).toMatchObject({ unassigned: 'true', sort: 'priority', dir: 'desc' });
    expect(listCardRequest('late-work', all).query).toMatchObject({ overdue: 'true', sort: 'dueDate', dir: 'asc' });
    const down = listCardRequest('assets-down', all);
    expect(down.path).toBe('/assets');
    expect(down.link).toBe('/assets?downNow=true');
    const low = listCardRequest('low-stock', all);
    expect(low.path).toBe('/parts');
    expect(low.link).toBe('/inventory?low=true');
    expect(low.query).toMatchObject({ belowReorder: 'true', activeOnly: 'true' });
  });

  it('count what the list counts', () => {
    for (const type of Object.keys(LIST_CARDS) as (keyof typeof LIST_CARDS)[]) {
      const { query } = listCardRequest(type, all);
      const card = LIST_CARDS[type];
      expect(strip(query)).toEqual(strip(apiQuery(card.list, new URLSearchParams(card.address), all)));
    }
  });
});
