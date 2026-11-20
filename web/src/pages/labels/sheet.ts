/** A4 label stock: 3 columns × 8 rows of 70 × 37 mm labels, edge to edge (24 per sheet). */
export const SHEET = { columns: 3, rows: 8, labelWidthMm: 70, labelHeightMm: 37, marginTopMm: 0 } as const;
export const LABELS_PER_SHEET = SHEET.columns * SHEET.rows;

/** Splits the labels into printed pages, so every page starts at the top of a fresh sheet. */
export function sheets<T>(labels: T[]): T[][] {
  const pages: T[][] = [];
  for (let i = 0; i < labels.length; i += LABELS_PER_SHEET) pages.push(labels.slice(i, i + LABELS_PER_SHEET));
  return pages;
}
