import type { QueuedChange } from './outbox';
import type { TechJob } from './types';

/**
 * A job as the technician sees it right now: the phone's copy from the server, with the changes still waiting in the
 * queue applied on top (so a tick or a photo shows at once, with or without signal).
 */
export function withQueued(job: TechJob, queue: QueuedChange[], opts: { canIssue?: boolean } = {}): TechJob & { pendingPhotos: number; pendingSignatures: number } {
  const canIssue = opts.canIssue ?? true;
  const mine = queue.filter((c) => c.workOrderId === job.id && c.state !== 'failed').sort((a, b) => a.seq - b.seq);
  let j: TechJob = { ...job, tasks: job.tasks.map((t) => ({ ...t })), myLabor: [...job.myLabor], comments: [...job.comments],
    plannedLines: job.plannedLines?.map((l) => ({ ...l })), usedParts: job.usedParts?.map((u) => ({ ...u })) };
  const lineFor = (p: Record<string, unknown>) =>
    j.plannedLines?.find((l) => l.id === p.lineId) ?? j.plannedLines?.find((l) => l.partId === p.partId && (l.storeId ?? null) === (p.storeId ?? null));
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
      // Parts (0.11.0): until the next copy the line shows them used (or, for people who may not issue stock, asked for).
      case 'part.use':
      case 'stock.issue': {
        const qty = Number(p.quantity) || 0;
        const line = lineFor(p);
        if (c.type === 'part.use' && !canIssue) {
          if (line) line.quantity = Math.max(line.quantity, line.issued + qty);
          else j.plannedLines = [...(j.plannedLines ?? []), { id: `queued-${c.clientId}`, partId: String(p.partId), storeId: (p.storeId as string) ?? null, quantity: qty, issued: 0 }];
        } else if (line) line.issued += qty;
        else {
          const used = j.usedParts?.find((u) => u.partId === p.partId && u.storeId === p.storeId);
          if (used) used.issued += qty;
          else j.usedParts = [...(j.usedParts ?? []), { partId: String(p.partId), storeId: String(p.storeId), issued: qty }];
        }
        break;
      }
      case 'part.return': {
        let qty = Number(p.quantity) || 0;
        const line = lineFor(p);
        if (line && line.issued > 0) { const back = Math.min(qty, line.issued); line.issued -= back; qty -= back; }
        const used = j.usedParts?.find((u) => u.partId === p.partId && u.storeId === p.storeId);
        if (used && qty > 0) used.issued -= qty;
        j.usedParts = j.usedParts?.filter((u) => u.issued > 0);
        break;
      }
    }
  }
  return { ...j, pendingPhotos, pendingSignatures };
}

/** A change the server took, with when: the view keeps showing it until a copy of the jobs fetched after that arrives. */
export type SentChange = QueuedChange & { sentAt: number };

/**
 * The sent changes still to show on top of the phone's copy: those sent after the copy was fetched (`fetchedAt`, the
 * time the fetch started). A sent photo isn't shown as waiting; it appears with the job's files on the next copy.
 */
export function stillShown(sent: SentChange[], fetchedAt: number | undefined): SentChange[] {
  return sent.filter((c) => c.type !== 'photo.add' && (fetchedAt === undefined || c.sentAt > fetchedAt));
}
