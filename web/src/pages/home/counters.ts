import { apiQuery, LIST_API, LIST_PAGE, LIST_VIEW_PERMS, type Can } from '@/components/list/apiQueries';
import type { ListKey } from '@/components/list/types';
import type { PagedQuery } from './listCards';

/** A saved view pinned to the home page (0.22.0). */
export type PinnedView = { id: string; list: ListKey; name: string; query: string };

/** Whether the person can still open the view's list; without it the tile says so and asks for nothing. */
export const counterAccess = (list: ListKey, can: Can) => LIST_VIEW_PERMS[list].some((p) => can(p));

/** What a tile asks for: the list's own query for the view, one row, to read the total. */
export function counterRequest(v: PinnedView, can: Can): { path: string; query: PagedQuery } {
  return { path: LIST_API[v.list], query: { ...apiQuery(v.list, new URLSearchParams(v.query), can), page: 1, pageSize: 1 } };
}

/** Where a tile goes: the list with the view's query, and a note that keeps the person's default view from replacing it. */
export function counterLink(v: PinnedView): { to: string; state: { noDefaultView: true } } {
  return { to: v.query ? `${LIST_PAGE[v.list]}?${v.query}` : LIST_PAGE[v.list], state: { noDefaultView: true } };
}

/** The order after moving one tile a place earlier (-1) or later (1); at either end it stays. */
export function moveInOrder(ids: string[], id: string, by: -1 | 1): string[] {
  const i = ids.indexOf(id);
  const j = i + by;
  if (i < 0 || j < 0 || j >= ids.length) return ids;
  const next = [...ids];
  [next[i], next[j]] = [next[j], next[i]];
  return next;
}
