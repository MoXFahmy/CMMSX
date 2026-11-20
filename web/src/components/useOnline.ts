import { useSyncExternalStore } from 'react';

const subscribe = (changed: () => void) => {
  window.addEventListener('online', changed);
  window.addEventListener('offline', changed);
  return () => { window.removeEventListener('online', changed); window.removeEventListener('offline', changed); };
};

/** False when the device says it has no network (airplane mode, no Wi-Fi or data). True can still mean a weak signal. */
export const useOnline = () => useSyncExternalStore(subscribe, () => navigator.onLine, () => true);
