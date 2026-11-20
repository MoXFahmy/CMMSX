import { describe, expect, it } from 'vitest';
import { withEmail } from './notifications';

describe('switching e-mails on and off', () => {
  it('changes only the one e-mail, starting from what is saved now', () => {
    expect(withEmail(['a', 'b'], 'a', true)).toEqual(['b']);
    expect(withEmail(['b'], 'a', false)).toEqual(['b', 'a']);
    expect(withEmail(['a'], 'a', false)).toEqual(['a']);
    expect(withEmail(undefined, 'a', false)).toEqual(['a']);
  });
});
