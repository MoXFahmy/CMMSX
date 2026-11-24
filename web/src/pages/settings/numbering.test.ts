import { describe, expect, it } from 'vitest';
import { previewNumber } from './numbering';

describe('the next number, as settings show it', () => {
  const now = new Date(2027, 2, 5);
  it('puts the year in when numbers restart each year', () => expect(previewNumber('WO-', 5, true, now)).toBe('WO-2027-00001'));
  it('is the running number otherwise', () => expect(previewNumber('WO-', 5, false, now)).toBe('WO-00001'));
  it('keeps the padding', () => expect(previewNumber('', 3, false, now)).toBe('001'));
});
