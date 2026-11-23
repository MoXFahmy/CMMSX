import type { ContractStatus } from '@/api/types';

/** Badge colour of a contract's status (0.17.0). */
export function statusColor(s: ContractStatus): string {
  return ({ Upcoming: 'blue', Active: 'green', Expired: 'gray', Terminated: 'red' } as Record<ContractStatus, string>)[s] ?? 'gray';
}

/** What the contract's deadline means: the last day to stop the renewal, or the end of a contract that doesn't renew (an i18n key). */
export function deadlineLabel(c: { autoRenew: boolean }): string {
  return c.autoRenew ? 'contracts.noticeDeadline' : 'contracts.endsOn';
}

/** Whole days from <today> to <deadline> (both "yyyy-mm-dd"); negative once it has passed. */
export function daysLeft(deadline: string, today: string): number {
  return Math.round((Date.parse(`${deadline}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000);
}
