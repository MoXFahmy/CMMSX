import type { WorkOrderStatus } from '@/api/types';

/** Which required comment (0.10.0) a status change needs, or null; the same rules as the server. Completing follows requireCompletionNotes. */
export function noteRequired(rules: string[] | undefined, from: WorkOrderStatus, to: WorkOrderStatus): 'hold' | 'reopen' | 'cancel' | null {
  const on = rules ?? [];
  if (to === 'OnHold' && on.includes('hold')) return 'hold';
  if (to === 'Cancelled' && on.includes('cancel')) return 'cancel';
  if ((from === 'Completed' || from === 'Closed' || from === 'Cancelled') && (to === 'InProgress' || to === 'Open') && on.includes('reopen')) return 'reopen';
  return null;
}
