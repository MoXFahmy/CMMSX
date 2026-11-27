import { QueryClient } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { put } from '@/api/client';
import i18n from './i18n';
import { applyLanguage, directionOf, numberLocale, pickLanguage, switchLanguage } from './language';
import { fmtDate, fmtNumber, fromNow } from '@/components/utils';
import { span } from '@/components/sla';
import { fmtDateTime, fmtMoney, fmtPrice, hoursText } from '@/components/utils';

vi.mock('@/api/client', () => ({ put: vi.fn() }));

afterEach(() => { applyLanguage('en'); vi.mocked(put).mockReset(); });

describe('language', () => {
  it('Arabic reads right to left, English left to right', () => {
    expect(directionOf('ar')).toBe('rtl');
    expect(directionOf('en')).toBe('ltr');
  });

  it('a saved or offered language is used only if CMX has it', () => {
    expect(pickLanguage('ar')).toBe('ar');
    expect(pickLanguage('fr')).toBe('en');
    expect(pickLanguage(null)).toBe('en');
  });

  it('durations use Arabic unit letters in Arabic', () => {
    applyLanguage('ar');
    expect(span(2.5 * 3600_000)).toBe('2 س 30 د');
    expect(span(26 * 3600_000)).toBe('1 ي 2 س');
    expect(hoursText(3.5)).toBe('3.5 س');
    applyLanguage('en');
    expect(span(26 * 3600_000)).toBe('1 d 2 h');
    expect(hoursText(3.5)).toBe('3.5 h');
  });

  it('Arabic amounts never mix Latin letters into a currency sign, which right to left would scramble', () => {
    applyLanguage('ar');
    expect(fmtMoney(128293, 'USD')).toMatch(/^\u200f?128,293\s+USD$/);
    expect(fmtPrice(12.5, 'USD')).toMatch(/12\.50\s+USD$/);
    expect(fmtMoney(1000, 'EGP')).toContain('ج.م');
    applyLanguage('en');
    expect(fmtMoney(128293, 'USD')).toBe('$128,293');
  });

  it('Arabic dates with a time use the Arabic comma', () => {
    applyLanguage('ar');
    expect(fmtDateTime('2026-11-21T10:30:00')).toBe('21 نوفمبر 2026، 10:30');
    applyLanguage('en');
    expect(fmtDateTime('2026-11-21T10:30:00')).toBe('21 Nov 2026, 10:30');
  });

  it('Arabic dates use Arabic month names with the digits 0-9', () => {
    applyLanguage('ar');
    expect(fmtDate('2026-11-21')).toBe('21 نوفمبر 2026');
    expect(fromNow(new Date(Date.now() - 3 * 3600_000).toISOString())).toMatch(/^منذ .*3/);
  });

  it('Arabic numbers keep the digits 0-9', () => {
    applyLanguage('ar');
    expect(numberLocale()).toBe('ar-EG-u-nu-latn');
    expect(fmtNumber(1234.5)).toMatch(/^1.?234.5$/);
    applyLanguage('en');
    expect(numberLocale()).toBeUndefined();
  });
});

describe('switching language (0.29.2)', () => {
  it('refreshes every query, not only the profile', async () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries').mockResolvedValue();
    vi.mocked(put).mockResolvedValue(undefined);
    await switchLanguage('ar', qc, true);
    expect(put).toHaveBeenCalledWith('/auth/me/language', { language: 'ar' });
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith();
  });

  it('still refreshes everything when saving to the account fails, and reports the failure', async () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries').mockResolvedValue();
    vi.mocked(put).mockRejectedValue(new Error('offline'));
    await expect(switchLanguage('ar', qc, true)).rejects.toThrow('offline');
    expect(i18n.language).toBe('ar'); // the screen did change
    expect(spy).toHaveBeenCalledWith();
  });

  it('on the sign-in page nothing is saved but everything is refreshed', async () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries').mockResolvedValue();
    await switchLanguage('ar', qc, false);
    expect(put).not.toHaveBeenCalled();
    expect(spy).toHaveBeenCalledWith();
  });
});
