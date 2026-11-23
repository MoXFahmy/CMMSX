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
