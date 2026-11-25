import type { QueuedChange } from './outbox';
import type { PlannedLine, StockPart, TechStock } from './types';

/** What a store can give a job: on hand less what other jobs reserve (the job's own reservation is its to use). */
export function available(part: StockPart, storeId: string, ownStillReserved = 0): number {
  const l = part.levels.find((x) => x.storeId === storeId);
  if (!l) return 0;
  return Math.max(0, l.onHand - Math.max(0, l.reserved - ownStillReserved));
}

/** How much of <qty> the store can't cover (0 when it can, or when the part may go below zero). */
export function shortBy(part: StockPart, storeId: string, qty: number, ownStillReserved = 0): number {
  return part.allowNegativeStock ? 0 : Math.max(0, qty - available(part, storeId, ownStillReserved));
}

/** A typed or scanned code: a part number or barcode (ignoring case). */
export function findPart(stock: TechStock | undefined, code: string): StockPart | undefined {
  const c = code.trim().toLowerCase();
  if (!stock || !c) return undefined;
  return stock.parts.find((p) => p.partNumber.toLowerCase() === c) ?? stock.parts.find((p) => p.barcode?.toLowerCase() === c);
}

/** Parts whose number, name or barcode contains the words (first 50). */
export function searchParts(stock: TechStock | undefined, text: string): StockPart[] {
  const words = text.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (!stock || words.length === 0) return [];
  return stock.parts.filter((p) => words.every((w) => `${p.partNumber} ${p.name} ${p.barcode ?? ''}`.toLowerCase().includes(w))).slice(0, 50);
}

/** What a planned line still holds in the store: planned less issued. */
export const stillReserved = (l: PlannedLine) => Math.max(0, l.quantity - l.issued);

/** The latest counted quantity of a part in a count that is still waiting in the queue. */
export function queuedCount(queue: QueuedChange[], countId: string, partId: string): number | undefined {
  const mine = queue.filter((c) => c.type === 'count.line').map((c) => ({ at: c.at, p: c.payload as { countId: string; partId: string; quantity: number } }))
    .filter((x) => x.p.countId === countId && x.p.partId === partId).sort((a, b) => a.at.localeCompare(b.at));
  return mine.length ? mine[mine.length - 1].p.quantity : undefined;
}

/**
 * Whether this person may issue parts to a job and count stock (the stock permission). The server refuses these actions for everyone else (0.29.1);
 * a server without the flag (before 0.11.0) is taken to allow them.
 */
export const canUseStock = (bundle?: { can?: { issueStock?: boolean } } | null) => bundle?.can?.issueStock ?? true;

/** Serial parts (0.24.0) are fitted and removed as units on the job, never used or given back by quantity. */
export const canUseByQuantity = (part: StockPart) => !part.isSerialTracked;
