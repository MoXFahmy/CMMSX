import type { TFunction } from 'i18next';
/** Smart checklists (0.13.0) on the web and the phone: the same reading rules as the server's MeasureLevels / StepRules. */
export type MeasureLevel = 'Normal' | 'AlertHigh' | 'AlarmHigh' | 'AlertLow' | 'AlarmLow';
export type StepType = 'Check' | 'Number' | 'Meter' | 'Text';
export type StepAnswer = 'Ok' | 'NotOk' | 'NotApplicable';

export type PointLimits = {
  unit?: string | null; decimals?: number | null; validMin?: number | null; validMax?: number | null;
  alarmLow?: number | null; alertLow?: number | null; target?: number | null; alertHigh?: number | null; alarmHigh?: number | null;
};

export type StepState = { type: StepType; answer?: StepAnswer | null; value?: number | null; textValue?: string | null; level?: MeasureLevel | null };

const set = (x: number | null | undefined): x is number => x !== null && x !== undefined;

/** Alarm wins over alert; a threshold counts (≥ high, ≤ low); unset levels are skipped. */
export function levelOf(p: PointLimits, value: number): MeasureLevel {
  if (set(p.alarmHigh) && value >= p.alarmHigh) return 'AlarmHigh';
  if (set(p.alertHigh) && value >= p.alertHigh) return 'AlertHigh';
  if (set(p.alarmLow) && value <= p.alarmLow) return 'AlarmLow';
  if (set(p.alertLow) && value <= p.alertLow) return 'AlertLow';
  return 'Normal';
}

/** The largest number a step's value or a meter reading can hold (the database columns); the server refuses more (0.29.1). */
export const MAX_VALUE = 99_999_999_999_999;
export const tooBig = (value: number) => Math.abs(value) > MAX_VALUE;

/** A value that can't be right at all (typed 850 for 85.0). */
export const outOfRange = (p: PointLimits, value: number) => (set(p.validMin) && value < p.validMin) || (set(p.validMax) && value > p.validMax);

export const answered = (s: StepState) =>
  s.type === 'Number' || s.type === 'Meter' ? set(s.value) : s.type === 'Text' ? !!s.textValue?.trim() : !!s.answer;

export const failed = (s: StepState) => s.answer === 'NotOk' || (!!s.level && s.level !== 'Normal');

/** A round asset's progress: red when a step failed, green when all are answered, else none. */
export function roundProgress(steps: StepState[]): 'green' | 'red' | null {
  if (steps.some(failed)) return 'red';
  return steps.length > 0 && steps.every(answered) ? 'green' : null;
}

export const formatValue = (value: number, p: PointLimits) => `${value.toFixed(p.decimals ?? 2)}${p.unit ? ` ${p.unit}` : ''}`;

/** Colours for level badges. */
export const levelColor = (l?: MeasureLevel | null) => (l === 'AlarmHigh' || l === 'AlarmLow' ? 'red' : l === 'AlertHigh' || l === 'AlertLow' ? 'orange' : 'teal');

/** Pressing the chosen answer again clears it (0.13.0); another answer replaces it. */
export const toggleAnswer = (current: StepAnswer | null | undefined, clicked: StepAnswer): { answer: StepAnswer } | { isDone: false } =>
  (clicked === current ? { isDone: false } : { answer: clicked });

/** The message for a value outside a point's possible values, naming the bounds the point has. */
export function rangeMessage(p: PointLimits, t: TFunction): string {
  const unit = p.unit ?? '';
  const text = set(p.validMin) && set(p.validMax) ? t('checklist.outOfRange', { min: p.validMin, max: p.validMax, unit })
    : set(p.validMax) ? t('checklist.outOfRangeMax', { max: p.validMax, unit }) : t('checklist.outOfRangeMin', { min: p.validMin ?? '', unit });
  return text.replace(/\s+\.$/, '.');
}

export type PrintableStep = StepState & { unit?: string | null; decimals?: number | null; reading?: string | null; followUp?: { number?: string | null } | null };

/** A step on the printed work order (0.13.0): ☐ open, ☑ answered, ☒ failed; its answer, value with unit and level, text, reading and follow-up. */
export function printedStep(s: PrintableStep, t: TFunction): { mark: string; result: string } {
  const parts: string[] = [];
  if (s.type === 'Check' && s.answer) parts.push(t(`checklist.answers.${s.answer}`));
  if (s.type === 'Number' && set(s.value)) parts.push(formatValue(s.value, s) + (s.level && s.level !== 'Normal' ? ` (${t(`checklist.levels.${s.level}`)})` : ''));
  if (s.type === 'Meter' && set(s.value)) parts.push(`${s.value}${s.unit ? ` ${s.unit}` : ''}`);
  if (s.type === 'Text' && s.textValue) parts.push(s.textValue);
  if (s.reading) parts.push(s.reading);
  if (s.followUp?.number) parts.push(t('checklist.followUp', { number: s.followUp.number }));
  return { mark: !answered(s) ? '☐' : failed(s) ? '☒' : '☑', result: parts.join(' · ') };
}

