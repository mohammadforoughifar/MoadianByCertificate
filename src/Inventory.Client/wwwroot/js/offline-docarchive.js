// PWA آفلاین آرشیو — صف تایید + کش IndexedDB
// نسخه سبک بدون کتابخانه خارجی، با localStorage fallback اگر IndexedDB در دسترس نبود
(function(){
  const DB_NAME='doc-archive-offline-v1';
  const STORE_QUEUE='queue';
  const STORE_CACHE='cache';
  const QUEUE_KEY='docArchiveQueue'; // fallback localStorage
  const CACHE_KEY='docArchiveCache';

  function isOnline(){ return navigator.onLine; }

  // ---------- IndexedDB helpers ----------
  function openDb(){
    return new Promise((resolve, reject)=>{
      if(!('indexedDB' in window)) return resolve(null);
      const req=indexedDB.open(DB_NAME,1);
      req.onupgradeneeded=e=>{
        const db=e.target.result;
        if(!db.objectStoreNames.contains(STORE_QUEUE)) db.createObjectStore(STORE_QUEUE,{keyPath:'id', autoIncrement:true});
        if(!db.objectStoreNames.contains(STORE_CACHE)) db.createObjectStore(STORE_CACHE,{keyPath:'key'});
      };
      req.onsuccess=()=>resolve(req.result);
      req.onerror=()=>resolve(null);
    });
  }

  async function idbAdd(store, value){
    const db=await openDb();
    if(!db) return false;
    return new Promise(res=>{
      try{ const tx=db.transaction(store,'readwrite'); tx.objectStore(store).put(value); tx.oncomplete=()=>res(true); tx.onerror=()=>res(false);}catch{res(false)}
    });
  }
  async function idbGetAll(store){
    const db=await openDb();
    if(!db) return null;
    return new Promise(res=>{
      try{ const tx=db.transaction(store,'readonly'); const req=tx.objectStore(store).getAll(); req.onsuccess=()=>res(req.result||[]); req.onerror=()=>res(null);}catch{res(null)}
    });
  }
  async function idbClear(store){
    const db=await openDb();
    if(!db) return;
    try{ const tx=db.transaction(store,'readwrite'); tx.objectStore(store).clear(); }catch{}
  }
  async function idbDelete(store, key){
    const db=await openDb();
    if(!db) return;
    try{ const tx=db.transaction(store,'readwrite'); tx.objectStore(store).delete(key); }catch{}
  }

  // ---------- Queue (offline approvals) ----------
  function fallbackQueue(){
    try{ return JSON.parse(localStorage.getItem(QUEUE_KEY)||'[]'); }catch{return []}
  }
  function saveFallbackQueue(q){ try{ localStorage.setItem(QUEUE_KEY, JSON.stringify(q)); }catch{} }

  window.docArchiveOffline={
    isOnline,
    async queueApproval(payload){
      const item={ id:Date.now(), type:'approval', payload, createdAt:new Date().toISOString(), deviceInfo:navigator.userAgent.slice(0,120) };
      // try IndexedDB
      const ok=await idbAdd(STORE_QUEUE, item);
      if(!ok){
        const q=fallbackQueue(); q.push(item); saveFallbackQueue(q);
      }
      // also cache for UI badge
      this.updateBadge();
      // try background sync registration
      if('serviceWorker' in navigator && 'SyncManager' in window){
        try{ const reg=await navigator.serviceWorker.ready; await reg.sync.register('doc-archive-sync'); }catch{}
      }
      return item.id;
    },
    async getQueue(){
      const rows=await idbGetAll(STORE_QUEUE);
      if(rows && rows.length!==null) return rows;
      return fallbackQueue();
    },
    async clearQueueItem(id){
      const db=await openDb();
      if(db){ try{ await idbDelete(STORE_QUEUE, id); }catch{} }
      const q=fallbackQueue().filter(x=>x.id!==id); saveFallbackQueue(q);
      this.updateBadge();
    },
    async syncQueue(fetchFn){
      const q=await this.getQueue();
      if(!q || q.length===0) return {synced:0, failed:0};
      let synced=0, failed=0;
      for(const item of [...q]){
        try{
          let ok=false;
          if(fetchFn) ok=await fetchFn(item);
          else {
            // default: POST to approval endpoint via fetch with auth header from localStorage (authSession JSON)
            let token='';
            try{
              const sess=localStorage.getItem('authSession');
              if(sess){ const j=JSON.parse(sess); token=j.Token||j.token||''; }
            }catch{}
            if(!token) token=localStorage.getItem('authToken')|| sessionStorage.getItem('authToken') || '';
            // آدرس API از همان تابع سراسری index.html (پشتیبانی از IIS با مسیر پایه مثل /erp/ و dev server جدا)
            const apiRoot=(typeof window.inventoryApiBase==='function' ? (window.inventoryApiBase(window.location.origin)||'') : '').replace(/\/+$/,'');
            const res=await fetch(apiRoot+'/api/doc-archive/documents/approval',{
              method:'POST',
              headers:{'Content-Type':'application/json', ...(token?{'Authorization':'Bearer '+token}:{})},
              body: JSON.stringify(item.payload)
            });
            ok=res.ok;
            if(!ok && res.status>=400 && res.status<500 && res.status!==408 && res.status!==429) {
              // خطای منطقی سرور (مثلاً قبلاً تایید شده) -> از صف حذف کن ولی failed نشمار
              console.warn('queue item server error, dropping', res.status);
              ok=true; // treat as synced to avoid loop
            }
          }
          if(ok){ await this.clearQueueItem(item.id); synced++; }
          else failed++;
        }catch(e){ failed++; }
      }
      return {synced, failed, remaining:(await this.getQueue()).length};
    },
    async updateBadge(){
      const q=await this.getQueue();
      const n=q? q.length:0;
      const el=document.getElementById('offline-queue-badge');
      if(el) { el.textContent=n>0? n:''; el.style.display=n>0?'inline-block':'none'; }
      // dispatch event for Blazor
      window.dispatchEvent(new CustomEvent('doc-archive-queue-changed',{detail:{count:n}}));
      return n;
    },
    // ---------- simple cache for GETs (fallback) ----------
    async cacheGet(key){
      const rows=await idbGetAll(STORE_CACHE);
      if(rows){ const r=rows.find(x=>x.key===key); if(r) return r.value; }
      try{ const v=localStorage.getItem(CACHE_KEY+'_'+key); return v? JSON.parse(v):null; }catch{return null}
    },
    async cacheSet(key, value){
      const ok=await idbAdd(STORE_CACHE,{key, value, at:Date.now()});
      if(!ok){ try{ localStorage.setItem(CACHE_KEY+'_'+key, JSON.stringify(value)); }catch{} }
    }
  };

  // auto sync when online
  window.addEventListener('online', ()=>{ window.docArchiveOffline.syncQueue().then(r=>{ if(r.synced>0) console.log('offline sync',r); window.docArchiveOffline.updateBadge(); }); });
  // initial badge
  setTimeout(()=>window.docArchiveOffline.updateBadge(), 900);

  // expose for Blazor
  window.docArchiveQueueCount=async()=> (await window.docArchiveOffline.getQueue()).length;
  window.docArchiveSyncNow=async()=> await window.docArchiveOffline.syncQueue();
})();

