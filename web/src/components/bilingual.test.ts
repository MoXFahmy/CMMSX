import { describe, expect, it } from 'vitest';
import { pick, pickNote, safetyText } from './bilingual';

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

describe('safetyText (0.29.2: safety notes are never hidden, whichever language they were written in)', () => {
  it("shows the reader's language when it has text", () => {
    expect(safetyText('Isolate first', 'افصل أولاً', 'ar')).toBe('افصل أولاً');
    expect(safetyText('Isolate first', 'افصل أولاً', 'en')).toBe('Isolate first');
  });

  it("falls back to the other language when the reader's has none", () => {
    expect(safetyText(null, 'احذر', 'en')).toBe('احذر');
    expect(safetyText(undefined, 'احذر', 'en')).toBe('احذر');
    expect(safetyText('Isolate first', null, 'ar')).toBe('Isolate first');
    expect(safetyText('Isolate first', '  ', 'ar-EG')).toBe('Isolate first');
  });

  it('Blank_main_safety_note_falls_back', () => {
    expect(safetyText('', 'احذر', 'en')).toBe('احذر');
    expect(safetyText('   ', 'احذر', 'en')).toBe('احذر');
    expect(safetyText('', 'احذر', 'ar')).toBe('احذر');
  });

  it('is null when both are missing or blank', () => {
    expect(safetyText(null, null, 'en')).toBeNull();
    expect(safetyText('', ' ', 'ar')).toBeNull();
  });
});
