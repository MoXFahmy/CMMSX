import { describe, expect, it } from 'vitest';
import { packText, packsFor, piecesOf } from './packs';

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
