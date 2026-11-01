(() => {
    'use strict';
    const optInKey = 'push-opt-in-user';
    const bounded = (promise, ms = 15000) => new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('ارتباط با سرویس اعلان طول کشید؛ دوباره تلاش کنید.')), ms);
        Promise.resolve(promise).then(x => { clearTimeout(timer); resolve(x); }, e => { clearTimeout(timer); reject(e); });
    });
    const decode = text => Uint8Array.from(atob(text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - text.length % 4) % 4)), c => c.charCodeAt(0));
    const keyEquals = (a, b) => a && b && a.length === b.length && a.every((v, i) => v === b[i]);
    const subscriptionData = sub => { const json = sub.toJSON(); return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth }; };
    async function request(root, token, path, data) {
        const response = await bounded(fetch(root.replace(/\/$/, '') + '/api/notifications/' + path, {
            method: data === undefined ? 'GET' : 'POST', cache: 'no-store',
            headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
            ...(data === undefined ? {} : { body: JSON.stringify(data) })
        }));
        let result = {};
        try { result = await response.json(); } catch (_) { }
        if (!response.ok) throw new Error(result.message || (response.status === 401 ? 'نشست شما منقضی شده؛ دوباره وارد شوید.' : 'ارتباط با سرور اعلان برقرار نشد.'));
        return result;
    }
    async function registration() {
        await bounded(navigator.serviceWorker.register(new URL('service-worker.js', document.baseURI), { updateViaCache: 'none' }));
        return await bounded(navigator.serviceWorker.ready);
    }
    async function owner(reg, userId) {
        if (!reg?.active) throw new Error('سرویس اعلان فعال نیست؛ صفحه را تازه کنید.');
        const channel = new MessageChannel();
        try {
            await bounded(new Promise((resolve, reject) => {
                channel.port1.onmessage = e => e.data?.ok ? resolve() : reject(new Error('تنظیم دستگاه ناموفق بود.'));
                reg.active.postMessage({ type: 'push-owner', userId }, [channel.port2]);
            }), 5000);
        } finally { channel.port1.close(); channel.port2.close(); }
    }
    function diagnose() {
        if (!window.isSecureContext) return { supported: false, insecureOrigin: true,
            message: 'اعلان سیستمی فقط روی نشانی امن (HTTPS) فعال می‌شود و اکنون روی HTTP هستید. راه‌حل: در «تنظیمات ← HTTPS و اعلان اندروید» گواهی سامانه را نصب کنید (یک‌بار روی هر دستگاه) و نرم‌افزار را با https:// باز کنید.' };
        if (!('Notification' in window) || !('PushManager' in window) || !('serviceWorker' in navigator))
            return { supported: false, message: 'این مرورگر اعلان پس‌زمینه ندارد؛ از Chrome به‌روز استفاده کنید.' };
        if (Notification.permission === 'denied') return { supported: true, denied: true, message: 'اجازه اعلان مسدود است؛ در تنظیمات سایت و تنظیمات اعلان اندروید آن را فعال کنید.' };
        return { supported: true, message: 'برای دریافت اعلان در نوار گوشی، فعال‌سازی را بزنید.' };
    }
    async function save(root, token, userId, reg, sub) {
        await request(root, token, 'push-subscribe', subscriptionData(sub));
        await owner(reg, userId);
        localStorage.setItem(optInKey, String(userId));
    }
    function fail(error, diag = diagnose()) { return { supported: diag.supported, enabled: false, message: error?.message || String(error) }; }
    window.appPush = {
        diagnose,
        async state(root, token, userId) {
            const d = diagnose();
            if (!d.supported || d.denied) return { ...d, enabled: false };
            try {
                const config = await request(root, token, 'push-vapid-key');
                if (!config.configured || !config.publicKey) return { supported: true, enabled: false, message: 'کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند؛ مدیر سامانه باید آن‌ها را تنظیم کند.' };
                const reg = await registration();
                let sub = await reg.pushManager.getSubscription();
                if (!sub || localStorage.getItem(optInKey) !== String(userId)) {
                    await owner(reg, 0);
                    return { supported: true, enabled: false, message: d.message };
                }
                if (!keyEquals(new Uint8Array(sub.options.applicationServerKey || []), decode(config.publicKey))) {
                    await owner(reg, 0); await sub.unsubscribe(); localStorage.removeItem(optInKey);
                    return { supported: true, enabled: false, message: 'کلید سرور تغییر کرده؛ اعلان این دستگاه را دوباره فعال کنید.' };
                }
                const stored = await request(root, token, 'push-status', { endpoint: sub.endpoint });
                if (!stored.registered) {
                    // Removed/expired endpoints need a fresh provider subscription, not endless re-saving.
                    await owner(reg, 0);
                    if (!await sub.unsubscribe()) throw new Error('بازیابی اشتراک انجام نشد؛ اعلان را دوباره فعال کنید.');
                    sub = await bounded(reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: decode(config.publicKey) }));
                }
                // A local subscription alone does not prove registration on the server.
                await save(root, token, userId, reg, sub);
                const status = await request(root, token, 'push-status', { endpoint: sub.endpoint });
                const last = status.last;
                let message = 'این دستگاه ثبت شد؛ دریافت واقعی را با «آزمایش» بررسی کنید.';
                if (last?.status === 'Accepted') message = 'آخرین اعلان به سرویس Push تحویل شد؛ دریافت روی گوشی را بررسی کنید.';
                else if (last?.status === 'Failed') message = 'ارسال اخیر ناموفق بود (' + (last.errorCode || 'خطای ارسال') + ')؛ تنظیمات سرور و اینترنت بررسی شود.';
                else if (last?.status === 'Pending' || last?.status === 'Working') message = 'اعلان در صف ارسال است؛ در خطای موقت دوباره تلاش می‌شود.';
                return { supported: true, enabled: !!status.registered, message };
            } catch (e) { return fail(e, d); }
        },
        async enable(root, token, userId) {
            const d = diagnose();
            if (!d.supported || d.denied) return { ...d, enabled: false };
            try {
                // Permission must be requested during the click, before any network await.
                const permission = Notification.permission === 'granted' ? 'granted' : await Notification.requestPermission();
                if (permission !== 'granted') return { supported: true, enabled: false, message: 'اجازه اعلان داده نشد؛ از تنظیمات سایت اجازه دهید.' };
                const config = await request(root, token, 'push-vapid-key');
                if (!config.configured || !config.publicKey) throw new Error('کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند.');
                const reg = await registration();
                let sub = await reg.pushManager.getSubscription();
                const key = decode(config.publicKey);
                if (sub && !keyEquals(new Uint8Array(sub.options.applicationServerKey || []), key)) {
                    await owner(reg, 0); if (!await sub.unsubscribe()) throw new Error('لغو اشتراک قدیمی انجام نشد.'); sub = null;
                }
                sub ||= await bounded(reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key }));
                try { await save(root, token, userId, reg, sub); }
                catch (e) { await owner(reg, 0); await sub.unsubscribe(); localStorage.removeItem(optInKey); throw e; }
                return { supported: true, enabled: true, message: 'دستگاه ثبت شد؛ برای اطمینان «آزمایش» را بزنید.' };
            } catch (e) { return fail(e, d); }
        },
        async disable(root, token) {
            try {
                localStorage.removeItem(optInKey);
                const reg = await navigator.serviceWorker.getRegistration();
                try { if (reg?.active) await owner(reg, 0); } catch (_) { /* Still revoke the provider subscription. */ }
                const sub = await reg?.pushManager.getSubscription();
                if (sub) {
                    const endpoint = sub.endpoint;
                    if (!await sub.unsubscribe()) throw new Error('لغو اشتراک مرورگر انجام نشد؛ از تنظیمات اندروید اعلان این سایت را ببندید.');
                    try { await request(root, token, 'push-unsubscribe', { endpoint }); }
                    catch (_) { return { supported: true, enabled: false, message: 'اعلان روی دستگاه خاموش شد؛ پاک‌سازی ثبت سرور پس از اتصال انجام می‌شود.' }; }
                }
                return { supported: true, enabled: false, message: 'اعلان این دستگاه خاموش شد.' };
            } catch (e) { return { supported: true, enabled: true, message: e.message }; }
        },
        async test(root, token) {
            const reg = await navigator.serviceWorker.getRegistration();
            const sub = await reg?.pushManager.getSubscription();
            if (!sub) throw new Error('ابتدا اعلان این دستگاه را فعال کنید.');
            return await request(root, token, 'test-push', { endpoint: sub.endpoint });
        },
        async clearLocal() {
            localStorage.removeItem(optInKey);
            try {
                const reg = await navigator.serviceWorker.getRegistration();
                try { if (reg?.active) await owner(reg, 0); } catch (_) { }
                const sub = await reg?.pushManager.getSubscription();
                if (sub) await sub.unsubscribe();
            } catch (_) { }
        },
        async logout(root, token) {
            // A durable zero owner suppresses queued pushes from a previous account, even offline.
            return await window.appPush.disable(root, token);
        }
    };
    // ذخیرهٔ فایل دودویی (مثل گواهی PFX) که Base64 آن از سرور آمده است.
    window.appSaveBase64File = function (base64, fileName, contentType) {
        try {
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const url = URL.createObjectURL(new Blob([bytes], { type: contentType || 'application/octet-stream' }));
            const link = document.createElement('a');
            link.href = url; link.download = fileName || 'file.bin';
            document.body.appendChild(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 60000);
            return true;
        } catch (_) { return false; }
    };

    // Live toasts remain in Blazor. Do not generate a duplicate OS notification via SignalR.
    window.attLocalNotify = () => false;

    // زنگ/لرزش اعلان داخل خود نرم‌افزار (وقتی اعلان سیستمی گوشی فعال نیست):
    // صدای کوتاه دو‌نُتی + لرزش در اندروید — دقیقاً مثل وقتی اعلان اندروید می‌رسد.
    let notifyAudio = null;
    function beep() {
        if (!notifyAudio) {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            notifyAudio = new Ctx();
        }
        if (notifyAudio.state === 'suspended') notifyAudio.resume().catch(() => { });
        const now = notifyAudio.currentTime;
        [[880, 0], [1180, 0.16]].forEach(([freq, at]) => {
            const osc = notifyAudio.createOscillator();
            const gain = notifyAudio.createGain();
            osc.type = 'sine';
            osc.frequency.value = freq;
            gain.gain.setValueAtTime(0.0001, now + at);
            gain.gain.exponentialRampToValueAtTime(0.18, now + at + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + at + 0.15);
            osc.connect(gain); gain.connect(notifyAudio.destination);
            osc.start(now + at); osc.stop(now + at + 0.17);
        });
    }
    window.attNotifyAlert = function () {
        try { beep(); } catch (_) { /* صدای مرورگر در دسترس نیست */ }
        try { if (navigator.vibrate) navigator.vibrate([120, 60, 120]); } catch (_) { /* لرزش پشتیبانی نمی‌شود */ }
        return true;
    };
})();
