/** Approval levels on orders (0.16.0): types and the rules the settings page and the order page share. */

export type ApprovalRule = 'Any' | 'All';
export type StepStatus = 'Pending' | 'Waiting' | 'Approved' | 'Rejected' | 'Stopped';

export type LevelRow = {
  id: string; siteId?: string | null; siteName?: string | null; orderTypeId?: string | null; orderTypeName?: string | null; name: string; minAmount: number; rule: ApprovalRule;
  approvers: { id: string; name?: string | null; ok: boolean; problem?: 'no-permission' | 'no-site-access' | 'inactive' | null }[];
  rowVersion?: string;
};

export type ApprovalStep = {
  step: number; name: string; minAmount: number; rule: ApprovalRule; status: StepStatus; doneAt?: string | null;
  approvers: { id: string; name?: string | null; signed: boolean; approved?: boolean | null; at?: string | null; comment?: string | null; canSign: boolean }[];
};

export type ApprovalPanel = {
  round: number; steps: ApprovalStep[]; earlierRounds: { round: number; steps: ApprovalStep[] }[]; nobodyCanSign: boolean; waiting: boolean;
};

/**
 * The same rule as the server: the most specific levels win — the site's for the order's type, else the site's for every type,
 * else the organization's for the type, else the organization's for every type; then minimum ≤ amount, lowest first.
 */
export function levelsFor(levels: LevelRow[], siteId: string | null, orderTypeId: string | null, amount: number): LevelRow[] {
  const sets: ((l: LevelRow) => boolean)[] = [
    (l) => !!siteId && l.siteId === siteId && !!orderTypeId && l.orderTypeId === orderTypeId,
    (l) => !!siteId && l.siteId === siteId && !l.orderTypeId,
    (l) => !l.siteId && !!orderTypeId && l.orderTypeId === orderTypeId,
    (l) => !l.siteId && !l.orderTypeId,
  ];
  const chosen = sets.map((m) => levels.filter(m)).find((s) => s.length > 0) ?? [];
  return chosen.filter((l) => l.minAmount <= amount).sort((a, b) => a.minAmount - b.minAmount);
}

/** The printed order lists the current round's signatures when it has any; otherwise it keeps the single "Approved by" line. */
export const printsSignatures = (p: ApprovalPanel | null | undefined) => !!p && p.steps.some((s) => s.approvers.some((a) => a.signed));

type T = (key: string, params?: Record<string, unknown>) => string;

/** "An order of 50,000 needs: Maintenance manager → Plant manager." — or that no level applies and approvers sign within their own limit. */
export function exampleSentence(t: T, levels: LevelRow[], amount: number, fmt: (n: number) => string): string {
  const needed = [...levels].filter((l) => l.minAmount <= amount).sort((a, b) => a.minAmount - b.minAmount);
  return needed.length === 0
    ? t('approvals.exampleNone', { amount: fmt(amount) })
    : t('approvals.example', { amount: fmt(amount), names: needed.map((l) => l.name).join(' → ') });
}

export function stepState(s: Pick<ApprovalStep, 'status'>): 'done' | 'waiting' | 'pending' | 'stopped' {
  if (s.status === 'Approved') return 'done';
  if (s.status === 'Waiting') return 'waiting';
  if (s.status === 'Pending') return 'pending';
  return 'stopped';
}

/** Names on the waiting step who haven't signed yet. */
export const whoIsNext = (p: ApprovalPanel) =>
  (p.steps.find((s) => s.status === 'Waiting')?.approvers ?? []).filter((a) => !a.signed).map((a) => a.name ?? '').filter(Boolean);
