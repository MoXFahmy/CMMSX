import { describe, expect, it } from 'vitest';
import { spreadBlocks } from './spread';

describe('spreadBlocks', () => {
  it('splits like the server: earlier parts take the extra assets', () => {
    expect(spreadBlocks(10, 3)).toEqual([4, 3, 3]);
    expect(spreadBlocks(12, 12)).toEqual(Array(12).fill(1));
    expect(spreadBlocks(5, 0)).toEqual([5]);
  });
});
