/*
 * Offline support. The first visit stores the pages, scripts, pictures and fonts it uses; later visits open at once
 * and solo and hot-seat games work with no network. The page itself is fetched fresh whenever there is a network, so
 * a new version arrives on the next visit. Online play is never touched: sockets and the health check pass through.
 */
const CACHE = 'jericho-__BUILD_ID__';
const SHELL = ['./', 'index.html', 'manifest.webmanifest', 'icon.svg', 'icons/icon-192.png', 'icons/icon-512.png'];

self.addEventListener('install', (event) => {
  self.skipWaiting();
  event.waitUntil(caches.open(CACHE).then((c) => c.addAll(SHELL.map((u) => new URL(u, self.registration.scope).toString()))).catch(() => undefined));
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    for (const k of await caches.keys()) if (k.startsWith('jericho-') && k !== CACHE) await caches.delete(k);
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  // pages of the game server are never stored: a health check, the socket, and a save handed to another device
  if (url.origin !== self.location.origin || url.pathname.endsWith('/healthz') || url.pathname.endsWith('/ws') || url.pathname.endsWith('/sw.js') || url.pathname.includes('/transfer')) return;

  if (req.mode === 'navigate') {
    // the page: the network when there is one (so updates arrive), the stored copy when there is not
    event.respondWith((async () => {
      const cache = await caches.open(CACHE);
      const key = new URL('index.html', self.registration.scope).toString();
      try {
        const res = await fetch(req);
        if (res.ok) cache.put(key, res.clone());
        return res;
      } catch {
        return (await cache.match(key)) || (await cache.match(req, { ignoreSearch: true })) || Response.error();
      }
    })());
    return;
  }

  // everything else this site serves: stored copy first, fetched and stored the first time
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    const hit = await cache.match(req);
    if (hit) return hit;
    const res = await fetch(req);
    if (res.ok && res.status === 200) cache.put(req, res.clone());
    return res;
  })());
});
