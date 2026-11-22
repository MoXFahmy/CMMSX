import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import { notifications } from '@mantine/notifications';
import type { UseFormReturnType } from '@mantine/form';
import { ApiError } from '@/api/client';
import type { TFunction } from 'i18next';
import { numberLocale } from '@/language';
import i18n from '@/i18n';

dayjs.extend(relativeTime);

export const fmtDate = (v?: string | null) => (v ? dayjs(v).format('DD MMM YYYY') : '—');
/** "21 Nov 2026, 10:30"; Arabic separates the time with the Arabic comma. */
export const dateTimeFormat = (): string => (i18n.language === 'ar' ? 'DD MMM YYYY، HH:mm' : 'DD MMM YYYY, HH:mm');
export const fmtDateTime = (v?: string | null) => (v ? dayjs(v).format(dateTimeFormat()) : '—');
export const fromNow = (v?: string | null) => (v ? dayjs(v).fromNow() : '—');
/**
 * A money formatter for the current language. Right to left, a sign that mixes Latin letters with a symbol ("US$")
 * gets scrambled ("$US"), so Arabic shows the plain code ("USD") then, and keeps Arabic signs such as "ج.م.".
 */
function moneyFormat(currency: string, min: number, max: number): Intl.NumberFormat {
  const options: Intl.NumberFormatOptions = { style: 'currency', currency, minimumFractionDigits: min, maximumFractionDigits: max };
  const symbol = new Intl.NumberFormat(numberLocale(), options).formatToParts(0).find((p) => p.type === 'currency')?.value ?? '';
  const scrambles = i18n.language === 'ar' && /[A-Za-z]/.test(symbol) && symbol !== currency;
  return new Intl.NumberFormat(numberLocale(), scrambles ? { ...options, currencyDisplay: 'code' } : options);
}

export const fmtMoney = (v?: number | null, currency = 'USD') =>
  v === undefined || v === null ? '—' : moneyFormat(currency, 0, 0).format(v);
/** Money with cents — unit prices, order lines, stock values. */
export const fmtPrice = (v?: number | null, currency = 'USD', digits = 2) =>
  v === undefined || v === null ? '—' : moneyFormat(currency, digits, digits).format(v);
export const fmtQty = (v?: number | null) => (v === undefined || v === null ? '—' : new Intl.NumberFormat(numberLocale(), { maximumFractionDigits: 3 }).format(v));
export const fmtNumber = (v?: number | null) => (v === undefined || v === null ? '—' : new Intl.NumberFormat(numberLocale()).format(v));
export const fmtBytes = (n: number) => (n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1048576).toFixed(1)} MB`);
export const toIsoDate = (d: Date | string | null | undefined) => (d ? dayjs(d).format('YYYY-MM-DD') : null);

export function notifySuccess(message: string) {
  notifications.show({ color: 'teal', message, autoClose: 3000 });
}

/** Something worth knowing after a save that still worked (e.g. a PM's assets with no checklist steps). */
export function notifyWarning(message: string) {
  notifications.show({ color: 'yellow', message, autoClose: 8000 });
}

export function notifyError(err: unknown) {
  const message = err instanceof Error ? err.message : 'Something went wrong';
  notifications.show({ color: 'red', title: 'Could not complete the action', message, autoClose: 6000 });
}

/** Maps server validation errors onto form fields; shows a toast for everything else. */
export function handleFormError<T>(form: UseFormReturnType<T>, err: unknown) {
  if (err instanceof ApiError && Object.keys(err.fieldErrors).length) {
    form.setErrors(err.fieldErrors);
    const unknown = Object.keys(err.fieldErrors).filter((k) => !(k in (form.getValues() as object)) && !k.includes('.'));
    if (unknown.length) notifyError(new Error(unknown.map((k) => err.fieldErrors[k]).join(' ')));
  } else notifyError(err);
}

/** "Version 0.0.0", plus "· Android app 0.0.0" when running inside the Android app (it adds 4CCMMS-Android/x.y.z to its user agent). */
export function appVersionLabel(t: TFunction): string {
  const android = /4CCMMS-Android\/(\d+\.\d+\.\d+)/.exec(typeof navigator === 'undefined' ? '' : navigator.userAgent)?.[1];
  const web = t('common.version', { v: __APP_VERSION__ });
  return android ? `${web} · ${t('common.androidApp', { v: android })}` : web;
}

/** "3.5 h" in English, "3.5 س" in Arabic. */
export const hoursText = (hours: number | string): string => `${hours} ${i18n.t('unit.h')}`;
