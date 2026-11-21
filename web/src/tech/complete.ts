import type { QueuedChange } from './outbox';

type FileLike = { isSignature?: boolean; signedBy?: string | null };

/**
 * Whether `name` has already signed this job: a signature on the server copy, or one still waiting to be sent (a queued
 * signature without a name is the technician's own, `myName`). Completing again after a refusal doesn't upload it twice.
 */
export function alreadySigned(name: string, files: FileLike[], queue: QueuedChange[], jobId: string, myName?: string): boolean {
  if (!name.trim()) return false;
  if (files.some((f) => f.isSignature && f.signedBy === name)) return true;
  return queue.some((c) => {
    const p = c.payload as { kind?: string; signedBy?: string | null } | null;
    return c.workOrderId === jobId && c.type === 'photo.add' && p?.kind === 'signature' && (p.signedBy || myName) === name;
  });
}

/** Earlier completions of this job the server refused: completing again replaces them. */
export function staleCompletions(queue: QueuedChange[], jobId: string): QueuedChange[] {
  return queue.filter((c) => c.workOrderId === jobId && c.type === 'status.change' && c.state === 'failed'
    && (c.payload as { status?: string } | null)?.status === 'Completed');
}

/** "Log labour before completing" isn't met yet: nobody's hours on the job (as the server checks), and none of mine waiting. */
export function needsLabour(job: { hasLabor?: boolean; myLabor: unknown[] }): boolean {
  return !job.hasLabor && job.myLabor.length === 0;
}
