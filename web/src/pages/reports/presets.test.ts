import { describe, expect, it } from 'vitest';
import { localToday, PRESETS } from './presets';

describe('report presets', () => {
  it('end on the organization\'s today, not the browser\'s', () => {
    const now = new Date('2026-11-25T23:30:00Z'); // still the 25th in UTC, already the 26th at UTC+2
    expect(localToday('UTC', now)).toBe('2026-11-25');
    expect(localToday('Etc/GMT-2', now)).toBe('2026-11-26');
    const ytd = PRESETS.find((p) => p.key === 'ytd')!.range(localToday('Etc/GMT-2', now));
    expect(ytd).toEqual(['2026-01-01', '2026-11-26']);
  });
});
