import { describe, expect, it } from 'vitest';
import { packText, packsFor, perPieceOf, piecesOf, refill } from './packs';

describe('packs', () => {
  it('rounds up to whole packs and the minimum', () => {
    expect(packsFor(20, 100, 1)).toBe(1);
    expect(packsFor(250, 100, 1)).toBe(3);
    expect(packsFor(20, 100, 2)).toBe(2);
    expect(packsFor(5, 2.5, 1)).toBe(2);
  });
  it('counts pieces', () => expect(piecesOf(1.5, 100)).toBe(150));
  it('writes the pack with its pieces', () => {
    expect(packText(1, 'Box of 100', 100, 'EA')).toBe('1 Box of 100 = 100 EA');
    expect(packText(3, 'EA', 1, 'EA')).toBe('3 EA');
  });
});

describe('refilling a line from the catalogue', () => {
  const line = { unitOfMeasure: 'EA', packSize: 1, quantity: 20, unitPrice: 0.7, discountPercent: 5, supplierPartNumber: '', leadTimeDays: null, notCatalogue: false };
  const filled = { partId: 'p', unitOfMeasure: 'Box of 100', packSize: 100, quantity: 1, unitPrice: 45, discountPercent: 2, supplierPartNumber: '6205-2RS', leadTimeDays: 10, cataloguePrice: true };
  it('sends the line\'s own price per piece, none when it has no price', () => {
    expect(perPieceOf({ unitPrice: 45, packSize: 100 })).toBe(0.45);
    expect(perPieceOf({ unitPrice: '', packSize: 1 })).toBeNull();
    expect(perPieceOf({ unitPrice: 0, packSize: 1 })).toBeNull();
  });
  it('takes the catalogue price and discount', () => {
    const r = refill(line, filled);
    expect(r.line).toMatchObject({ unitOfMeasure: 'Box of 100', packSize: 100, quantity: 1, unitPrice: 45, discountPercent: 2, supplierPartNumber: '6205-2RS', notCatalogue: false });
    expect(r.changed).toBe(true);
  });
  it('keeps the line\'s discount when the catalogue price does not apply', () => {
    const r = refill(line, { ...filled, unitPrice: 70, discountPercent: 0, cataloguePrice: false });
    expect(r.line).toMatchObject({ unitPrice: 70, discountPercent: 5, notCatalogue: true });
  });
  it('says nothing changed when the line already matches', () => {
    expect(refill(line, { ...filled, unitOfMeasure: 'EA', packSize: 1, quantity: 20, unitPrice: 0.7, discountPercent: 0, supplierPartNumber: null, leadTimeDays: null, cataloguePrice: false }).changed).toBe(false);
  });
});