// ---------------------------------------------------------------------------
// نشانگر آنلاین/آفلاین آرشیو اسناد — مدیریت شنونده‌ها با attach/detach
// (قبلاً هر بار رندر شدن، شنونده‌های window دوباره اضافه می‌شدند و هرگز حذف نمی‌شدند)
// ---------------------------------------------------------------------------
window.docOfflineStatus = window.docOfflineStatus || (function () {
    var h = null;
    return {
        attach: function () {
            if (h) return;
            h = {
                online: function () { try { DotNet.invokeMethodAsync('Inventory.Client', 'DocOfflineStatus_OnlineChanged', true); } catch (e) { } },
                offline: function () { try { DotNet.invokeMethodAsync('Inventory.Client', 'DocOfflineStatus_OnlineChanged', false); } catch (e) { } },
                queue: function (e) { try { DotNet.invokeMethodAsync('Inventory.Client', 'DocOfflineStatus_QueueChanged', e.detail.count); } catch (err) { } }
            };
            window.addEventListener('online', h.online);
            window.addEventListener('offline', h.offline);
            window.addEventListener('doc-archive-queue-changed', h.queue);
        },
        detach: function () {
            if (!h) return;
            try { window.removeEventListener('online', h.online); } catch (e) { }
            try { window.removeEventListener('offline', h.offline); } catch (e) { }
            try { window.removeEventListener('doc-archive-queue-changed', h.queue); } catch (e) { }
            h = null;
        }
    };
})();
