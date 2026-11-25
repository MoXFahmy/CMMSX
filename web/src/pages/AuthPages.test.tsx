import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHtml } from '@/test/render';

const post = vi.fn();
vi.mock('@/api/client', async (orig) => ({ ...(await orig<typeof import('@/api/client')>()), post: (...a: unknown[]) => post(...a), get: vi.fn() }));

import { ApiError } from '@/api/client';
import { CheckEmailPanel, ConfirmEmailPage, confirmToken, resendConfirmation } from './AuthPages';
import { signInStep } from './auth/signIn';

beforeEach(() => { post.mockReset(); });

describe('e-mail confirmation screens (0.29.0)', () => {
  it('a 403 email-unconfirmed answer is its own sign-in step', () => {
    expect(signInStep(403, { code: 'email-unconfirmed', title: 'Confirm' })).toBe('unconfirmed');
    expect(signInStep(403, {})).toBe('error');
  });

  it('the check-your-e-mail panel names the address and offers the resend button', () => {
    const html = renderHtml(<CheckEmailPanel email="ali@example.com" />);
    expect(html).toContain('Check your e-mail');
    expect(html).toContain('We sent a link to ali@example.com. Open it within 48 hours to start using CMX.');
    expect(html).toContain('Send the link again');
  });

  it('resending posts the typed address, once, to the resend endpoint', async () => {
    post.mockResolvedValue({ ok: true });
    await resendConfirmation('ali@example.com');
    expect(post).toHaveBeenCalledWith('/auth/resend-confirmation', { email: 'ali@example.com' });
  });

  it('the confirm page shows "Confirming" while it waits', () => {
    post.mockReturnValue(new Promise(() => {}));
    expect(renderHtml(<ConfirmEmailPage />)).toContain('Confirming your e-mail');
  });

  it('confirmToken posts the token and reports success', async () => {
    post.mockResolvedValue({ ok: true });
    expect(await confirmToken('x')).toEqual({ ok: true });
    expect(post).toHaveBeenCalledWith('/auth/confirm-email', { token: 'x' });
  });

  it('confirmToken reports an expired link on 400', async () => {
    post.mockRejectedValue(new ApiError(400, 'This link has expired or was already used.'));
    expect(await confirmToken('x')).toEqual({ ok: false, invalid: true, message: 'This link has expired or was already used.' });
  });

  it('confirmToken keeps the server title for too many attempts (429)', async () => {
    post.mockRejectedValue(new ApiError(429, 'Too many attempts from your network. Try again in 5 minutes.'));
    expect(await confirmToken('x')).toEqual({ ok: false, invalid: false, message: 'Too many attempts from your network. Try again in 5 minutes.' });
  });
});
