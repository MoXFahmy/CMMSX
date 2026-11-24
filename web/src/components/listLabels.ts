import type { i18n as I18n, TFunction } from 'i18next';
import { ASSET_STATUSES, CRITICALITIES, WORK_PRIORITIES, WORK_TYPES, type ListKey, type ListLabels } from '@/api/types';

/** The translation key prefix each list's words live under. */
export const LIST_PREFIX: Record<ListKey, string> = { workType: 'workType', priority: 'priority', assetStatus: 'status', criticality: 'criticality' };
export const LIST_VALUES: Record<ListKey, readonly string[]> = { workType: WORK_TYPES, priority: WORK_PRIORITIES, assetStatus: ASSET_STATUSES, criticality: CRITICALITIES };
/** The colours an organization may pick (Mantine's palette; the server accepts the same). */
export const LIST_PALETTE = ['gray', 'red', 'pink', 'grape', 'violet', 'indigo', 'blue', 'cyan', 'teal', 'green', 'lime', 'yellow', 'orange'] as const;

const originals = new WeakMap<I18n, Map<string, string | undefined>>();
/** The labels last applied (the signed-in organization's), for the screens' pickers and badges. */
let applied: ListLabels | undefined;

/**
 * Puts the organization's names (0.21.0) in place of the built-in words of the four lists, in English and Arabic, so every
 * screen that shows `t('priority.Urgent')` shows the organization's word. Names no longer set get their built-in words back.
 */
export function applyListLabels(i18n: I18n, labels: ListLabels | undefined): void {
  applied = labels;
  let saved = originals.get(i18n);
  if (!saved) { saved = new Map(); originals.set(i18n, saved); }
  for (const list of Object.keys(LIST_PREFIX) as ListKey[]) {
    for (const value of LIST_VALUES[list]) {
      const key = `${LIST_PREFIX[list]}.${value}`;
      const label = labels?.[list]?.[value];
      for (const [lng, own] of [['en', label?.name], ['ar', label?.nameAr]] as const) {
        const id = `${lng}|${key}`;
        if (!saved.has(id)) saved.set(id, i18n.getResource(lng, 'translation', key) as string | undefined);
        const text = own?.trim() || saved.get(id);
        if (text !== undefined) i18n.addResource(lng, 'translation', key, text);
      }
    }
  }
}

/** Picker options in the organization's order (then the built-in one), hidden values left out unless it's the record's own. */
export function listOptions(list: ListKey, values: readonly string[], labels: ListLabels | undefined, t: TFunction, current?: string | null) {
  const own = labels?.[list] ?? {};
  return values
    .map((v, i) => ({ v, i, order: own[v]?.order ?? null }))
    .filter((x) => !own[x.v]?.hidden || x.v === current)
    .sort((a, b) => (a.order ?? Number.MAX_SAFE_INTEGER) - (b.order ?? Number.MAX_SAFE_INTEGER) || a.i - b.i)
    .map((x) => ({ value: x.v, label: t(`${LIST_PREFIX[list]}.${x.v}`) }));
}

/** The organization's colour for a value, else the screen's own. */
export function listColor(list: ListKey, value: string, labels: ListLabels | undefined, fallback: string): string {
  return labels?.[list]?.[value]?.color || fallback;
}

/** A form's default, unless the organization hid it: then the first value it shows. */
export function firstVisible(list: ListKey, values: readonly string[], labels: ListLabels | undefined, preferred: string): string {
  if (!labels?.[list]?.[preferred]?.hidden) return preferred;
  const t = ((k: string) => k) as unknown as TFunction;
  return listOptions(list, values, labels, t)[0]?.value ?? preferred;
}

/** Picker options for the signed-in organization (see {@link listOptions}); filters and rules pass keepHidden to offer every value. */
export function listItems(list: ListKey, t: TFunction, current?: string | null, keepHidden = false) {
  const labels = keepHidden && applied?.[list]
    ? { ...applied, [list]: Object.fromEntries(Object.entries(applied[list]!).map(([k, v]) => [k, { ...v, hidden: false }])) }
    : applied;
  return listOptions(list, LIST_VALUES[list], labels, t, current);
}

/** The signed-in organization's colour for a value, else the screen's own. */
export const listColorOf = (list: ListKey, value: string, fallback: string) => listColor(list, value, applied, fallback);

/** A new record's default, unless the signed-in organization hid it. */
export const listDefault = (list: ListKey, preferred: string) => firstVisible(list, LIST_VALUES[list], applied, preferred);

