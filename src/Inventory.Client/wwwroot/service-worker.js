// Development always uses the network, including after this origin previously hosted a publish.
// Without skipWaiting/claim, an old published worker can keep serving an obsolete login/index
// until every tab is closed, even though the new development server is already running.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.filter(key => key.startsWith('offline-cache-')).map(key => caches.delete(key)));
    // Only discard generated app-shell caches; leave document/user data and push support intact.
    await self.clients.claim();
})()));
self.addEventListener('fetch', () => { });

self.importScripts('./push-worker.js');
