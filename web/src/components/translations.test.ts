import { describe, expect, it } from 'vitest';
import { toRows, toSaveBody } from './translations';

const draft = {
  field: 'name',
  items: [
    { id: 'a', text: 'Pump A', draft: 'المضخة أ' },
    { id: 'b', text: 'Pump B', draft: 'المضخة ب' },
  ],
  couldNot: 1,
  remaining: 3,
};

describe('AI draft review (0.28.0)', () => {
  it('ticks every drafted row and keeps its texts', () => {
    expect(toRows(draft)).toEqual([
      { id: 'a', text: 'Pump A', ar: 'المضخة أ', keep: true },
      { id: 'b', text: 'Pump B', ar: 'المضخة ب', keep: true },
    ]);
  });

  it('saves only ticked, non-blank rows, trimmed', () => {
    const rows = [
      { id: 'a', text: 'Pump A', ar: '  المضخة أ  ', keep: true },
      { id: 'b', text: 'Pump B', ar: 'المضخة ب', keep: false },
      { id: 'c', text: 'Pump C', ar: '   ', keep: true },
    ];
    expect(toSaveBody('assets', 'name', rows)).toEqual({ list: 'assets', field: 'name', items: [{ id: 'a', ar: 'المضخة أ' }] });
  });

  it('has nothing to review when the list has every Arabic text', () => {
    expect(toRows({ field: null, items: [], couldNot: 0, remaining: 0 })).toEqual([]);
  });
});
