import { useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, put } from '@/api/client';
import { notifyError } from '@/components/utils';
import type { ListColumn, ListKey } from './types';

type Settings = Record<string, { hiddenColumns: string[]; fromRole?: boolean }>;

export type ListColumns<T> = ReturnType<typeof useListColumns<T>>;

/**
 * Which columns a person shows on a list, saved on the server (it follows them to any device). The saved list holds
 * tokens: "x" hides column x, "+x" shows a column that is hidden by default. With nothing saved, the defaults apply.
 */
export function useListColumns<T>(list: ListKey, all: ListColumn<T>[],
  opts: { defaultHidden?: string[]; hiddenByDefault?: (accessor: string) => boolean; legacyKey?: string } = {}) {
  const qc = useQueryClient();
  const { data: settings, isSuccess } = useQuery({ queryKey: ['list-settings'], queryFn: () => get<Settings>('/auth/me/list-settings'), staleTime: Infinity });
  const tokens = settings?.[list]?.hiddenColumns ?? [];
  const isDefaultHidden = (acc: string) => (opts.defaultHidden ?? []).includes(acc) || (opts.hiddenByDefault?.(acc) ?? false);
  const isVisible = (acc: string) => (tokens.includes(acc) ? false : tokens.includes(`+${acc}`) ? true : !isDefaultHidden(acc));

  // Saves run one at a time, in the order the person ticked; if one fails, show what the server really has.
  const save = useMutation({
    mutationFn: (next: string[]) => put(`/auth/me/list-settings/${list}`, { hiddenColumns: next }),
    scope: { id: `list-settings-${list}` },
    onError: (e) => { notifyError(e); void qc.invalidateQueries({ queryKey: ['list-settings'] }); },
  });
  const setTokens = (next: string[]) => {
    qc.setQueryData<Settings>(['list-settings'], (cur) => ({ ...(cur ?? {}), [list]: { hiddenColumns: next } }));
    save.mutate(next);
  };
  const toggle = (acc: string) => {
    const base = tokens.filter((x) => x !== acc && x !== `+${acc}`);
    const byDefault = isDefaultHidden(acc);
    setTokens(isVisible(acc) ? (byDefault ? base : [...base, acc]) : (byDefault ? [...base, `+${acc}`] : base));
  };

  // Carry over a choice an older version kept in this browser, once (the asset list did that before 0.2.0).
  useEffect(() => {
    if (!isSuccess || !opts.legacyKey || settings?.[list]) return;
    try {
      const raw = localStorage.getItem(opts.legacyKey);
      if (!raw) return;
      const old: unknown = JSON.parse(raw);
      if (Array.isArray(old)) {
        // The old list named every hidden column, defaults included: a default-hidden column missing from it was shown.
        const hidden = old.filter((x): x is string => typeof x === 'string');
        setTokens([...hidden, ...(opts.defaultHidden ?? []).filter((acc) => !hidden.includes(acc)).map((acc) => `+${acc}`)]);
      }
      localStorage.removeItem(opts.legacyKey);
    } catch { /* storage blocked or unreadable: keep the defaults */ }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isSuccess]);

  /** Drops the person's own choice: their role's default columns (0.22.0), or the list's own, apply again. */
  const reset = async () => {
    try { await del(`/auth/me/list-settings/${list}`); } catch (e) { notifyError(e); }
    await qc.invalidateQueries({ queryKey: ['list-settings'] });
  };

  return { all, columns: all.filter((c) => isVisible(String(c.accessor))), tokens, setTokens, isVisible, toggle, reset, fromRole: !!settings?.[list]?.fromRole };
}
