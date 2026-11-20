/** Keeps the app's files on the phone for /tech (see public/tech-sw.js). Office pages are outside its scope. */
export function registerTechWorker(): Promise<ServiceWorkerRegistration | undefined> {
  if (!('serviceWorker' in navigator)) return Promise.resolve(undefined);
  return navigator.serviceWorker.register('/tech-sw.js', { scope: '/tech' }).catch(() => undefined);
}
