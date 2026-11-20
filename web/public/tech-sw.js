// Service worker for the technician view. Its scope is /tech, so office pages are never served from the phone's copy.
// It keeps every file of the current release on the device, so /tech opens with no connection (the Android app starts
// there). /api is never cached. A new release is picked up the next time the app opens with a connection.
const PREFIX = 'cmms-tech-';
const META = 'cmms-tech-meta';
const NAVIGATION_TIMEOUT_MS = 4000;

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const res = await fetch('/tech-precache.json', { cache: 'no-store' });
    const { version, files } = await res.json();
    const cache = await caches.open(PREFIX + version);
    await cache.addAll(files.map((f) => '/' + f));
    await (await caches.open(META)).put('/current', new Response(version));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const current = await (await (await caches.open(META)).match('/current'))?.text();
    for (const name of await caches.keys())
      if (name.startsWith(PREFIX) && name !== PREFIX + current) await caches.delete(name);
    await self.clients.claim();
  })());
});

/** The saved copy of the app's page (the single-page app answers every /tech address with it). */
const savedPage = async () => (await caches.match('/index.html')) ?? Response.error();

self.addEventListener('fetch', (event) => {
  const req = event.request;
  const url = new URL(req.url);
  if (req.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;
  if (req.mode === 'navigate') {
    // Pages: the network when it answers in time, otherwise the saved app (no signal, or too slow to wait for).
    event.respondWith(new Promise((resolve) => {
      let done = false;
      const fallback = () => { if (!done) { done = true; resolve(savedPage()); } };
      const timer = setTimeout(fallback, NAVIGATION_TIMEOUT_MS);
      fetch(req).then((res) => { if (!done) { done = true; clearTimeout(timer); resolve(res); } }, () => { clearTimeout(timer); fallback(); });
    }));
    return;
  }
  // Files of the release (hashed names, never change): the saved copy first.
  event.respondWith(caches.match(req).then((hit) => hit ?? fetch(req)));
});
