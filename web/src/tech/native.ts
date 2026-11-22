/**
 * Fingerprint/face unlock and push notifications (0.11.0). Both live in the Android app (Capacitor plugins compiled into the
 * APK); the web app reaches them through the bridge Capacitor puts on window, so on an older app or in a browser they are
 * simply not there and nothing changes.
 */
/* eslint-disable @typescript-eslint/no-explicit-any */
export type Bridge = { isNativePlatform?: () => boolean; Plugins?: Record<string, any> };

export const bridge = (): Bridge | undefined => (typeof window === 'undefined' ? undefined : (window as any).Capacitor);

/** The phone asks for the fingerprint again after this long away from the app. */
export const LOCK_AFTER_MS = 5 * 60 * 1000;
/** "Unlock with fingerprint or face" is on (kept on the phone). */
export const BIOMETRIC_KEY = 'cmms.tech.biometric';

/** Lock when the setting is on and the app was just opened (no hiddenAt) or away at least <lockAfter>. */
export function shouldLock(enabled: boolean, hiddenAt: number | null, now: number, lockAfter = LOCK_AFTER_MS): boolean {
  return enabled && (hiddenAt === null || now - hiddenAt >= lockAfter);
}

export async function biometricAvailable(b: Bridge | undefined = bridge()): Promise<boolean> {
  const p = b?.Plugins?.NativeBiometric;
  if (!p?.isAvailable) return false;
  try { return !!(await p.isAvailable())?.isAvailable; } catch { return false; }
}

/** Asks for the fingerprint or face; false when it is refused or cancelled. */
export async function verifyBiometric(reason: string, b: Bridge | undefined = bridge()): Promise<boolean> {
  const p = b?.Plugins?.NativeBiometric;
  if (!p?.verifyIdentity) return false;
  try { await p.verifyIdentity({ reason, title: reason }); return true; } catch { return false; }
}

/**
 * An app built with the Firebase file adds this to its user agent (mobile/capacitor.config.ts). Without Firebase the push
 * plugin is still in the app, but registering would crash it.
 */
export const PUSH_MARK = '4CCMMS-Push';

/**
 * Registers the phone for push: asks Android's permission once, hands the Firebase token to <onToken>, and the link of a
 * tapped notification to <onOpen>.
 */
export async function registerPush(onToken: (token: string) => void, onOpen: (link: string) => void, b: Bridge | undefined = bridge(),
  userAgent: string = typeof navigator === 'undefined' ? '' : navigator.userAgent): Promise<'unsupported' | 'denied' | 'registered'> {
  const p = b?.Plugins?.PushNotifications;
  if (!p?.checkPermissions || !userAgent.split(' ').includes(PUSH_MARK)) return 'unsupported';
  try {
    let perm = await p.checkPermissions();
    if (perm?.receive === 'prompt' || perm?.receive === 'prompt-with-rationale') perm = await p.requestPermissions();
    if (perm?.receive !== 'granted') return 'denied';
    await p.addListener('registration', (t: { value: string }) => onToken(t.value));
    await p.addListener('pushNotificationActionPerformed', (a: { notification?: { data?: { link?: string } } }) => {
      const link = a?.notification?.data?.link;
      if (link) onOpen(link);
    });
    await p.register();
    return 'registered';
  } catch { return 'unsupported'; }
}
