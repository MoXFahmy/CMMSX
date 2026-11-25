import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import { feedbackLoadFailure } from './RequestFeedbackPage';

describe('requester answer page failures (0.29.0)', () => {
  it('a 429 keeps the server title so the person knows to wait', () => {
    expect(feedbackLoadFailure(new ApiError(429, 'Too many attempts from your network. Try again in 3 minutes.')))
      .toEqual({ state: 'limited', message: 'Too many attempts from your network. Try again in 3 minutes.' });
  });
  it('anything else is the "link no longer works" page', () => {
    expect(feedbackLoadFailure(new ApiError(404, 'x'))).toEqual({ state: 'gone', message: null });
    expect(feedbackLoadFailure(new Error('boom'))).toEqual({ state: 'gone', message: null });
  });
});
