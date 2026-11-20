import type { QueuedChange } from './outbox';
import type { TechJob } from './types';

/**
 * A job as the technician sees it right now: the phone's copy from the server, with the changes still waiting in the
 * queue applied on top (so a tick or a photo shows at once, with or without signal).
 */
export function withQueued(job: TechJob, queue: QueuedChange[]): TechJob & { pendingPhotos: number; pendingSignatures: number } {
  const mine = queue.filter((c) => c.workOrderId === job.id && c.state !== 'failed').sort((a, b) => a.seq - b.seq);
  let j: TechJob = { ...job, tasks: job.tasks.map((t) => ({ ...t })), myLabor: [...job.myLabor], comments: [...job.comments] };
  let pendingPhotos = 0;
  let pendingSignatures = 0;
  for (const c of mine) {
    const p = c.payload as Record<string, unknown>;
    switch (c.type) {
      case 'task.update': {
        const t = j.tasks.find((x) => x.id === p.taskId);
        if (!t) break;
        if (typeof p.isDone === 'boolean') t.isDone = p.isDone;
        if (typeof p.reading === 'string') t.reading = p.reading;
        if (typeof p.remarks === 'string') t.remarks = p.remarks;
        break;
      }
      case 'labor.add':
        j.myLabor = [{ workDate: String(p.workDate), hours: Number(p.hours), overtimeHours: Number(p.overtimeHours ?? 0), notes: (p.notes as string) ?? null }, ...j.myLabor];
        break;
      case 'comment.add':
        j.comments = [{ body: String(p.body), userName: null, createdAt: c.at }, ...j.comments];
        break;
      case 'status.change':
        j = { ...j, status: p.status as TechJob['status'], holdReason: (p.holdReason as TechJob['holdReason']) ?? null };
        break;
      case 'photo.add':
        if ((p as { kind?: string }).kind === 'signature') pendingSignatures++; else pendingPhotos++;
        break;
    }
  }
  return { ...j, pendingPhotos, pendingSignatures };
}
