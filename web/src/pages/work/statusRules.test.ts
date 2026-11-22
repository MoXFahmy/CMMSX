import { describe, expect, it } from 'vitest';
import { noteRequired } from './statusRules';

describe('status note rules', () => {
  it('asks for a note only where the organization wants one', () => {
    expect(noteRequired([], 'Open', 'OnHold')).toBeNull();
    expect(noteRequired(['hold'], 'Open', 'OnHold')).toBe('hold');
    expect(noteRequired(['reopen'], 'Completed', 'InProgress')).toBe('reopen');
    expect(noteRequired(['reopen'], 'Cancelled', 'Open')).toBe('reopen');
    expect(noteRequired(['reopen'], 'Open', 'InProgress')).toBeNull();
    expect(noteRequired(['cancel'], 'InProgress', 'Cancelled')).toBe('cancel');
    expect(noteRequired(['hold', 'cancel'], 'Open', 'Completed')).toBeNull();
  });
});
