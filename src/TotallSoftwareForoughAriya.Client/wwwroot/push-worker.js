/* Shared by development and published workers. No access token is stored here. v3 */
const pushOwnerCache = 'push-owner-v1';
const pushOwnerUrl = new URL('/__push_owner__', self.location.origin).href;
async function readPushOwner() {
    const cache = await caches.open(pushOwnerCache);
    const response = await cache.match(pushOwnerUrl);
    return response ? (await response.json()).userId : 0;
}
function safePushUrl(value) {
    try {
        const url = new URL(value || '/', self.location.origin);
        const secureOrLocal = url.protocol === 'https:' || (url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(self.location.hostname));
        return url.origin === self.location.origin && !url.username && !url.password && secureOrLocal ? url.href : self.location.origin + '/';
    } catch (_) { return self.location.origin + '/'; }
}
self.addEventListener('message', event => {
    if (event.data?.type !== 'push-owner') return;
    event.waitUntil((async () => {
        try {
            if (!event.source?.url || new URL(event.source.url).origin !== self.location.origin) return;
            const userId = Number.isInteger(event.data.userId) && event.data.userId > 0 ? event.data.userId : 0;
            const cache = await caches.open(pushOwnerCache);
            await cache.put(pushOwnerUrl, new Response(JSON.stringify({ userId }), { headers: { 'Content-Type': 'application/json' } }));
            for (const notification of await self.registration.getNotifications())
                if (!userId || notification.data?.userId !== userId) notification.close();
            event.ports[0]?.postMessage({ ok: true });
        } catch (_) { event.ports[0]?.postMessage({ ok: false }); }
    })());
});
self.addEventListener('push', event => {
    event.waitUntil((async () => {
        let data;
        try { data = event.data?.json(); } catch (_) { return; }
        if (!data || !data.userId || await readPushOwner() !== data.userId) return;
        if (data.expiresAtUtc && Date.parse(data.expiresAtUtc) <= Date.now()) return;
        await self.registration.showNotification(data.title || 'اعلان جدید', {
            body: data.body || '', icon: '/icon-192.png', badge: '/icon-192.png',
            tag: data.tag || 'inv-generic', renotify: false, dir: 'rtl', lang: 'fa', vibrate: [100, 50, 100],
            // اعلان تا زمانی که کاربر آن را ببیند/کلیک کند روی صفحه بماند (به‌جای محو شدن سریع خودکار)
            requireInteraction: true, silent: false,
            data: { url: safePushUrl(data.link), userId: data.userId }
        });
    })());
});
self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil((async () => {
        if (await readPushOwner() !== event.notification.data?.userId) return;
        const url = safePushUrl(event.notification.data.url);
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const same = windows.find(c => c.url === url);
        if (same) return same.focus();
        const app = windows.find(c => new URL(c.url).origin === self.location.origin && !new URL(c.url).pathname.startsWith('/radis-hr/'));
        if (app) {
            try { const navigated = await app.navigate(url); if (navigated) return navigated.focus(); } catch (_) { }
        }
        return self.clients.openWindow(url);
    })());
});
self.addEventListener('pushsubscriptionchange', event => {
    // A worker has no login token. Reconciliation happens on the next authenticated app open.
    event.waitUntil(self.clients.matchAll({ type: 'window' }).then(windows => {
        for (const client of windows) client.postMessage({ type: 'push-subscription-changed' });
    }));
});
