import { describe, expect, it } from 'vitest';
import { draftState, nextStep, passedIds, primaryAction, toRows, toSaveBody } from './translations';

const draft = {
  field: 'name',
  items: [
    { id: 'a', text: 'Pump A', draft: 'المضخة أ' },
    { id: 'b', text: 'Pump B', draft: 'المضخة ب' },
  ],
  couldNot: 1,
  remaining: 3,
  couldNotIds: ['c'],
  skippedLeft: 0,
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
    expect(toRows({ field: null, items: [], couldNot: 0, remaining: 0, couldNotIds: [], skippedLeft: 0 })).toEqual([]);
  });

  it('passes the unticked rows on, so they are not asked for again', () => {
    const rows = [
      { id: 'a', text: 'Pump A', ar: 'x', keep: true },
      { id: 'b', text: 'Pump B', ar: 'y', keep: false },
      { id: 'c', text: 'Pump C', ar: 'z', keep: false },
    ];
    expect(passedIds(rows)).toEqual(['b', 'c']);
    expect(passedIds([])).toEqual([]);
  });

  it('tells the three shapes of a batch apart', () => {
    expect(draftState({ field: null, items: [], couldNot: 0, remaining: 0, couldNotIds: [], skippedLeft: 0 })).toBe('done');
    expect(draftState({ field: 'name', items: [], couldNot: 2, remaining: 5, couldNotIds: ['a', 'b'], skippedLeft: 0 })).toBe('nothing');
    expect(draftState(draft)).toBe('review');
  });
});

// I1 (0.29.2 final review): after a save or a skip the window always asks for the next batch, so the last batch of one field moves on
// to the list's next field (assets: name, then notes to technician) instead of closing; nothing kept is a skip, not a disabled Save.
describe('AI window: what happens after a batch (0.29.2)', () => {
  const rows = (keep: [string, string, boolean][]) => keep.map(([id, ar, k]) => ({ id, text: id, ar, keep: k }));

  it('skips a batch whose rows are all unticked: no save, every row passed on, then the next batch', () => {
    const step = nextStep('assets', draft, rows([['a', 'x', false], ['b', 'y', false]]), ['z']);
    expect(step).toEqual({ kind: 'next', save: null, skip: ['z', 'a', 'b', 'c'] });
    expect(primaryAction(rows([['a', 'x', false], ['b', 'y', false]]), draft)).toBe('skip');
  });

  it('saves the kept rows and passes on the unticked and the blank ones', () => {
    const rs = rows([['a', ' المضخة أ ', true], ['b', 'y', false], ['d', '  ', true]]);
    expect(nextStep('assets', draft, rs, [])).toEqual({
      kind: 'next', save: { list: 'assets', field: 'name', items: [{ id: 'a', ar: 'المضخة أ' }] }, skip: ['b', 'd', 'c'],
    });
    expect(primaryAction(rs, draft)).toBe('saveNext');
  });

  it("goes on after a field's last batch: another field may be left", () => {
    const last = { ...draft, remaining: 0, couldNot: 0, couldNotIds: [] };
    const kept = rows([['a', 'x', true], ['b', 'y', true]]);
    expect(nextStep('assets', last, kept, ['p'])).toEqual({
      kind: 'next', save: { list: 'assets', field: 'name', items: [{ id: 'a', ar: 'x' }, { id: 'b', ar: 'y' }] }, skip: ['p'],
    });
    expect(primaryAction(kept, last)).toBe('save');
    // Unticking all of the last batch still goes on (it used to close the window).
    expect(nextStep('assets', last, rows([['a', 'x', false]]), [])).toEqual({ kind: 'next', save: null, skip: ['a'] });
    // A last batch the assistant drafted nothing for: "Skip these and go on" goes on too.
    const nothing = { ...draft, items: [], remaining: 0, couldNot: 2, couldNotIds: ['a', 'b'] };
    expect(nextStep('assets', nothing, [], ['p'])).toEqual({ kind: 'next', save: null, skip: ['p', 'a', 'b'] });
  });

  it('closes when nothing is left to draft', () => {
    const done = { field: null, items: [], couldNot: 0, remaining: 0, couldNotIds: [], skippedLeft: 0 };
    expect(nextStep('assets', done, [], ['a'])).toEqual({ kind: 'close' });
    expect(nextStep('assets', null, [], [])).toEqual({ kind: 'close' });
  });
});
