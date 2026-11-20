import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { useDebouncedValue } from '@mantine/hooks';
import type { DataTableSortStatus } from 'mantine-datatable';

/** Address keys that aren't part of a view: the page number, and keys that open a drawer or form over the list. */
const NOT_IN_VIEW = ['page', 'open', 'new'];

/** The address of a list as a saved view stores it: every key but those above, sorted, empty values dropped. */
export function viewQuery(params: URLSearchParams): string {
  const entries = [...params.entries()].filter(([k, v]) => !NOT_IN_VIEW.includes(k) && v !== '').sort(([a], [b]) => a.localeCompare(b));
  return new URLSearchParams(entries).toString();
}

export const normalizeQuery = (query: string) => viewQuery(new URLSearchParams(query));

export type ListParams<T> = ReturnType<typeof useListParams<T>>;

/**
 * A list's state in the address bar (so it can be bookmarked, shared and saved as a view): filters, page, page size and
 * sort, plus the search box (typed text reaches the address after 300 ms). Changing anything but the page goes back to page 1.
 */
export function useListParams<T>(defaultSort: { sort: string; dir: 'asc' | 'desc' }) {
  const [params, setParams] = useSearchParams();
  const p = (k: string) => params.get(k) ?? '';
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k); }
    if (!('page' in patch)) next.delete('page');
    setParams(next, { replace: true });
  };
  /** Replaces the whole address query (applying a view, clearing every filter). */
  const replaceAll = (query: string) => setParams(new URLSearchParams(query), { replace: true });

  const [search, setSearch] = useState(p('search'));
  const [debounced] = useDebouncedValue(search, 300);
  const urlSearch = p('search');
  useEffect(() => { if (debounced !== urlSearch) set({ search: debounced || null }); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [debounced]);
  // The address changed by itself (a view was applied, filters cleared): show its search text.
  useEffect(() => { if (urlSearch !== debounced) setSearch(urlSearch); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [urlSearch]);

  const sort: DataTableSortStatus<T> = { columnAccessor: p('sort') || defaultSort.sort, direction: (p('dir') as 'asc' | 'desc') || defaultSort.dir };
  return {
    params, p, set, replaceAll, search, setSearch, sort,
    page: Number(p('page') || 1),
    pageSize: Number(p('pageSize') || 25),
    setSort: (s: DataTableSortStatus<T>) => set({ sort: String(s.columnAccessor), dir: s.direction }),
    /** The address as a saved view would store it. */
    query: viewQuery(params),
    isEmpty: viewQuery(params) === '',
  };
}
