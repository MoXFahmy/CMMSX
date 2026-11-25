import dayjs from 'dayjs';

/** The organization's today (YYYY-MM-DD) in its time zone, not this browser's clock (0.29.1). */
export function localToday(zone?: string, now: Date = new Date()): string {
  try { return new Intl.DateTimeFormat('en-CA', { timeZone: zone || undefined, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now); }
  catch { return dayjs(now).format('YYYY-MM-DD'); }
}

export const PRESETS: { key: string; range: (todayText: string) => [string, string] }[] = [
  { key: 'thisMonth', range: (d) => [dayjs(d).startOf('month').format('YYYY-MM-DD'), d] },
  { key: 'lastMonth', range: (d) => [dayjs(d).subtract(1, 'month').startOf('month').format('YYYY-MM-DD'), dayjs(d).subtract(1, 'month').endOf('month').format('YYYY-MM-DD')] },
  { key: 'last90', range: (d) => [dayjs(d).subtract(89, 'day').format('YYYY-MM-DD'), d] },
  { key: 'ytd', range: (d) => [dayjs(d).startOf('year').format('YYYY-MM-DD'), d] },
  { key: 'last12', range: (d) => [dayjs(d).subtract(12, 'month').add(1, 'day').format('YYYY-MM-DD'), d] },
  { key: 'lastYear', range: (d) => [dayjs(d).subtract(1, 'year').startOf('year').format('YYYY-MM-DD'), dayjs(d).subtract(1, 'year').endOf('year').format('YYYY-MM-DD')] },
];
