import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import en from '@/locales/en.json';
import { answered, failed, formatValue, levelOf, outOfRange, printedStep, rangeMessage, roundProgress, toggleAnswer, tooBig, type PointLimits, type StepState } from './measure';

/** English, as the app shows it. */
const t = ((key: string, values?: Record<string, unknown>) => {
  const text = key.split('.').reduce<unknown>((o, k) => (o as Record<string, unknown>)?.[k], en);
  if (typeof text !== 'string') return key;
  return text.replace(/{{(\w+)}}/g, (_, n: string) => String(values?.[n] ?? ''));
}) as unknown as TFunction;

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

describe('range message', () => {
  it('names the bounds a point has', () => {
    expect(rangeMessage(sat, t)).toBe('Enter a value from 0 to 60 °C.');
    expect(rangeMessage({ ...sat, validMin: null }, t)).toBe('Enter a value of at most 60 °C.');
    expect(rangeMessage({ ...sat, validMax: null, unit: null }, t)).toBe('Enter a value of at least 0.');
  });
});

describe('answer buttons', () => {
  it('clicking the chosen answer again clears it', () => {
    expect(toggleAnswer('Ok', 'Ok')).toEqual({ isDone: false });
    expect(toggleAnswer('Ok', 'NotOk')).toEqual({ answer: 'NotOk' });
    expect(toggleAnswer(null, 'NotApplicable')).toEqual({ answer: 'NotApplicable' });
  });
});

describe('printed step', () => {
  it('prints a failed step as failed, with its answer, value, level, text and reading', () => {
    expect(printedStep({ ...step({ answer: 'NotOk' }), followUp: { number: 'WR-00012' } }, t)).toEqual({ mark: '☒', result: 'Not OK · Follow-up WR-00012' });
    expect(printedStep({ ...step({ answer: 'Ok' }), reading: '3.2 bar' }, t)).toEqual({ mark: '☑', result: 'OK · 3.2 bar' });
    expect(printedStep({ ...step({ type: 'Number', value: 33, level: 'AlarmHigh' }), unit: '°C', decimals: 1 }, t)).toEqual({ mark: '☒', result: '33.0 °C (Alarm high)' });
    expect(printedStep({ ...step({ type: 'Number', value: 21, level: 'Normal' }), unit: '°C', decimals: 1 }, t)).toEqual({ mark: '☑', result: '21.0 °C' });
    expect(printedStep(step({ type: 'Text', textValue: 'Belt worn' }), t)).toEqual({ mark: '☑', result: 'Belt worn' });
    expect(printedStep(step({ type: 'Number' }), t)).toEqual({ mark: '☐', result: '' });
  });

  it('refuses numbers the database columns cannot hold', () => {
    expect(tooBig(99_999_999_999_999)).toBe(false);
    expect(tooBig(-99_999_999_999_999)).toBe(false);
    expect(tooBig(1e15)).toBe(true);
    expect(tooBig(-1e14)).toBe(true);
  });
});
