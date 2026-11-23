import { describe, expect, it, vi } from 'vitest';
import { dictate, speechAvailable } from './speech';

const bridge = (plugin: Record<string, unknown>) => ({ isNativePlatform: () => true, Plugins: { SpeechRecognition: plugin } });

describe('dictation on the phone', () => {
  it('is there only with the speech plugin on a phone that can listen', async () => {
    expect(await speechAvailable(undefined)).toBe(false);
    expect(await speechAvailable({ isNativePlatform: () => true, Plugins: {} })).toBe(false);
    expect(await speechAvailable(bridge({ available: async () => ({ available: true }) }))).toBe(true);
    expect(await speechAvailable(bridge({ available: async () => ({ available: false }) }))).toBe(false);
    expect(await speechAvailable(bridge({ available: async () => { throw new Error('no'); } }))).toBe(false);
  });

  it('listens in the app language and gives back what was heard', async () => {
    const start = vi.fn(async () => ({ matches: ['تم تغيير الجلدة'] }));
    const b = bridge({ requestPermissions: async () => ({ speechRecognition: 'granted' }), start });
    expect(await dictate('ar', b)).toBe('تم تغيير الجلدة');
    expect(start).toHaveBeenCalledWith(expect.objectContaining({ language: 'ar-EG', maxResults: 1, partialResults: false, popup: true }));
    await dictate('en', b);
    expect(start).toHaveBeenLastCalledWith(expect.objectContaining({ language: 'en-US' }));
  });

  it('gives nothing when the microphone is refused or listening is cancelled', async () => {
    expect(await dictate('en', bridge({ requestPermissions: async () => ({ speechRecognition: 'denied' }), start: async () => ({ matches: ['x'] }) }))).toBeNull();
    expect(await dictate('en', bridge({ requestPermissions: async () => ({ speechRecognition: 'granted' }), start: async () => { throw new Error('cancelled'); } }))).toBeNull();
    expect(await dictate('en', bridge({ requestPermissions: async () => ({ speechRecognition: 'granted' }), start: async () => ({ matches: [] }) }))).toBeNull();
    expect(await dictate('en', undefined)).toBeNull();
  });
});
