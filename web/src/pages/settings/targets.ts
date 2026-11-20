export type Unit = 'h' | 'd';

/** Whole and half days show in days; anything else in hours. */
export const unitOf = (minutes?: number | null): Unit => (minutes && minutes % 720 === 0 ? 'd' : 'h');

/** Minutes shown in a unit (two decimals at most); '' for no target. */
export const inUnit = (minutes: number | null | undefined, unit: Unit): number | '' =>
  minutes ? Math.round((minutes / (unit === 'd' ? 1440 : 60)) * 100) / 100 : '';

/** A typed number in a unit as minutes; null for empty or zero (no target). */
export const toMinutes = (value: number | '', unit: Unit): number | null =>
  value === '' || value <= 0 ? null : Math.round(value * (unit === 'd' ? 1440 : 60));
