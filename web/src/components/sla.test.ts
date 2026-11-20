import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import { deadlineText, slaColor, span } from './sla';

const t = ((key: string, values?: Record<string, unknown>) => (values ? `${key}:${JSON.stringify(values)}` : key)) as unknown as TFunction;
const now = new Date('2026-11-20T10:00:00Z');

describe('deadline text', () => {
  it('says how long is left or how late, in plain units', () => {
    expect(deadlineText('2026-11-20T13:00:00Z', 'OnTrack', now, t)).toBe('sla.dueIn:{"when":"3 h"}');
    expect(deadlineText('2026-11-20T08:00:00Z', 'Late', now, t)).toBe('sla.late:{"by":"2 h"}');
    expect(deadlineText('2026-11-21T12:00:00Z', 'DueSoon', now, t)).toBe('sla.dueIn:{"when":"1 d 2 h"}');
    expect(deadlineText('2026-11-20T10:40:00Z', 'DueSoon', now, t)).toBe('sla.dueIn:{"when":"40 min"}');
  });

  it('shows the date for far-off deadlines, and the outcome once done', () => {
    expect(deadlineText('2026-12-01T10:00:00Z', 'OnTrack', now, t)).toMatch(/^sla\.dueOn:/);
    expect(deadlineText('2026-11-20T09:00:00Z', 'Met', now, t)).toBe('sla.met');
    expect(deadlineText('2026-11-20T09:00:00Z', 'Missed', now, t)).toBe('sla.missed');
    expect(deadlineText(null, 'None', now, t)).toBe('');
  });

  it('spans', () => {
    expect(span(2.5 * 3600_000)).toBe('2 h 30 min');
    expect(span(26 * 3600_000)).toBe('1 d 2 h');
    expect(span(30_000)).toBe('1 min');
  });
});

describe('deadline colour', () => {
  it('red when late or missed, orange when due soon', () => {
    expect(slaColor('Late')).toBe('red');
    expect(slaColor('Missed')).toBe('red');
    expect(slaColor('DueSoon')).toBe('orange');
    expect(slaColor('OnTrack')).toBeUndefined();
    expect(slaColor('Met')).toBeUndefined();
  });
});
