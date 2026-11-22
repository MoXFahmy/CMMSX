import { describe, expect, it, vi } from 'vitest';
import { biometricAvailable, registerPush, shouldLock, verifyBiometric, LOCK_AFTER_MS, PUSH_MARK } from './native';

const fakeBridge = (plugins: Record<string, unknown>) => ({ isNativePlatform: () => true, Plugins: plugins });

describe('native features', () => {
  it('locks on opening and after five minutes away, only when switched on', () => {
    expect(shouldLock(false, null, 0)).toBe(false);
    expect(shouldLock(true, null, 0)).toBe(true);
    expect(shouldLock(true, 1_000, 1_000 + LOCK_AFTER_MS - 1)).toBe(false);
    expect(shouldLock(true, 1_000, 1_000 + LOCK_AFTER_MS)).toBe(true);
  });

  it('knows when fingerprint unlock is there', async () => {
    expect(await biometricAvailable(undefined)).toBe(false);
    expect(await biometricAvailable(fakeBridge({}))).toBe(false);
    expect(await biometricAvailable(fakeBridge({ NativeBiometric: { isAvailable: async () => ({ isAvailable: true }) } }))).toBe(true);
    expect(await biometricAvailable(fakeBridge({ NativeBiometric: { isAvailable: async () => { throw new Error('no sensor'); } } }))).toBe(false);
  });

  it('verifies or refuses', async () => {
    expect(await verifyBiometric('Unlock', fakeBridge({ NativeBiometric: { verifyIdentity: async () => undefined } }))).toBe(true);
    expect(await verifyBiometric('Unlock', fakeBridge({ NativeBiometric: { verifyIdentity: async () => { throw new Error('cancelled'); } } }))).toBe(false);
  });

  const appUa = `Mozilla/5.0 (Linux; Android 15) 4CCMMS-Android/0.11.0 ${PUSH_MARK}`;

  it('registers for push and hands over the token and taps', async () => {
    expect(await registerPush(() => {}, () => {}, undefined, appUa)).toBe('unsupported');
    const denied = { checkPermissions: async () => ({ receive: 'prompt' }), requestPermissions: async () => ({ receive: 'denied' }) };
    expect(await registerPush(() => {}, () => {}, fakeBridge({ PushNotifications: denied }), appUa)).toBe('denied');

    const listeners: Record<string, (x: unknown) => void> = {};
    const push = {
      checkPermissions: async () => ({ receive: 'granted' }),
      addListener: async (name: string, fn: (x: unknown) => void) => { listeners[name] = fn; return { remove: async () => {} }; },
      register: async () => { listeners.registration?.({ value: 'tok-123' }); },
    };
    const onToken = vi.fn(); const onOpen = vi.fn();
    expect(await registerPush(onToken, onOpen, fakeBridge({ PushNotifications: push }), appUa)).toBe('registered');
    expect(onToken).toHaveBeenCalledWith('tok-123');
    listeners.pushNotificationActionPerformed?.({ notification: { data: { link: '/tech/jobs/1' } } });
    expect(onOpen).toHaveBeenCalledWith('/tech/jobs/1');
  });

  it('leaves push alone in an app built without Firebase, where registering would crash it', async () => {
    const checkPermissions = vi.fn(async () => ({ receive: 'granted' }));
    const push = { checkPermissions, addListener: vi.fn(), register: vi.fn() };
    expect(await registerPush(() => {}, () => {}, fakeBridge({ PushNotifications: push }), 'Mozilla/5.0 (Linux; Android 15) 4CCMMS-Android/0.11.0')).toBe('unsupported');
    expect(checkPermissions).not.toHaveBeenCalled();
    expect(push.register).not.toHaveBeenCalled();
  });
});
