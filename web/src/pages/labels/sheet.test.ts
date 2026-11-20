import { describe, expect, it } from 'vitest';
import { LABELS_PER_SHEET, SHEET, sheets } from './sheet';

describe('label sheets', () => {
  it('fills A4 label stock: 3 × 8 labels of 70 × 37 mm, edge to edge', () => {
    expect(LABELS_PER_SHEET).toBe(24);
    expect(SHEET.columns * SHEET.labelWidthMm).toBeLessThanOrEqual(210);
    expect(SHEET.rows * SHEET.labelHeightMm + SHEET.marginTopMm).toBeLessThanOrEqual(297);
  });

  it('starts a new page every 24 labels', () => {
    const labels = Array.from({ length: 50 }, (_, i) => i);
    expect(sheets(labels).map((p) => p.length)).toEqual([24, 24, 2]);
    expect(sheets([])).toEqual([]);
  });
});
