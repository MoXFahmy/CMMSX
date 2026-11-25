import type { AccessWindow } from '@/api/types';

export const MAX_WINDOWS = 10;
export const NOTE_LENGTH = 200;

/** A picker's local "YYYY-MM-DD HH:mm:ss" (or Date) as an ISO instant; a cleared picker is an empty value, never a made-up date. */
export const windowDate = (v: string | Date | null): string => (v ? new Date(typeof v === 'string' ? v.replace(' ', 'T') : v).toISOString() : '');

/** What is wrong with a list of access windows (an i18n key), or null; the same rules as the server. */
export function windowProblem(list: AccessWindow[]): string | null {
  if (list.length > MAX_WINDOWS) return 'accessWindows.tooMany';
  if (list.some((w) => !w.start || !w.end)) return 'accessWindows.fillDates';
  if (list.some((w) => new Date(w.end).getTime() <= new Date(w.start).getTime())) return 'accessWindows.endAfterStart';
  if (list.some((w) => (w.note?.trim().length ?? 0) > NOTE_LENGTH)) return 'accessWindows.noteTooLong';
  return null;
}

/** How far <zone>'s clock is ahead of UTC at <instant>, in milliseconds. */
function offsetMs(instant: number, zone: string): number {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-US', {
    timeZone: zone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(new Date(instant)).map((p) => [p.type, p.value]));
  const asUtc = Date.UTC(+parts.year, +parts.month - 1, +parts.day, +parts.hour % 24, +parts.minute, +parts.second);
  return asUtc - Math.floor(instant / 1000) * 1000;
}

/** Midnight at the start of <day> (YYYY-MM-DD) in <zone>, as a UTC instant. */
function midnight(day: string, zone: string): number {
  const [y, m, d] = day.split('-').map(Number);
  const guess = Date.UTC(y, m - 1, d);
  return guess - offsetMs(guess - offsetMs(guess, zone), zone);
}

/** True when there are windows and none of them touches <day> in <zone> (work planned outside every window). */
export function isOutside(list: AccessWindow[] | null | undefined, day: string, zone = 'UTC'): boolean {
  if (!list || list.length === 0) return false;
  const start = midnight(day, zone);
  const [y, m, d] = day.split('-').map(Number);
  const next = new Date(Date.UTC(y, m - 1, d + 1)).toISOString().slice(0, 10);
  const end = midnight(next, zone);
  return !list.some((w) => new Date(w.start).getTime() < end && new Date(w.end).getTime() > start);
}
