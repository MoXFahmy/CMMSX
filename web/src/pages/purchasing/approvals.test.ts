import { describe, expect, it } from 'vitest';
import { exampleSentence, levelsFor, printsSignatures, stepState, whoIsNext, type ApprovalPanel, type LevelRow } from './approvals';

const L = (name: string, minAmount: number, siteId: string | null = null, orderTypeId: string | null = null): LevelRow =>
  ({ id: name, name, minAmount, siteId, orderTypeId, rule: 'Any', approvers: [] } as unknown as LevelRow);

const t = (key: string, params?: Record<string, unknown>) => `${key}|${JSON.stringify(params ?? {})}`;
const fmt = (n: number) => n.toLocaleString('en-US');

describe('approval levels', () => {
  it('adds levels up from the lowest, at or above the minimum', () => {
    const org = [L('L100k', 100000), L('L0', 0), L('L10k', 10000)];
    expect(levelsFor(org, 's1', null, 10000).map((l) => l.name)).toEqual(['L0', 'L10k']);
    expect(levelsFor(org, 's1', null, 9999.99).map((l) => l.name)).toEqual(['L0']);
    expect(levelsFor(org, 's1', null, 250000).map((l) => l.name)).toEqual(['L0', 'L10k', 'L100k']);
  });

  it('lets a site set replace the organization set and type levels replace all-types levels', () => {
    const all = [L('Org0', 0), L('Org10k', 10000), L('A0', 0, 'sA'), L('Cap0', 0, null, 'cap')];
    expect(levelsFor(all, 'sA', null, 50000).map((l) => l.name)).toEqual(['A0']);
    expect(levelsFor(all, 'sB', null, 50000).map((l) => l.name)).toEqual(['Org0', 'Org10k']);
    expect(levelsFor(all, 'sB', 'cap', 50000).map((l) => l.name)).toEqual(['Cap0']);
    expect(levelsFor(all, 'sB', 'parts', 50000).map((l) => l.name)).toEqual(['Org0', 'Org10k']);
  });

  it('writes the example sentence for the settings page', () => {
    const org = [L('Maintenance manager', 0), L('Plant manager', 10000)];
    expect(exampleSentence(t, org, 50000, fmt)).toBe('approvals.example|{"amount":"50,000","names":"Maintenance manager → Plant manager"}');
    expect(exampleSentence(t, [L('Board', 5000)], 500, fmt)).toBe('approvals.exampleNone|{"amount":"500"}');
  });

  it('names who is next on the waiting step', () => {
    const panel = {
      round: 1, waiting: true, nobodyCanSign: false, earlierRounds: [],
      steps: [
        { step: 1, name: 'L1', minAmount: 0, rule: 'Any', status: 'Approved', approvers: [{ id: 'a', name: 'A', signed: true, approved: true, canSign: false }] },
        { step: 2, name: 'L2', minAmount: 10000, rule: 'All', status: 'Waiting', approvers: [
          { id: 'b', name: 'B', signed: true, approved: true, canSign: false }, { id: 'c', name: 'C', signed: false, canSign: true }] },
      ],
    } as unknown as ApprovalPanel;
    expect(whoIsNext(panel)).toEqual(['C']);
    expect(stepState(panel.steps[0])).toBe('done');
    expect(stepState(panel.steps[1])).toBe('waiting');
    expect(stepState({ ...panel.steps[1], status: 'Stopped' })).toBe('stopped');
    expect(stepState({ ...panel.steps[1], status: 'Pending' })).toBe('pending');
  });

  it('keeps the organization levels for other types when a site only has a type level', () => {
    const all = [L('Org0', 0), L('Org10k', 10000), L('ACap', 50000, 'sA', 'cap')];
    expect(levelsFor(all, 'sA', 'parts', 20000).map((l) => l.name)).toEqual(['Org0', 'Org10k']);
    expect(levelsFor(all, 'sA', 'cap', 60000).map((l) => l.name)).toEqual(['ACap']);
    expect(levelsFor([L('Org0', 0), L('OrgCap', 0, null, 'cap'), L('A0', 0, 'sA')], 'sA', 'cap', 10).map((l) => l.name)).toEqual(['A0']);
  });

  it('prints signatures only when the current round has some, else the approver line', () => {
    const signed = { round: 1, waiting: false, nobodyCanSign: false, earlierRounds: [],
      steps: [{ step: 1, name: 'L1', minAmount: 0, rule: 'Any', status: 'Approved', approvers: [{ id: 'a', name: 'A', signed: true, approved: true, canSign: false }] }] } as unknown as ApprovalPanel;
    expect(printsSignatures(signed)).toBe(true);
    expect(printsSignatures({ ...signed, round: 2, steps: [], earlierRounds: [{ round: 1, steps: signed.steps }] })).toBe(false);
    expect(printsSignatures(null)).toBe(false);
    expect(printsSignatures(undefined)).toBe(false);
  });
});
