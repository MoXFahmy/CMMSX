import { describe, expect, it } from 'vitest';
import { DEFAULT_SECURITY, securityProblems, signInStep } from './signIn';

describe('sign-in answers (0.25.0)', () => {
  it('signInStep maps each answer', () => {
    expect(signInStep(200, {})).toBe('done');
    expect(signInStep(403, { code: 'password-expired' })).toBe('renew');
    expect(signInStep(403, { code: 'password-rules' })).toBe('renew');
    expect(signInStep(409, { code: 'other-session' })).toBe('other-session');
    expect(signInStep(401, {})).toBe('error');
    expect(signInStep(403, {})).toBe('error');
    expect(signInStep(409, null)).toBe('error');
  });

  it('securityProblems checks the ranges', () => {
    expect(securityProblems(DEFAULT_SECURITY)).toEqual({});
    const p = securityProblems({ ...DEFAULT_SECURITY, passwordMinLength: 7, passwordExpiryDays: 10 });
    expect(p.passwordMinLength).toBe('security.minLengthRange');
    expect(p.passwordExpiryDays).toBe('security.expiryRange');
    expect(securityProblems({ ...DEFAULT_SECURITY, passwordHistory: 25 }).passwordHistory).toBe('security.historyRange');
    expect(securityProblems({ ...DEFAULT_SECURITY, activityRetentionMonths: 6 }).activityRetentionMonths).toBe('security.retentionRange');
    expect(securityProblems({ ...DEFAULT_SECURITY, passwordMinLength: 64, passwordExpiryDays: 730, passwordHistory: 24, activityRetentionMonths: 120 })).toEqual({});
    expect(securityProblems({ ...DEFAULT_SECURITY, passwordExpiryDays: 0, activityRetentionMonths: null })).toEqual({});
  });
});
