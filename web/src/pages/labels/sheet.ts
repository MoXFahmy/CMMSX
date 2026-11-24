import type { LabelSize } from '@/print/layouts';

/** A label size: its paper, its grid of labels, the gap between columns, the top margin and the QR code's size. */
export type Sheet = {
  pageWidthMm: number; pageHeightMm: number; columns: number; rows: number; labelWidthMm: number; labelHeightMm: number;
  marginTopMm: number; gapMm: number; qrMm: number;
};

/**
 * The label sizes a layout can choose (0.23.0): today's A4 sheet (3 × 8, 70 × 37 mm, edge to edge), two other common A4 sheets,
 * and one label per page for label printers.
 */
export const SHEETS: Record<LabelSize, Sheet> = {
  'a4-3x8': { pageWidthMm: 210, pageHeightMm: 297, columns: 3, rows: 8, labelWidthMm: 70, labelHeightMm: 37, marginTopMm: 0, gapMm: 0, qrMm: 27 },
  'a4-3x7': { pageWidthMm: 210, pageHeightMm: 297, columns: 3, rows: 7, labelWidthMm: 63.5, labelHeightMm: 38.1, marginTopMm: 15.15, gapMm: 2.54, qrMm: 28 },
  'a4-2x7': { pageWidthMm: 210, pageHeightMm: 297, columns: 2, rows: 7, labelWidthMm: 99.1, labelHeightMm: 38.1, marginTopMm: 15.15, gapMm: 2.5, qrMm: 30 },
  'roll-50x25': { pageWidthMm: 50, pageHeightMm: 25, columns: 1, rows: 1, labelWidthMm: 50, labelHeightMm: 25, marginTopMm: 0, gapMm: 0, qrMm: 20 },
  'roll-100x50': { pageWidthMm: 100, pageHeightMm: 50, columns: 1, rows: 1, labelWidthMm: 100, labelHeightMm: 50, marginTopMm: 0, gapMm: 0, qrMm: 40 },
};

/** The size to print: the layout's, or today's sheet. */
export const sheetFor = (size?: LabelSize | null): Sheet => SHEETS[size ?? 'a4-3x8'] ?? SHEETS['a4-3x8'];

/** Today's A4 label stock (kept for older callers). */
export const SHEET = SHEETS['a4-3x8'];
export const LABELS_PER_SHEET = SHEET.columns * SHEET.rows;

/** Splits the labels into printed pages, so every page starts at the top of a fresh sheet. */
export function sheets<T>(labels: T[], size?: LabelSize | null): T[][] {
  const s = sheetFor(size);
  const per = s.columns * s.rows;
  const pages: T[][] = [];
  for (let i = 0; i < labels.length; i += per) pages.push(labels.slice(i, i + per));
  return pages;
}
