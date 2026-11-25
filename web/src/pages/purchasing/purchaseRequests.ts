/** Purchase requests (0.16.0): types and the small rules the screens share. */
import type { PurchaseOrderStatus } from '@/api/types';

export type PurchaseRequestStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected' | 'PartlyOrdered' | 'Ordered' | 'Closed' | 'Cancelled';

export type PurchaseRequestListItem = {
  id: string; number: string; status: PurchaseRequestStatus; siteName: string; requestedByName?: string | null; neededBy?: string | null; lines: number;
  estimatedTotal?: number | null; submittedAt?: string | null; isLate: boolean;
};

export type PurchaseRequestLine = {
  id: string; lineNumber: number; partId?: string | null; part?: { id: string; partNumber: string; name: string; preferredSupplierId?: string | null } | null;
  description: string; unitOfMeasure?: string | null; quantity: number; neededBy?: string | null;
  workOrder?: { id: string; number: string; title: string } | null; asset?: { id: string; assetNumber: string; name: string } | null;
  store?: { id: string; code: string; name: string } | null; estimatedUnitPrice?: number | null; reason?: string | null;
  order?: { id: string; number: string; lineId: string; status: PurchaseOrderStatus } | null; ordered: number; received: number;
};

export type PurchaseRequestDetail = {
  id: string; number: string; status: PurchaseRequestStatus; siteId: string; siteName?: string | null; requestedById?: string | null; requestedByName?: string | null;
  neededBy?: string | null; notes?: string | null; recipientIds: string[]; submittedAt?: string | null; decidedAt?: string | null; decidedByName?: string | null;
  rejectReason?: string | null; cancelledAt?: string | null; cancelReason?: string | null; closedAt?: string | null; createdAt: string;
  lines: PurchaseRequestLine[];
  comments: { id: string; body: string; userId?: string | null; userName?: string | null; createdAt: string }[];
  actions: { edit: boolean; submit: boolean; approve: boolean; reject: boolean; cancel: boolean; delete: boolean; order: boolean; close: boolean; comment: boolean };
  rowVersion: string;
};

/** An approved line a buyer can put on an order, from one request's page or from Lines to order across requests. */
export type OrderableLine = {
  id: string; siteId: string; requestNumber?: string; description: string; quantity: number; unitOfMeasure?: string | null;
  part?: { id: string; partNumber: string; name: string; preferredSupplierId?: string | null } | null; estimatedUnitPrice?: number | null;
  workOrder?: { id: string; number: string } | null;
};

/** A line as the request form and the "Request purchase" buttons send it. */
export type PrLineDraft = {
  partId: string | null; description: string; unitOfMeasure: string; quantity: number; storeId: string | null; workOrderId: string | null;
  neededBy: string | null; estimatedUnitPrice: number | null; reason: string;
};

const colours: Record<PurchaseRequestStatus, string> = {
  Draft: 'gray', Submitted: 'blue', Approved: 'teal', Rejected: 'red', PartlyOrdered: 'yellow', Ordered: 'green', Closed: 'dark', Cancelled: 'gray',
};
export const requestStatusColor = (s: PurchaseRequestStatus) => colours[s];

/** Lines a buyer can still put on an order: the request is approved (or partly ordered) and the line isn't on a live order. */
export const orderableLines = (d: PurchaseRequestDetail) =>
  d.status === 'Approved' || d.status === 'PartlyOrdered' ? d.lines.filter((l) => !l.order) : [];

/** How many draft orders the chosen lines make: one per supplier and site (lines without a supplier yet don't count). */
export const previewGroups = (lines: { supplierId: string | null; siteId: string }[]) =>
  new Set(lines.filter((l) => l.supplierId).map((l) => `${l.supplierId}|${l.siteId}`)).size;

/** What the re-order proposal lets someone do with ticked lines: buyers raise draft orders, requesters raise purchase requests, others only look. */
export type ProposalMode = 'order' | 'request' | 'view';
export const proposalMode = (canCreate: boolean, canRequest: boolean): ProposalMode =>
  canCreate ? 'order' : canRequest ? 'request' : 'view';

/** Short planned parts become one request per site, each line for its work order and needed by its due date. */
export function draftFromShortages(rows: { partId: string; storeId: string; siteId: string; short: number; workOrderId?: string | null; dueDate?: string | null;
  description: string; unitOfMeasure: string }[]): { siteId: string; lines: PrLineDraft[] }[] {
  const bySite = new Map<string, PrLineDraft[]>();
  for (const r of rows) {
    const list = bySite.get(r.siteId) ?? [];
    list.push({ partId: r.partId, description: r.description, unitOfMeasure: r.unitOfMeasure, quantity: r.short, storeId: r.storeId, workOrderId: r.workOrderId ?? null,
      neededBy: r.dueDate ?? null, estimatedUnitPrice: null, reason: '' });
    bySite.set(r.siteId, list);
  }
  return [...bySite.entries()].map(([siteId, lines]) => ({ siteId, lines }));
}

export type ShortageRow = Parameters<typeof draftFromShortages>[0][number];
/** A short line that can't go on a request, and why ("noSite": its store isn't in the stores list, so its site is unknown). */
export type SkippedShortage = { row: ShortageRow; reason: 'noSite' };

/** As <c>draftFromShortages</c>, but a line whose store has no known site is kept apart and named instead of dropped (0.29.1). */
export function draftsFromShortages(rows: ShortageRow[]): { drafts: { siteId: string; lines: PrLineDraft[] }[]; skipped: SkippedShortage[] } {
  const skipped: SkippedShortage[] = rows.filter((r) => !r.siteId).map((row) => ({ row, reason: 'noSite' as const }));
  return { drafts: draftFromShortages(rows.filter((r) => r.siteId)), skipped };
}

const closed: PurchaseRequestStatus[] = ['Ordered', 'Closed', 'Cancelled'];
/** Needed before today and not yet ordered, closed or cancelled. Dates are ISO days. */
export const isLate = (neededBy: string | null | undefined, today: string, status: PurchaseRequestStatus) =>
  !!neededBy && neededBy < today && !closed.includes(status);

/** A row of the "order from requests" dialog. */
export type OrderRow = { lineId: string; on: boolean; supplierId: string | null; quantity: number | ''; unitPrice: number | ''; estimate: number | null };

/**
 * The dialog's rows: no price typed, so the supplier's catalogue price, else the request's estimate, prices the order (0.18.0). The estimate
 * is shown as a hint; a price the buyer types wins.
 */
export const orderRows = (lines: OrderableLine[]): OrderRow[] =>
  lines.map((l) => ({ lineId: l.id, on: true, supplierId: l.part?.preferredSupplierId ?? null, quantity: l.quantity, unitPrice: '', estimate: l.estimatedUnitPrice ?? null }));

/** One total of the ticked proposal lines in a currency. */
export type ProposalTotal = { currency: string; amount: number };

/**
 * The ticked re-order lines added up per currency, in the order the currencies first appear (0.18.0-1, 0.29.1). CMX has no exchange-rate table, so prices
 * in different currencies are never added together.
 */
export function proposalTotals(values: { amount: number; currency: string }[]): ProposalTotal[] {
  const sums = new Map<string, number>();
  for (const v of values) sums.set(v.currency, (sums.get(v.currency) ?? 0) + v.amount);
  return [...sums.entries()].map(([currency, amount]) => ({ currency, amount: Math.round(amount * 100) / 100 }));
}
