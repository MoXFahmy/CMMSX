import { describe, expect, it } from 'vitest';
import { builtInLayout, blocksInOrder } from './layouts';
import { copyLayout, moveBlock, toggleBlock, toggleField } from './editor';

const keys = (l: ReturnType<typeof builtInLayout>) => blocksInOrder(l, true).map((b) => b.key);

describe('print layout editor', () => {
  it('copies a layout as a new one without conditions', () => {
    const from = { ...builtInLayout('job-card'), isDefault: true, siteId: 's1', kindKey: 'Corrective' };
    const c = copyLayout(from, 'My card');
    expect(c).toMatchObject({ id: '', name: 'My card', isDefault: false, siteId: null, kindKey: null });
    expect(c.blocks).toEqual(from.blocks);
    expect(c.blocks).not.toBe(from.blocks);
  });

  it('moves blocks and keeps the header first', () => {
    const l = builtInLayout('request');
    expect(keys(moveBlock(l, 'progress', -1))).toEqual(['header', 'details', 'description', 'progress', 'access-windows', 'terms']);
    expect(keys(moveBlock(l, 'terms', 1))).toEqual(keys(l));
    expect(keys(moveBlock(l, 'details', -1))).toEqual(keys(l)); // can't pass the header
    expect(keys(moveBlock(l, 'header', 1))).toEqual(keys(l));
  });

  it('hides and shows blocks, but not a block that carries fixed fields', () => {
    const l = builtInLayout('job-card');
    const hidden = toggleBlock(l, 'labour');
    expect(blocksInOrder(hidden).map((b) => b.key)).not.toContain('labour');
    expect(blocksInOrder(toggleBlock(hidden, 'labour')).map((b) => b.key)).toContain('labour');
    const po = builtInLayout('purchase-order');
    expect(toggleBlock(po, 'lines')).toEqual(po);
  });

  it("ticks fields but can't untick a fixed one", () => {
    const po = builtInLayout('purchase-order');
    const noUnit = toggleField(po, 'lines', 'unit');
    expect(noUnit.blocks.find((b) => b.key === 'lines')!.fields).not.toContain('unit');
    expect(toggleField(noUnit, 'lines', 'unit').blocks.find((b) => b.key === 'lines')!.fields).toContain('unit');
    expect(toggleField(po, 'lines', 'description')).toEqual(po);
  });
});
