import { describe, expect, it } from 'vitest';
import { answered, failed, formatValue, levelOf, outOfRange, roundProgress, type PointLimits, type StepState } from './measure';

const sat: PointLimits = { unit: '°C', decimals: 1, validMin: 0, validMax: 60, alarmLow: 5, alertLow: 10, target: 22, alertHigh: 28, alarmHigh: 32 };
const step = (s: Partial<StepState>): StepState => ({ type: 'Check', answer: null, value: null, textValue: null, level: null, ...s });

describe('levels (as the server)', () => {
  it('goes from alarm to alarm, a threshold counting', () => {
    expect([4, 5, 10, 22, 28, 33].map((v) => levelOf(sat, v))).toEqual(['AlarmLow', 'AlarmLow', 'AlertLow', 'Normal', 'AlertHigh', 'AlarmHigh']);
  });
  it('skips levels that are not set', () => {
    const p: PointLimits = { alarmHigh: 50 };
    expect([levelOf(p, 49), levelOf(p, 50), levelOf(p, -100)]).toEqual(['Normal', 'AlarmHigh', 'Normal']);
  });
  it('knows impossible values', () => {
    expect([outOfRange(sat, 61), outOfRange(sat, -1), outOfRange(sat, 60), outOfRange({}, 1e9)]).toEqual([true, true, false, false]);
  });
});

describe('steps', () => {
  it('are answered by type', () => {
    expect(answered(step({}))).toBe(false);
    expect(answered(step({ answer: 'NotApplicable' }))).toBe(true);
    expect(answered(step({ type: 'Number', value: 0 }))).toBe(true);
    expect(answered(step({ type: 'Text', textValue: ' ' }))).toBe(false);
    expect(answered(step({ type: 'Text', textValue: 'worn' }))).toBe(true);
  });
  it('fail on Not OK or a level', () => {
    expect(failed(step({ answer: 'NotOk' }))).toBe(true);
    expect(failed(step({ type: 'Number', value: 40, level: 'AlarmHigh' }))).toBe(true);
    expect(failed(step({ type: 'Number', value: 20, level: 'Normal' }))).toBe(false);
  });
  it('colour a round asset', () => {
    expect(roundProgress([step({ answer: 'Ok' }), step({})])).toBeNull();
    expect(roundProgress([step({ answer: 'Ok' }), step({ type: 'Number', value: 20, level: 'Normal' })])).toBe('green');
    expect(roundProgress([step({ answer: 'NotOk' }), step({})])).toBe('red');
  });
  it('format a value with its unit', () => {
    expect(formatValue(31.04, sat)).toBe('31.0 °C');
    expect(formatValue(7, { decimals: 0 })).toBe('7');
  });
});
