import type { QueuedChange } from './outbox';
import { answered, failed } from '@/components/measure';
import type { TechStep } from './types';

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

const name = (s: TechStep) => `#${s.sequence} ${s.description}`;
const typed = (s: TechStep) => ({ ...s, type: s.type ?? 'Check' as const, answer: s.answer ?? (s.type === undefined || s.type === 'Check' ? (s.isDone ? 'Ok' as const : null) : null) });

/** Safety steps (0.13.0) still to answer, or answered Not OK, that hold up work on `assetId` (and on the whole job when it is null). */
export function safetyOpen(tasks: TechStep[], assetId?: string | null): TechStep[] {
  return tasks.filter((s) => s.beforeStart && (!s.assetId || s.assetId === assetId) && (!answered(typed(s)) || failed(typed(s))));
}

/** What completing still needs (0.13.0): every safety step answered and OK (each round asset's too), a photo on photo-required steps (sent or waiting), and readings when the organization asks. */
export function stepProblems(job: { tasks: TechStep[]; files: { workOrderTaskId?: string | null }[] }, queue: QueuedChange[], rules: { readings?: boolean }) {
  const waiting = new Set(queue.filter((c) => c.type === 'photo.add' && c.state !== 'failed').map((c) => (c.payload as { taskId?: string } | null)?.taskId).filter(Boolean));
  const withPhoto = (s: TechStep) => job.files.some((f) => f.workOrderTaskId === s.id) || waiting.has(s.id);
  return {
    safety: job.tasks.filter((s) => s.beforeStart && (!answered(typed(s)) || failed(typed(s)))).map(name),
    photos: job.tasks.filter((s) => s.photoRequired && !withPhoto(s)).map(name),
    readings: rules.readings ? job.tasks.filter((s) => (s.type === 'Number' || s.type === 'Meter' || s.type === 'Text') && !answered(typed(s))).map(name) : [],
  };
}
