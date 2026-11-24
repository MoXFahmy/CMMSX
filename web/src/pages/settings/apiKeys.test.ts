import { describe, expect, it } from 'vitest';
import { keyLabel, keyStatusColor } from './apiKeys';

describe('API keys (0.26.0)', () => {
  it('keyLabel shows the first characters only', () => {
    expect(keyLabel('4f9k2LmQ')).toBe('cmx_4f9k2LmQ…');
  });
  it('keyStatusColor tells working keys apart', () => {
    expect(keyStatusColor('Active')).toBe('teal');
    expect(keyStatusColor('Expired')).toBe('gray');
    expect(keyStatusColor('Revoked')).toBe('gray');
    expect(keyStatusColor('AccountInactive')).toBe('yellow');
  });
});
