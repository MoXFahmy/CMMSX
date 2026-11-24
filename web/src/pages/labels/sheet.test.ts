import { describe, expect, it } from 'vitest';
import { LABELS_PER_SHEET, SHEET, SHEETS, sheetFor, sheets } from './sheet';

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

describe('label sizes (0.23.0)', () => {
  it("keeps today's sheet as the built-in size", () => {
    expect(sheetFor(null)).toEqual(SHEETS['a4-3x8']);
    expect(SHEETS['a4-3x8']).toMatchObject({ columns: 3, rows: 8, labelWidthMm: 70, labelHeightMm: 37, pageWidthMm: 210, pageHeightMm: 297 });
  });

  it('has the other A4 sheets and the label-printer sizes', () => {
    expect(SHEETS['a4-2x7']).toMatchObject({ columns: 2, rows: 7, labelWidthMm: 99.1, labelHeightMm: 38.1 });
    expect(SHEETS['a4-3x7']).toMatchObject({ columns: 3, rows: 7, labelWidthMm: 63.5, labelHeightMm: 38.1 });
    expect(SHEETS['roll-50x25']).toMatchObject({ columns: 1, rows: 1, labelWidthMm: 50, labelHeightMm: 25, pageWidthMm: 50, pageHeightMm: 25 });
    expect(SHEETS['roll-100x50']).toMatchObject({ columns: 1, rows: 1, pageWidthMm: 100, pageHeightMm: 50 });
  });

  it('every size fits its paper', () => {
    for (const s of Object.values(SHEETS)) {
      expect(s.columns * s.labelWidthMm + (s.columns - 1) * s.gapMm).toBeLessThanOrEqual(s.pageWidthMm);
      expect(s.marginTopMm + s.rows * s.labelHeightMm).toBeLessThanOrEqual(s.pageHeightMm);
    }
  });

  it('puts one label on each page of a label printer', () => {
    expect(sheets([1, 2, 3], 'roll-50x25').map((p) => p.length)).toEqual([1, 1, 1]);
    expect(sheets(Array.from({ length: 15 }, (_, i) => i), 'a4-2x7').map((p) => p.length)).toEqual([14, 1]);
  });
});
