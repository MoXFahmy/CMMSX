import type { QueuedChange } from './outbox';
import type { TechJob, TechPoint } from './types';
import { answered, levelOf } from '@/components/measure';

/**
 * A job as the technician sees it right now: the phone's copy from the server, with the changes still waiting in the
 * queue applied on top (so a tick or a photo shows at once, with or without signal).
 */
export function withQueued(job: TechJob, queue: QueuedChange[], opts: { canIssue?: boolean; points?: TechPoint[] } = {}): TechJob & { pendingPhotos: number; pendingSignatures: number; stepPhotos: Record<string, number> } {
  const canIssue = opts.canIssue ?? true;
  const mine = queue.filter((c) => c.workOrderId === job.id && c.state !== 'failed').sort((a, b) => a.seq - b.seq);
  let j: TechJob = { ...job, tasks: job.tasks.map((t) => ({ ...t })), myLabor: [...job.myLabor], comments: [...job.comments],
    plannedLines: job.plannedLines?.map((l) => ({ ...l })), usedParts: job.usedParts?.map((u) => ({ ...u })) };
  const lineFor = (p: Record<string, unknown>) =>
    j.plannedLines?.find((l) => l.id === p.lineId) ?? j.plannedLines?.find((l) => l.partId === p.partId && (l.storeId ?? null) === (p.storeId ?? null));
  let pendingPhotos = 0;
  const stepPhotos: Record<string, number> = {};
  let pendingSignatures = 0;
  for (const c of mine) {
    const p = c.payload as Record<string, unknown>;
    switch (c.type) {
      case 'task.update': {
        const t = j.tasks.find((x) => x.id === p.taskId);
        if (!t) break;
        if (typeof p.reading === 'string') t.reading = p.reading;
        if (typeof p.remarks === 'string') t.remarks = p.remarks;
        // Typed answers (0.13.0), with the level as the server will judge it.
        const kind = t.type ?? 'Check';
        if (typeof p.answer === 'string') t.answer = p.answer as typeof t.answer;
        if (typeof p.value === 'number') {
          t.value = p.value;
          const point = opts.points?.find((x) => x.id === t.measurementPointId);
          t.level = kind === 'Number' && point ? levelOf(point, p.value) : t.level ?? null;
        }
        if (typeof p.meterId === 'string') t.meterId = p.meterId;
        if (typeof p.text === 'string') t.textValue = p.text;
        if (p.isDone === false) { t.answer = null; if (kind !== 'Meter') t.level = null; if (kind === 'Number' || kind === 'Meter') t.value = null; if (kind === 'Text') t.textValue = null; }
        if (p.isDone === true && kind === 'Check' && !t.answer) t.answer = 'Ok';
        t.isDone = kind === 'Check' && t.answer === undefined ? (typeof p.isDone === 'boolean' ? p.isDone : t.isDone)
          : answered({ type: kind, answer: t.answer, value: t.value, textValue: t.textValue });
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
        if (typeof p.taskId === 'string') stepPhotos[p.taskId] = (stepPhotos[p.taskId] ?? 0) + 1;
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
  return { ...j, pendingPhotos, pendingSignatures, stepPhotos };
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
