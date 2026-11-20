import dayjs from 'dayjs';
import { fmtDate } from '@/components/utils';

/** "21 Nov 2026, 14:00" when the job is due at a time of day; the date alone when it is due by the end of the day. */
export function dueLabel(dueDate?: string | null, dueAt?: string | null): string | null {
  if (dueAt) {
    const at = dayjs(dueAt);
    return at.hour() === 23 && at.minute() === 59 ? fmtDate(dueAt) : at.format('DD MMM YYYY, HH:mm');
  }
  return dueDate ? fmtDate(dueDate) : null;
}
