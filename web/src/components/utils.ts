import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import { notifications } from '@mantine/notifications';
import type { UseFormReturnType } from '@mantine/form';
import { ApiError } from '@/api/client';

dayjs.extend(relativeTime);

export const fmtDate = (v?: string | null) => (v ? dayjs(v).format('DD MMM YYYY') : '—');
export const fmtDateTime = (v?: string | null) => (v ? dayjs(v).format('DD MMM YYYY, HH:mm') : '—');
export const fromNow = (v?: string | null) => (v ? dayjs(v).fromNow() : '—');
export const fmtMoney = (v?: number | null, currency = 'USD') =>
  v === undefined || v === null ? '—' : new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 0 }).format(v);
/** Money with cents — unit prices, order lines, stock values. */
export const fmtPrice = (v?: number | null, currency = 'USD', digits = 2) =>
  v === undefined || v === null ? '—' : new Intl.NumberFormat(undefined, { style: 'currency', currency, minimumFractionDigits: digits, maximumFractionDigits: digits }).format(v);
export const fmtQty = (v?: number | null) => (v === undefined || v === null ? '—' : new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(v));
export const fmtNumber = (v?: number | null) => (v === undefined || v === null ? '—' : new Intl.NumberFormat().format(v));
export const fmtBytes = (n: number) => (n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1048576).toFixed(1)} MB`);
export const toIsoDate = (d: Date | string | null | undefined) => (d ? dayjs(d).format('YYYY-MM-DD') : null);

export function notifySuccess(message: string) {
  notifications.show({ color: 'teal', message, autoClose: 3000 });
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
