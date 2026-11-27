import type { TFunction } from 'i18next';

/** A month of the PM annual plan (0.14.0): what happened (done on time, late, missed, cancelled) and what is still planned. */
export type PlanCell = { done: number; late: number; missed: number; cancelled: number; planned: number };
export const PLAN_PARTS = ['done', 'late', 'missed', 'cancelled', 'planned'] as const;

/** A plan cell in words, for CSV and screen readers: "done 2 · late 1 · planned 1"; empty when nothing. */
export function planText(c: PlanCell | null | undefined, t: TFunction): string {
  if (!c) return '';
  return PLAN_PARTS.filter((k) => c[k] > 0).map((k) => `${t(`rep.plan.${k}`)} ${c[k]}`).join(' · ');
}

/** What a report row opens (0.14.0 adds work orders, requests and a filtered work order list), most specific first. */
export function rowLink(r: Record<string, unknown>, key: string): string | null {
  if (typeof r.listQuery === 'string' && r.listQuery) return `/work-orders?${r.listQuery}`;
  if (r.workOrderId) return `/work-orders/${r.workOrderId}`;
  if (r.requestId) return `/work-requests/${r.requestId}`;
  if (r.partId) return `/inventory/parts/${r.partId}`;
  if (r.assetId) return `/assets/${r.assetId}`;
  if (key === 'open-purchase-orders' && r.id) return `/purchasing/orders/${r.id}`;
  return null;
}

/**
 * A status badge's colour. The stock re-order report names each line's state in `stockState` (0.29.2), because its status words come in
 * the reader's language: out of stock and below minimum are red, re-order is orange; on order and every other report's status are grey.
 */
export function statusColor(r: Record<string, unknown>): 'red' | 'orange' | 'gray' {
  if (r.stockState === 'out' || r.stockState === 'below') return 'red';
  return r.stockState === 'reorder' ? 'orange' : 'gray';
}

/** The rows a chart shows: a tree report's top level (level 0), else every row. */
export function treeRows<T extends Record<string, unknown>>(rows: T[]): T[] {
  return rows.filter((r) => r.level === undefined || r.level === null || r.level === 0);
}

/** Whether a report's rows form a tree (rows carry a level): sorting would break it. */
export const isTree = (rows: Record<string, unknown>[]) => rows.some((r) => typeof r.level === 'number');
