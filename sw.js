/* Spawn Row Duel — service worker.
   Goal: card art is downloaded ONCE and served from cache forever, so iterating on the
   game (which only changes the small HTML) never re-downloads the ~14 MB of art.
   - assets/**  -> cache-first  (art/sprites: stable URLs, kept until ART_CACHE is bumped)
   - everything else (HTML/JS) -> network-first (always get the latest build; cache is offline fallback) */
const ART_CACHE = 'srd-art-v1';
const APP_CACHE = 'srd-app-v1';

self.addEventListener('install', (e) => { self.skipWaiting(); });

self.addEventListener('activate', (e) => {
  e.waitUntil((async () => {
    // drop old app caches but KEEP the art cache (that's the whole point — don't redownload art)
    const keys = await caches.keys();
    await Promise.all(keys.filter(k => k !== ART_CACHE && k !== APP_CACHE).map(k => caches.delete(k)));
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET') return;
  let url;
  try { url = new URL(req.url); } catch (_) { return; }
  if (url.origin !== self.location.origin) return; // let cross-origin (fonts) go straight to network

  // Art & sprites: cache-first — fetch once, then serve from cache (no bandwidth on later loads).
  if (/\/assets\//.test(url.pathname)) {
    e.respondWith((async () => {
      const cache = await caches.open(ART_CACHE);
      const hit = await cache.match(req);
      if (hit) return hit;
      try {
        const resp = await fetch(req);
        if (resp && resp.ok) cache.put(req, resp.clone());
        return resp;
      } catch (err) {
        return hit || Response.error();
      }
    })());
    return;
  }

  // HTML / JS / everything else: network-first so each iteration gets the newest build.
  e.respondWith((async () => {
    try {
      const resp = await fetch(req);
      if (resp && resp.ok) { const c = await caches.open(APP_CACHE); c.put(req, resp.clone()); }
      return resp;
    } catch (err) {
      const cached = await caches.match(req);
      return cached || Response.error();
    }
  })());
});
