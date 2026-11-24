import type { QueuedChange } from './outbox';
import type { StockUnit, TechJob, TechPoint } from './types';
import { answered, levelOf } from '@/components/measure';

/**
 * A job as the technician sees it right now: the phone's copy from the server, with the changes still waiting in the
 * queue applied on top (so a tick or a photo shows at once, with or without signal).
 */
export function withQueued(job: TechJob, queue: QueuedChange[], opts: { canIssue?: boolean; points?: TechPoint[] } = {}): TechJob & { pendingPhotos: number; pendingSignatures: number; stepPhotos: Record<string, number> } {
  const canIssue = opts.canIssue ?? true;
  const mine = queue.filter((c) => c.workOrderId === job.id && c.state !== 'failed').sort((a, b) => a.seq - b.seq);
  let j: TechJob = { ...job, tasks: job.tasks.map((t) => ({ ...t })), myLabor: [...job.myLabor], comments: [...job.comments],
    plannedLines: job.plannedLines?.map((l) => ({ ...l })), usedParts: job.usedParts?.map((u) => ({ ...u })), units: job.units?.map((u) => ({ ...u })) };
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
        // A completion's cause and remedy (0.14.0); one that leaves them out keeps what the job has, as the server does.
        if (typeof p.failureCodeId === 'string') j = { ...j, failureCodeId: p.failureCodeId };
        if (typeof p.remedyId === 'string') j = { ...j, remedyId: p.remedyId };
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
      // Serial units (0.24.0): a fit shows the unit on the job at once (the change carries its serial and part for this), a removal takes it off.
      case 'unit.fit':
        j.units = [...(j.units ?? []).filter((u) => u.id !== p.unitId), { id: String(p.unitId), assetNumber: String(p.assetNumber ?? ''), serialNumber: (p.serialNumber as string) ?? null,
          partId: String(p.partId ?? ''), partName: (p.partName as string) ?? null, fittedToId: String(p.assetId) }];
        break;
      case 'unit.remove':
        j.units = (j.units ?? []).filter((u) => u.id !== p.unitId);
        break;
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

/**
 * The units in store as the technician sees them right now (0.24.0): a queued fit takes the unit out; a queued removal back to a
 * store puts it in the job's default store (where the server will return it).
 */
export function unitsWithQueued(units: StockUnit[], queue: QueuedChange[], jobs: TechJob[]): StockUnit[] {
  let list = units.map((u) => ({ ...u }));
  const fittedNow = new Map<string, StockUnit>();
  for (const c of queue.filter((x) => x.state !== 'failed').sort((a, b) => a.seq - b.seq)) {
    const p = c.payload as Record<string, unknown>;
    if (c.type === 'unit.fit') {
      const was = list.find((u) => u.id === p.unitId);
      if (was) fittedNow.set(was.id, was);
      list = list.filter((u) => u.id !== p.unitId);
    } else if (c.type === 'unit.remove' && p.outcome === 'Store') {
      const job = jobs.find((x) => x.id === c.workOrderId);
      const onJob = job?.units?.find((u) => u.id === p.unitId);
      const known = onJob ? { id: onJob.id, assetNumber: onJob.assetNumber, serialNumber: onJob.serialNumber, partId: onJob.partId } : fittedNow.get(String(p.unitId));
      if (known && job?.defaultStoreId && !list.some((u) => u.id === known.id))
        list.push({ id: known.id, assetNumber: known.assetNumber, serialNumber: known.serialNumber, partId: known.partId, storeId: job.defaultStoreId });
    }
  }
  return list;
}
