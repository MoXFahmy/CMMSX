import type { DayName } from '@/api/types';

/** One day of a weekly working pattern (0.9.0): start and end as "HH:mm"; "24:00" is midnight at the end. */
export type WorkingDay = { day: DayName; start: string; end: string };

/** Sunday first, like the server's day numbers. */
export const WEEK: DayName[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export function minutes(text: string | null | undefined): number | null {
  const t = (text ?? '').trim();
  if (t === '24:00') return 24 * 60;
  const m = /^(\d{1,2}):(\d{2})$/.exec(t);
  if (!m) return null;
  const [h, min] = [Number(m[1]), Number(m[2])];
  return h < 24 && min < 60 ? h * 60 + min : null;
}

/** Hours worked on a day (0 on a day off). */
export function hoursOn(pattern: WorkingDay[], day: DayName): number {
  const d = pattern.find((x) => x.day === day);
  const [s, e] = [minutes(d?.start), minutes(d?.end)];
  return d && s !== null && e !== null && e > s ? Math.round(((e - s) / 60) * 100) / 100 : 0;
}

/** The i18n key of what's wrong with a pattern, or null when it can be saved (the server's checks, in the same order). */
export function problem(pattern: WorkingDay[]): string | null {
  if (pattern.some((d) => minutes(d.start) === null || minutes(d.end) === null)) return 'hours.timeFormat';
  if (new Set(pattern.map((d) => d.day)).size !== pattern.length) return 'hours.oncePerDay';
  if (pattern.some((d) => (minutes(d.end) ?? 0) <= (minutes(d.start) ?? 0))) return 'hours.endAfterStart';
  return null;
}
