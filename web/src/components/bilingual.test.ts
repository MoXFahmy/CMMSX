import { describe, expect, it } from 'vitest';
import { pick, pickNote } from './bilingual';

describe('pick (0.28.0: a stored main text and Arabic twin, shown in the reader\'s language)', () => {
  it('shows the Arabic text to Arabic readers', () => {
    expect(pick('Pump', 'مضخة', 'ar')).toBe('مضخة');
  });

  it('shows the main text to English readers', () => {
    expect(pick('Pump', 'مضخة', 'en')).toBe('Pump');
  });

  it('falls back to the main text when the Arabic one is blank or missing', () => {
    expect(pick('Pump', '  ', 'ar')).toBe('Pump');
    expect(pick('Pump', null, 'ar-EG')).toBe('Pump');
    expect(pick('Pump', undefined, 'ar')).toBe('Pump');
  });

  it('treats any Arabic locale as Arabic', () => {
    expect(pick('Pump', 'مضخة', 'ar-EG')).toBe('مضخة');
  });

  it('handles notes that may be missing', () => {
    expect(pickNote(null, null, 'ar')).toBeNull();
    expect(pickNote('Isolate first', 'افصل أولاً', 'ar')).toBe('افصل أولاً');
    expect(pickNote(null, 'افصل أولاً', 'en')).toBeNull();
  });
});
