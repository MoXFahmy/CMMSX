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
