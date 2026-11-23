import type { FilledLine } from '@/api/types';
import { fmtQty } from '@/components/utils';

/** Whole packs for a number of pieces, rounded up and never below the minimum (the server's PackMath.Packs, 0.18.0). */
export function packsFor(pieces: number, packSize: number, minPacks: number): number {
  const least = Math.max(minPacks, 1);
  if (packSize <= 0) return Math.max(Math.ceil(pieces), least);
  return Math.max(Math.ceil(pieces / packSize - 1e-9), least);
}

/** The part's own units in an ordered quantity of packs. */
export function piecesOf(quantity: number, packSize: number): number {
  return Math.round(quantity * (packSize || 1) * 10000) / 10000;
}

/** "1 Box of 100 = 100 EA"; just "3 EA" for a pack of 1. */
export function packText(quantity: number, unit: string, packSize: number, partUnit: string): string {
  const base = `${fmtQty(quantity)} ${unit}`.trim();
  return packSize && packSize !== 1 ? `${base} = ${fmtQty(piecesOf(quantity, packSize))} ${partUnit}`.trim() : base;
}

type Fillable = {
  unitOfMeasure: string; packSize: number; quantity: number | ''; unitPrice: number | ''; discountPercent: number | ''; supplierPartNumber: string;
  leadTimeDays: number | null; notCatalogue: boolean;
};

/** The line's own price per piece, sent with a refill and kept when the catalogue has no price for it; none when the line has no price. */
export function perPieceOf(l: { unitPrice: number | ''; packSize: number }): number | null {
  return l.unitPrice === '' || !(l.unitPrice > 0) ? null : Math.round((l.unitPrice / (l.packSize || 1)) * 10000) / 10000;
}

/**
 * A line refilled from the supplier's catalogue (0.18.0): the catalogue price and discount when they apply, else the server's price per piece × pack
 * with the line's own discount. Says whether anything the buyer sees changed.
 */
export function refill<L extends Fillable>(line: L, f: FilledLine): { line: L; changed: boolean } {
  const next: L = {
    ...line, unitOfMeasure: f.unitOfMeasure, packSize: f.packSize, quantity: f.quantity, unitPrice: f.unitPrice,
    discountPercent: f.cataloguePrice ? f.discountPercent : line.discountPercent, supplierPartNumber: f.supplierPartNumber ?? '',
    leadTimeDays: f.leadTimeDays ?? null, notCatalogue: f.packSize !== 1 && !f.cataloguePrice,
  };
  const keys = ['unitOfMeasure', 'packSize', 'quantity', 'unitPrice', 'discountPercent', 'supplierPartNumber'] as const;
  return { line: next, changed: keys.some((k) => next[k] !== line[k]) };
}
