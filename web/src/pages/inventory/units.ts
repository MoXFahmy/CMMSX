import type { UnitState } from '@/api/types';

/** Serial units (0.24.0): where a unit is, and serial numbers as typed. */
export type { UnitState };

export const UNIT_STATES: UnitState[] = ['InStore', 'Fitted', 'AtRepair', 'Scrapped'];

export const UNIT_STATE_COLOR: Record<UnitState, string> = { InStore: 'teal', Fitted: 'blue', AtRepair: 'orange', Scrapped: 'gray' };

/** Serial numbers from a text box: one per line (commas and semicolons also separate), trimmed, blanks dropped, case and order kept. */
export function parseSerials(text: string): string[] {
  return text.split(/[\r\n,;]+/).map((s) => s.trim()).filter((s) => s.length > 0);
}

/** What's wrong with serials for <needed> pieces: a different count, or the same serial twice (ignoring case, as the server does). */
export function serialProblem(serials: string[], needed: number): 'count' | 'duplicate' | null {
  if (new Set(serials.map((s) => s.toLowerCase())).size !== serials.length) return 'duplicate';
  if (serials.length !== needed) return 'count';
  return null;
}
