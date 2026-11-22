/** One work or request e-mail in the Settings › Notifications grid: who it can go to, and who it goes to by default. */
export type GridRow = { key: string; applicable: string[]; defaults: string[] };

/** The recipient columns of the grid, in order. */
export const RECIPIENT_KINDS = ['requester', 'requestRecipients', 'technicians', 'contractors', 'issuer'] as const;

/** The kinds switched on for a row: the organization's choice (only kinds that apply), else the defaults. */
export function kindsOn(chosen: Record<string, string[]> | undefined, row: GridRow): string[] {
  const own = chosen?.[row.key];
  return own ? own.filter((k) => row.applicable.includes(k)) : [...row.defaults];
}

/** The saved choices after switching one cell; an event without a choice yet starts from its defaults. Other events are left as they are. */
export function withKind(chosen: Record<string, string[]> | undefined, row: GridRow, kind: string, on: boolean): Record<string, string[]> {
  if (!row.applicable.includes(kind)) return { ...(chosen ?? {}) };
  const now = kindsOn(chosen, row).filter((k) => k !== kind);
  const next = on ? row.applicable.filter((k) => k === kind || now.includes(k)) : now;
  return { ...(chosen ?? {}), [row.key]: next };
}
