import type { QueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import 'dayjs/locale/ar';
import { put } from './api/client';
import i18n from './i18n';

/** The languages CMX speaks (the server has the same list: Core/Common/Languages.cs). */
export type Language = 'en' | 'ar';
export const LANGUAGES: { code: Language; name: string }[] = [
  { code: 'en', name: 'English' },
  { code: 'ar', name: 'العربية' },
];

const KEY = 'cmx.language';

/** A language CMX has, else English. */
export function pickLanguage(code: string | null | undefined): Language {
  return code === 'ar' || code === 'en' ? code : 'en';
}

export const directionOf = (code: Language): 'rtl' | 'ltr' => (code === 'ar' ? 'rtl' : 'ltr');

/** Locale for numbers and money: Arabic wording with the digits 0-9 (codes and quantities stay readable to everyone). */
export const numberLocale = (): string | undefined => (i18n.language === 'ar' ? 'ar-EG-u-nu-latn' : undefined);

/** The language last used on this device (for the sign-in page and the technician app without signal). */
export function savedLanguage(): Language {
  try { return pickLanguage(localStorage.getItem(KEY)); } catch { return 'en'; }
}

/**
 * Switches wording, dates and page direction. dayjs's Arabic months are used without its digit conversion, so dates read
 * "21 نوفمبر 2026".
 */
export function applyLanguage(code: Language) {
  void i18n.changeLanguage(code);
  dayjs.locale(code);
  if (typeof document !== 'undefined') {
    document.documentElement.lang = code;
    document.documentElement.dir = directionOf(code);
  }
  try { localStorage.setItem(KEY, code); } catch { /* private window: this device just won't remember */ }
}

/**
 * The language switch (0.29.2). The screen changes first; signed in, the choice is saved on the account. Then EVERY query is
 * refetched (lists and pages carry names in the language they were fetched in), also when saving failed, because the screen
 * language has changed either way. A failed save is thrown after the refresh so the caller can report it.
 */
export async function switchLanguage(code: Language, qc: QueryClient, save: boolean): Promise<void> {
  applyLanguage(code);
  let failure: unknown;
  let failed = false;
  if (save) {
    try { await put('/auth/me/language', { language: code }); } catch (e) { failed = true; failure = e; }
  }
  await qc.invalidateQueries();
  if (failed) throw failure;
}
