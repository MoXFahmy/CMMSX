import { describe, expect, it, vi } from 'vitest';
import { draftState, inSession, leftOutCount, nextStep, passedIds, primaryAction, toRows, toSaveBody } from './translations';

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
  const name = (...ids: string[]) => ids.map((id) => ({ field: 'name', id }));

  it('skips a batch whose rows are all unticked: no save, every row passed on, then the next batch', () => {
    const step = nextStep('assets', draft, rows([['a', 'x', false], ['b', 'y', false]]), name('z'));
    expect(step).toEqual({ kind: 'next', save: null, skip: name('z', 'a', 'b', 'c') });
    expect(primaryAction(rows([['a', 'x', false], ['b', 'y', false]]), draft)).toBe('skip');
  });

  it('saves the kept rows and passes on the unticked and the blank ones', () => {
    const rs = rows([['a', ' المضخة أ ', true], ['b', 'y', false], ['d', '  ', true]]);
    expect(nextStep('assets', draft, rs, [])).toEqual({
      kind: 'next', save: { list: 'assets', field: 'name', items: [{ id: 'a', ar: 'المضخة أ' }] }, skip: name('b', 'd', 'c'),
    });
    expect(primaryAction(rs, draft)).toBe('saveNext');
  });

  it("goes on after a field's last batch: another field may be left", () => {
    const last = { ...draft, remaining: 0, couldNot: 0, couldNotIds: [] };
    const kept = rows([['a', 'x', true], ['b', 'y', true]]);
    expect(nextStep('assets', last, kept, name('p'))).toEqual({
      kind: 'next', save: { list: 'assets', field: 'name', items: [{ id: 'a', ar: 'x' }, { id: 'b', ar: 'y' }] }, skip: name('p'),
    });
    expect(primaryAction(kept, last)).toBe('save');
    // Unticking all of the last batch still goes on (it used to close the window).
    expect(nextStep('assets', last, rows([['a', 'x', false]]), [])).toEqual({ kind: 'next', save: null, skip: name('a') });
    // A last batch the assistant drafted nothing for: "Skip these and go on" goes on too.
    const nothing = { ...draft, items: [], remaining: 0, couldNot: 2, couldNotIds: ['a', 'b'] };
    expect(nextStep('assets', nothing, [], name('p'))).toEqual({ kind: 'next', save: null, skip: name('p', 'a', 'b') });
  });

  it('closes when nothing is left to draft', () => {
    const done = { field: null, items: [], couldNot: 0, remaining: 0, couldNotIds: [], skippedLeft: 0 };
    expect(nextStep('assets', done, [], name('a'))).toEqual({ kind: 'close' });
    expect(nextStep('assets', null, [], [])).toEqual({ kind: 'close' });
  });
});

// N1 (0.29.2 last review): one record has several texts (an asset's name and notes to technician share its id), so what is passed on
// names its field: a record passed on for its name is still offered for its notes, and "left out" counts texts, not records.
describe('AI window: passed-on texts are per field (0.29.2)', () => {
  const notes = { field: 'notesToTechnician', items: [{ id: 'a', text: 'Isolate first', draft: 'اعزل أولًا' }], couldNot: 0, remaining: 0, couldNotIds: [], skippedLeft: 1 };

  it("keeps an earlier field's entry and adds this field's own for the same record", () => {
    const step = nextStep('assets', notes, [{ id: 'a', text: 'Isolate first', ar: 'اعزل أولًا', keep: false }], [{ field: 'name', id: 'a' }]);
    expect(step).toEqual({ kind: 'next', save: null, skip: [{ field: 'name', id: 'a' }, { field: 'notesToTechnician', id: 'a' }] });
  });

  it('names the batch field for the rows the assistant could not draft', () => {
    const nothing = { ...notes, items: [], couldNot: 1, couldNotIds: ['b'] };
    expect(nextStep('assets', nothing, [], [])).toEqual({ kind: 'next', save: null, skip: [{ field: 'notesToTechnician', id: 'b' }] });
  });

  it('lists each text once', () => {
    const step = nextStep('assets', { ...notes, couldNotIds: ['a'] }, [{ id: 'a', text: 'x', ar: '', keep: true }], [{ field: 'notesToTechnician', id: 'a' }]);
    expect(step).toEqual({ kind: 'next', save: null, skip: [{ field: 'notesToTechnician', id: 'a' }] });
  });

  it('counts the texts left out, so one record left out on two fields counts twice', () => {
    const rs = [{ id: 'a', text: 'Isolate first', ar: 'اعزل أولًا', keep: false }];
    expect(leftOutCount('assets', notes, rs, [{ field: 'name', id: 'a' }])).toBe(2);
    expect(leftOutCount('assets', notes, [{ ...rs[0], keep: true }], [{ field: 'name', id: 'a' }])).toBe(1);
    // With nothing left to draft, the earlier passes are what was left out.
    expect(leftOutCount('assets', { ...notes, field: null, items: [] }, [], [{ field: 'name', id: 'a' }, { field: 'notesToTechnician', id: 'a' }])).toBe(2);
  });
});

// N3 (0.29.2 last review): a batch request belongs to the window it was asked from. Once that window is closed, its answer is dropped
// (it must not reopen the window) and so is its failure (no error toast for a window that is gone).
describe('AI window: requests after closing (0.29.2)', () => {
  /** A request that settles when told to, and a session the test can bump (as closing the window does). */
  function pending() {
    let settle: { ok: (v: string) => void; fail: (e: unknown) => void } = { ok: () => {}, fail: () => {} };
    const request = () => new Promise<string>((ok, fail) => { settle = { ok, fail }; });
    return { request, settle: () => settle, session: { current: 0 } };
  }

  it('drops a failure that arrives after the window was closed', async () => {
    const p = pending();
    const done = vi.fn(); const failed = vi.fn();
    const run = inSession(p.session, p.request, done, failed);
    p.session.current++; // closed while the request was out
    p.settle().fail(new Error('used up'));
    await run;
    expect(failed).not.toHaveBeenCalled();
    expect(done).not.toHaveBeenCalled();
  });

  it('drops an answer that arrives after the window was closed', async () => {
    const p = pending();
    const done = vi.fn(); const failed = vi.fn();
    const run = inSession(p.session, p.request, done, failed);
    p.session.current++;
    p.settle().ok('batch');
    await run;
    expect(done).not.toHaveBeenCalled();
  });

  it('reports the answer or the failure while the window is open', async () => {
    const ok = pending(); const done = vi.fn();
    const first = inSession(ok.session, ok.request, done, vi.fn());
    ok.settle().ok('batch');
    await first;
    expect(done).toHaveBeenCalledWith('batch');
    const bad = pending(); const failed = vi.fn(); const error = new Error('used up');
    const second = inSession(bad.session, bad.request, vi.fn(), failed);
    bad.settle().fail(error);
    await second;
    expect(failed).toHaveBeenCalledWith(error);
  });
});
