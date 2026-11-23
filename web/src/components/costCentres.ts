import type { CostCentre } from '@/api/types';

/** The tree as table rows: parents first with their children under them, by code. One whose parent is hidden shows at the top. */
export function treeRows(list: CostCentre[]): (CostCentre & { depth: number })[] {
  const ids = new Set(list.map((c) => c.id));
  const children = new Map<string | null, CostCentre[]>();
  for (const c of list) {
    const k = c.parentId && ids.has(c.parentId) ? c.parentId : null;
    children.set(k, [...(children.get(k) ?? []), c]);
  }
  const out: (CostCentre & { depth: number })[] = [];
  const seen = new Set<string>();
  const walk = (parent: string | null, depth: number) => {
    for (const c of (children.get(parent) ?? []).sort((a, b) => a.code.localeCompare(b.code))) {
      if (seen.has(c.id)) continue;
      seen.add(c.id);
      out.push({ ...c, depth });
      walk(c.id, depth + 1);
    }
  };
  walk(null, 0);
  return out;
}

/** A yearly amount spread over the months, rounded to cents, with what's left over on the last month. */
export function spread(total: number, months = 12): number[] {
  const cents = Math.round(total * 100);
  const each = Math.floor(cents / months);
  return Array.from({ length: months }, (_, i) => (i === months - 1 ? cents - each * (months - 1) : each) / 100);
}

/** Picker options: active cost centres (plus the current one, even if inactive), labelled by path. */
export function costCentreOptions(list: CostCentre[], current?: string | null) {
  return treeRows(list).filter((c) => c.isActive || c.id === current).map((c) => ({ value: c.id, label: `${c.path} (${c.code})` }));
}
