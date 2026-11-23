/**
 * Dictation (0.15.0): the phone's own speech recognition through the Capacitor speech plugin compiled into the APK. Audio never
 * leaves the phone. In a browser or an older app the plugin isn't there and the microphone simply doesn't show.
 */
import { bridge, type Bridge } from './native';

export async function speechAvailable(b: Bridge | undefined = bridge()): Promise<boolean> {
  const p = b?.Plugins?.SpeechRecognition;
  if (!p?.available) return false;
  try { return !!(await p.available())?.available; } catch { return false; }
}

/** Listens once in the app language; the words heard, or null when the microphone is refused, nothing was heard or it was cancelled. */
export async function dictate(lang: 'ar' | 'en', b: Bridge | undefined = bridge()): Promise<string | null> {
  const p = b?.Plugins?.SpeechRecognition;
  if (!p?.start) return null;
  try {
    const perm = p.requestPermissions ? await p.requestPermissions() : { speechRecognition: 'granted' };
    if (perm?.speechRecognition !== 'granted') return null;
    const r = await p.start({ language: lang === 'ar' ? 'ar-EG' : 'en-US', maxResults: 1, partialResults: false, popup: true });
    const heard = (r?.matches?.[0] ?? '').trim();
    return heard || null;
  } catch { return null; }
}
