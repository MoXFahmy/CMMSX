import { apiQuery, LIST_API, LIST_PAGE, type Can } from '@/components/list/apiQueries';
import type { ListKey } from '@/components/list/types';

export type ListCardType = 'my-work' | 'unassigned-work' | 'late-work' | 'assets-down' | 'low-stock';

/**
 * The home cards that show the top of a list: which list, the address their "View all" opens, and the order that puts
 * the most pressing first (the closest sort the list has).
 */
export const LIST_CARDS: Record<ListCardType, { list: ListKey; address: string; sort?: string; dir?: 'asc' | 'desc' }> = {
  'my-work': { list: 'work-orders', address: 'view=mine', sort: 'dueDate', dir: 'asc' },
  'unassigned-work': { list: 'work-orders', address: 'view=unassigned', sort: 'priority', dir: 'desc' },
  'late-work': { list: 'work-orders', address: 'view=overdue', sort: 'dueDate', dir: 'asc' },
  'assets-down': { list: 'assets', address: 'downNow=true', sort: 'criticality', dir: 'desc' },
  'low-stock': { list: 'parts', address: 'low=true' },
};

/** Rows a list card shows. */
export const LIST_CARD_ROWS = 8;

/** A list request: the list's query plus a page. */
export type PagedQuery = Record<string, string | string[] | boolean | number | undefined>;

/** What a list card asks for: the list's own query for its link, in its order, the first rows only. */
export function listCardRequest(type: ListCardType, can: Can): { path: string; query: PagedQuery; link: string } {
  const card = LIST_CARDS[type];
  const base = apiQuery(card.list, new URLSearchParams(card.address), can);
  return {
    path: LIST_API[card.list],
    query: { ...base, ...(card.sort ? { sort: card.sort, dir: card.dir } : {}), page: 1, pageSize: LIST_CARD_ROWS },
    link: `${LIST_PAGE[card.list]}?${card.address}`,
  };
}
