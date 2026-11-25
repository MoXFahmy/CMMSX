import type { SecuritySettings } from '@/api/types';

/** What a sign-in answer asks for next (0.25.0): done, a new password first, confirming another device's session, or an error to show. */
export type SignInStep = 'done' | 'renew' | 'other-session' | 'unconfirmed' | 'error';

function codeOf(body: unknown): string | undefined {
  return body && typeof body === 'object' && 'code' in body && typeof (body as { code: unknown }).code === 'string' ? (body as { code: string }).code : undefined;
}

export function signInStep(status: number, body: unknown): SignInStep {
  if (status >= 200 && status < 300) return 'done';
  const code = codeOf(body);
  if (status === 403 && (code === 'password-expired' || code === 'password-rules')) return 'renew';
  if (status === 403 && code === 'email-unconfirmed') return 'unconfirmed';
  if (status === 409 && code === 'other-session') return 'other-session';
  return 'error';
}

/** Set when a signed-in request was refused because the password expired; the sign-in page then says why. */
export const PASSWORD_EXPIRED_FLAG = 'cmx.passwordExpired';

/** Remembers, for the sign-in page, that the password expired (the phone view and the web both go there to renew it). */
export function markPasswordExpired() {
  try { sessionStorage.setItem(PASSWORD_EXPIRED_FLAG, '1'); } catch { /* storage off: the server still asks at sign-in */ }
}

export const DEFAULT_SECURITY: SecuritySettings = {
  passwordMinLength: 8, passwordExpiryDays: 0, passwordHistory: 0, oneSessionPerUser: false, activityRetentionMonths: null,
};

const whole = (n: unknown): n is number => typeof n === 'number' && Number.isInteger(n);

/** The server's ranges, as message keys, so the page can say what's wrong before saving. */
export function securityProblems(s: SecuritySettings): Partial<Record<keyof SecuritySettings, string>> {
  const p: Partial<Record<keyof SecuritySettings, string>> = {};
  if (!whole(s.passwordMinLength) || s.passwordMinLength < 8 || s.passwordMinLength > 64) p.passwordMinLength = 'security.minLengthRange';
  if (!whole(s.passwordExpiryDays) || (s.passwordExpiryDays !== 0 && (s.passwordExpiryDays < 30 || s.passwordExpiryDays > 730))) p.passwordExpiryDays = 'security.expiryRange';
  if (!whole(s.passwordHistory) || s.passwordHistory < 0 || s.passwordHistory > 24) p.passwordHistory = 'security.historyRange';
  const r = s.activityRetentionMonths;
  if (r !== null && (!whole(r) || r < 12 || r > 120)) p.activityRetentionMonths = 'security.retentionRange';
  return p;
}
