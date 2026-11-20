import dayjs from 'dayjs';
import type { TFunction } from 'i18next';

/** Where a record stands against a deadline (the server works it out; see Sla.State). */
export type SlaState = 'None' | 'OnTrack' | 'DueSoon' | 'Late' | 'Met' | 'Missed';

/** "40 min", "2 h 30 min", "3 h", "1 d 2 h": short and plain. */
export function span(ms: number): string {
  const minutes = Math.max(1, Math.round(Math.abs(ms) / 60_000));
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours < 24) return hours < 3 && rest > 0 ? `${hours} h ${rest} min` : `${hours} h`;
  const days = Math.floor(hours / 24);
  const h = hours % 24;
  return h > 0 ? `${days} d ${h} h` : `${days} d`;
}

/** Deadlines further off than this show their date instead of "due in …". */
const NEAR_MS = 7 * 24 * 3600_000;

/** "due in 3 h", "2 h late", "due 1 Dec 2026", "met", "missed", or '' without a deadline. */
export function deadlineText(deadline: string | null | undefined, state: SlaState, now: Date, t: TFunction): string {
  if (!deadline || state === 'None') return '';
  if (state === 'Met') return t('sla.met');
  if (state === 'Missed') return t('sla.missed');
  const left = new Date(deadline).getTime() - now.getTime();
  if (left < 0) return t('sla.late', { by: span(left) });
  if (left > NEAR_MS) return t('sla.dueOn', { date: dayjs(deadline).format('DD MMM YYYY, HH:mm') });
  return t('sla.dueIn', { when: span(left) });
}

export function slaColor(state: SlaState | null | undefined): string | undefined {
  return state === 'Late' || state === 'Missed' ? 'red' : state === 'DueSoon' ? 'orange' : undefined;
}
