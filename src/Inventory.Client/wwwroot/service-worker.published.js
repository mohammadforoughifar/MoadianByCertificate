// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
// ⚠️ بدون skipWaiting، نسخهٔ جدید بعد از هر پابلیش «در انتظار» می‌ماند و کاربر تا بستن کامل
// همهٔ تب‌ها/PWA همچنان نسخهٔ قدیمیِ کش‌شده را می‌بیند (مثلاً تقویم کاری جدید لود نمی‌شد).
self.addEventListener('install', event => { self.skipWaiting(); event.waitUntil(onInstall(event)); });
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
// مسیر پایه از scope خود سرویس‌ورکر خوانده می‌شود تا نصب زیر Application/Virtual Directory در IIS (مثلاً /erp/) هم درست کار کند.
const base = self.registration ? new URL(self.registration.scope).pathname : "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    // ⚠️ قبلاً cache.addAll با integrity استفاده می‌شد؛ اگر حتی «یک» فایل هنگام پابلیش
    // روی سرور کمی تغییر می‌کرد (فشرده‌سازی/CRLF) کل install بی‌صدا شکست می‌خورد و
    // کاربران برای همیشه روی نسخهٔ قدیمی می‌ماندند. حالا هر فایل جداگانه و بدون
    // شکستن کل نصب کش می‌شود.
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await Promise.all(assetsRequests.map(async request => {
        try {
            const response = await fetch(request);
            if (response.ok) await cache.put(request.url, response);
        } catch {
            // فایل با integrity قابل دریافت نبود — بدون integrity تلاش مجدد
            try {
                const response = await fetch(new Request(request.url, { cache: 'no-cache' }));
                if (response.ok) await cache.put(request.url, response);
            } catch { /* آفلاین‌کش این فایل ممکن نشد؛ از شبکه سرو می‌شود */ }
        }
    }));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    // کنترل فوری همهٔ تب‌های باز — تا بعد از پابلیش، همان لحظه نسخهٔ جدید فعال شود
    await self.clients.claim();
}

const docArchiveApiCache='doc-archive-api-v1';
// مسیر نسبت به محدودهٔ (scope) سرویس‌ورکر: اگر برنامه روی IIS زیر مسیر پایه نصب شود (مثلاً /erp/)،
// «/erp/api/…» هم باید مسیر API شناخته شود؛ وگرنه دانلود/پیش‌نمایش پیوست با index.html جواب داده می‌شود.
// روی ریشهٔ سایت (scope = «/») همان pathname قبلی برمی‌گردد.
const appScopePath = new URL(self.registration ? self.registration.scope : self.location.href).pathname;
const appPath = (url)=> url.pathname.startsWith(appScopePath) ? '/' + url.pathname.slice(appScopePath.length) : url.pathname;

const docArchiveCacheableGet = (url)=> appPath(url).startsWith('/api/doc-archive/') && (
  url.pathname.includes('/folders') || url.pathname.includes('/documents') || url.pathname.includes('/cartable') ||
  url.pathname.includes('/favorites') || url.pathname.includes('/recently-viewed') || url.pathname.includes('/search') ||
  url.pathname.includes('/tags') || url.pathname.includes('/lookups') || url.pathname.includes('/stats') || url.pathname.includes('/expiry-summary')
);

// Background Sync برای صف آفلاین آرشیو
self.addEventListener('sync', event=>{
  if(event.tag==='doc-archive-sync'){
    event.waitUntil((async()=>{
      const clients=await self.clients.matchAll();
      for(const c of clients) c.postMessage({type:'doc-archive-sync'});
    })());
  }
});

async function networkFirstWithCache(request){
  const cache=await caches.open(docArchiveApiCache);
  try{
    const resp=await fetch(request);
    if(resp.ok){
      const clone=resp.clone();
      // فقط GETهای کوچک را کش کن (نه فایل‌های حجیم)
      const ct=resp.headers.get('content-type')||'';
      if(ct.includes('json')){
        cache.put(request, clone).catch(()=>{});
        // محدود نگه داشتن کش (حداکثر 80 ورودی)
        const keys=await cache.keys();
        if(keys.length>80) await cache.delete(keys[0]);
      }
    }
    return resp;
  }catch(e){
    const cached=await cache.match(request);
    if(cached) return cached;
    // پاسخ آفلاین ساختگی برای اینکه UI بتواند پیام مناسب نشان دهد
    return new Response(JSON.stringify({message:'آفلاین - داده از کش', offline:true}), {status:503, headers:{'Content-Type':'application/json'}});
  }
}

async function onFetch(event) {
    const requestUrl = new URL(event.request.url);

    // آرشیو اسناد — GETهای قابل کش با استراتژی NetworkFirst + Cache Fallback (برای PWA آفلاین)
    if(event.request.method==='GET' && docArchiveCacheableGet(requestUrl)){
      return networkFirstWithCache(event.request);
    }

    // ⚠️ مسیرهای داده/فایل (API، SignalR و هر URL دارای access_token) هرگز نباید
    // از کش سرو یا با index.html پاسخ داده شوند: درخواست دانلود/پیش‌نمایش پیوست چت
    // وقتی در تب جدید (یا نوار آدرس) باز می‌شود باید مستقیم به سرور برسد؛ وگرنه
    // PWA صفحهٔ برنامه را نشان می‌دهد و دانلود فقط بعد از Ctrl+Shift+R انجام می‌شود.
    const requestPath = appPath(requestUrl);
    const isDataUrl = requestPath.startsWith('/api/')
        || requestPath.startsWith('/hubs/')
        || requestPath.startsWith('/signalr/')
        || requestUrl.searchParams.has('access_token')
        || requestUrl.searchParams.has('ft');   // توکن کوتاه فایل‌های چت
    if (isDataUrl) {
        return fetch(event.request);
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && requestUrl.origin === self.location.origin
            // RADIS-HR یک SPA مستقل زیر همین Origin است؛ index اصلی نباید جای آن برگردد.
            && !requestPath.startsWith('/radis-hr/')
            && !manifestUrlList.some(url => url === event.request.url);

        // Cache-busting query strings must still resolve to this published version's static asset.
        // APIs/tokens were excluded above; other-origin URLs never match this manifest.
        const staticAsset = manifestUrlList.find(url => {
            const asset = new URL(url);
            return asset.origin === requestUrl.origin && asset.pathname === requestUrl.pathname;
        });
        const request = shouldServeIndexHtml ? 'index.html' : (staticAsset || event.request);
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}

self.importScripts('./push-worker.js');
