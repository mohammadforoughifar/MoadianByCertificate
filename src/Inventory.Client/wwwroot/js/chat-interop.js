// Native keyboard handling is needed to prevent only Enter (not all typing).
// Chat content is never inserted with innerHTML/eval.
const instances = new Map();
let sequence = 0;

export function initialize(dotnet) {
    const id = ++sequence;
    const state = { dotnet, element: null, keydown: null };
    state.visibility = () => dotnet.invokeMethodAsync('ChatVisibilityChanged',
        !document.hidden && document.hasFocus()).catch(() => {});
    document.addEventListener('visibilitychange', state.visibility);
    window.addEventListener('focus', state.visibility);
    window.addEventListener('blur', state.visibility);
    // جای نوار عملیات پیام: کنار حباب؛ اگر جا نبود بالای حباب؛ اگر آنجا هم نبود داخل حباب.
    state.placeActions = event => placeBubbleActions(event);
    document.addEventListener('mouseover', state.placeActions);
    instances.set(id, state);
    state.visibility();
    return id;
}

function placeBubbleActions(event) {
    const bubble = event.target && event.target.closest ? event.target.closest('.message-bubble') : null;
    if (!bubble) return;
    if (event.relatedTarget && bubble.contains(event.relatedTarget)) return;
    const bar = bubble.querySelector('.bubble-actions-overlay');
    const box = document.getElementById('chatMessagesScroll');
    if (!bar || !box) return;
    bubble.classList.remove('act-top', 'act-in');
    const b = bubble.getBoundingClientRect();
    const c = box.getBoundingClientRect();
    const width = bar.offsetWidth || 280;
    const room = bubble.classList.contains('bubble-out') ? b.left - c.left : c.right - b.right;
    if (room >= width + 16) return;
    bubble.classList.add(b.top - c.top >= 46 ? 'act-top' : 'act-in');
}

export function attachComposer(id, element) {
    const state = instances.get(id);
    if (!state || !element || state.element === element) return;
    if (state.element) {
        state.element.removeEventListener('keydown', state.keydown);
        if (state.input) state.element.removeEventListener('input', state.input);
    }
    state.element = element;
    state.keydown = event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && !event.repeat) {
            event.preventDefault();
            state.dotnet.invokeMethodAsync('SendFromComposer', element.value).catch(() => {});
        } else if (event.key === 'Escape' && !event.isComposing) {
            state.dotnet.invokeMethodAsync('CancelFromComposer').catch(() => {});
        }
    };
    element.addEventListener('keydown', state.keydown);
    // رشد خودکار کادر متن با هر خط جدید (اینتر/متن چندخطی) تا سقف CSS max-height
    state.input = () => growElement(element);
    element.addEventListener('input', state.input);
    growElement(element);
}

function growElement(element) {
    if (!element) return;
    element.style.height = 'auto';
    const min = parseInt(getComputedStyle(element).minHeight, 10) || 54;
    const max = parseInt(getComputedStyle(element).maxHeight, 10) || 220;
    element.style.height = Math.max(min, Math.min(element.scrollHeight, max)) + 'px';
}

/** بزرگ کردن کادر متن — بعد از ویرایش پیام که متن از بیرون پر می‌شود. */
export function growComposer(id) {
    const state = instances.get(id);
    if (state && state.element) growElement(state.element);
}

/** برگشت به اندازهٔ اول — بعد از ارسال موفق که متن پاک می‌شود. */
export function resetComposer(id) {
    const state = instances.get(id);
    if (state && state.element) {
        state.element.style.height = 'auto';
        growElement(state.element);
    }
}

/** برگرداندن فوکوس به کادر متن (بعد از ارسال، ویرایش، پاسخ، کپی و فوروارد).
 *  toEnd: مکان‌نما به آخر متن برود — برای ویرایش که متن پیام داخل کادر گذاشته می‌شود.
 *  force: برای کارهای مستقیم کاربر (پاسخ/ویرایش/کپی/فوروارد) حتی اگر فیلد دیگری فوکوس دارد. */
export function focusComposer(id, toEnd, force) {
    const state = instances.get(id);
    let element = state && state.element;
    if (!element || !element.isConnected) element = document.querySelector('.chat-textarea');
    if (!element || element.disabled) return;
    // اگر کاربر در همین لحظه داخل یک فیلد دیگر (جستجو، مودال) تایپ می‌کند، فوکوس را از او نگیر.
    const active = document.activeElement;
    if (!force && active && active !== element && active !== document.body &&
        (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.isContentEditable)) return;
    element.focus({ preventScroll: true });
    if (toEnd) {
        const end = element.value.length;
        try { element.setSelectionRange(end, end); } catch (e) { /* نادیده */ }
        element.scrollTop = element.scrollHeight;
    }
}

export function scrollToBottom() {
    const element = document.getElementById('chatMessagesScroll');
    if (element) element.scrollTop = element.scrollHeight;
}

// =====================================================================
// ضبط پیام صوتی (MediaRecorder) — خروجی برای آپلود به endpoint پیوست چت
// =====================================================================
let recStream = null;
let recRecorder = null;
let recChunks = [];
let recStartedAt = 0;

function recPickMime() {
    if (!window.MediaRecorder) return '';
    const candidates = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus', 'audio/ogg'];
    for (const mime of candidates) {
        try { if (MediaRecorder.isTypeSupported(mime)) return mime; } catch (e) { /* ادامه */ }
    }
    return '';
}

function recRelease() {
    try { if (recStream) recStream.getTracks().forEach(t => t.stop()); } catch (e) { /* نادیده */ }
    recStream = null;
    recRecorder = null;
    recChunks = [];
    recStartedAt = 0;
}

export function isRecordingSupported() {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder);
}

// ---------------------------------------------------------------------
// تشخیص وضعیت میکروفون و مجوز دسترسی
// مرورگرها اجازهٔ میکروفون را فقط در «زمینهٔ امن» می‌دهند: HTTPS یا localhost.
// روی http://192.168.x.x هیچ پیغام مجوزی نشان داده نمی‌شود و getUserMedia
// در دسترس نیست؛ قبلاً همین باعث خطای مبهم «دسترسی به میکروفون ممکن نشد» می‌شد.
// ---------------------------------------------------------------------

/** آدرس امن پیشنهادی (HTTPS) برای همین صفحه — سرور سامانه روی پورت 5443 هم گوش می‌دهد. */
function suggestedSecureUrl() {
    try {
        const url = new URL(window.location.href);
        if (url.protocol === 'https:') return null;
        url.protocol = 'https:';
        url.port = '5443';
        return url.toString();
    } catch (e) {
        return null;
    }
}

/** وضعیت کامل میکروفون برای نمایش راهنمای درست به کاربر. */
export async function micStatus() {
    const status = {
        secure: !!window.isSecureContext,
        supported: isRecordingSupported(),
        permission: 'unknown',   // granted | denied | prompt | unknown
        hasInputDevice: null,    // true | false | null (نامعلوم)
        secureUrl: suggestedSecureUrl()
    };
    try {
        if (navigator.permissions && navigator.permissions.query) {
            const st = await navigator.permissions.query({ name: 'microphone' });
            status.permission = st.state;
        }
    } catch (e) { /* فایرفاکس/سافاری: پرس‌وجوی مجوز میکروفون پشتیبانی نمی‌شود */ }
    try {
        if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
            const devices = await navigator.mediaDevices.enumerateDevices();
            status.hasInputDevice = devices.some(d => d.kind === 'audioinput');
        }
    } catch (e) { /* نادیده */ }
    return status;
}

/** پیام فارسی متناسب با خطای getUserMedia — تا کاربر بداند دقیقاً مشکل چیست. */
function describeMicError(err) {
    const name = (err && err.name) || '';
    if (!window.isSecureContext)
        return { reason: 'insecure', message: 'مرورگر اجازهٔ میکروفون را فقط روی آدرس امن (HTTPS) می‌دهد؛ روی HTTP هیچ پیغام مجوزی هم نشان داده نمی‌شود.' };
    if (!navigator.mediaDevices)
        return { reason: 'unsupported', message: 'این مرورگر از ضبط صدا پشتیبانی نمی‌کند؛ از کروم/اِج نسخهٔ جدید استفاده کنید.' };
    switch (name) {
        case 'NotAllowedError':
        case 'PermissionDeniedError':
        case 'SecurityError':
            return { reason: 'denied', message: 'دسترسی به میکروفون رد شده است. با کلیک روی قفل/آیکون کنار نوار آدرس، میکروفون را روی «اجازه» بگذارید و صفحه را بازخوانی کنید.' };
        case 'NotFoundError':
        case 'DevicesNotFoundError':
        case 'OverconstrainedError':
            return { reason: 'no-device', message: 'میکروفونی روی این دستگاه پیدا نشد.' };
        case 'NotReadableError':
        case 'TrackStartError':
            return { reason: 'busy', message: 'میکروفون در اختیار برنامهٔ دیگری است؛ آن برنامه را ببندید و دوباره تلاش کنید.' };
        case 'AbortError':
            return { reason: 'aborted', message: 'درخواست ضبط لغو شد؛ دوباره تلاش کنید.' };
        case 'NotSupportedError':
            return { reason: 'unsupported', message: 'این مرورگر اجازهٔ دسترسی به میکروفون را در این حالت نمی‌دهد (مثلاً حالت ناشناس یا مرورگر قدیمی).' };
        default:
            return { reason: 'error', message: 'ضبط صدا ممکن نشد' + (err && err.message ? ' (خطای مرورگر: ' + err.message + ')' : '') + '. اگر پیغام مجوز نشان داده نمی‌شود، در منوی مرورگر (آیکون قفل کنار آدرس ← تنظیمات سایت) مجوز میکروفون را روی «اجازه» بگذارید و صفحه را بازخوانی کنید.' };
    }
}

/**
 * درخواست مجوز میکروفون بدون ضبط — با کلیک کاربر اجرا شود تا مرورگر
 * پیغام «اجازه می‌دهید؟» را نشان دهد. خروجی: {ok, reason, message}
 */
export async function requestMicPermission() {
    if (!window.isSecureContext || !isRecordingSupported()) return describeMicError(null);
    let stream = null;
    try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        return { ok: true, reason: 'granted', message: 'مجوز میکروفون داده شد.' };
    } catch (err) {
        return describeMicError(err);
    } finally {
        try { if (stream) stream.getTracks().forEach(t => t.stop()); } catch (e) { /* نادیده */ }
    }
}

export async function startRecording() {
    if (recRecorder && recRecorder.state === 'recording') return { ok: true, reason: 'recording' };
    // روی HTTP (بدون HTTPS/localhost) مرورگر getUserMedia ندارد و پیغام مجوز هم نمی‌دهد.
    if (!window.isSecureContext || !isRecordingSupported()) return describeMicError(null);
    try {
        recRelease();
        recStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const mime = recPickMime();
        recRecorder = mime ? new MediaRecorder(recStream, { mimeType: mime }) : new MediaRecorder(recStream);
        recChunks = [];
        recRecorder.ondataavailable = e => { if (e.data && e.data.size) recChunks.push(e.data); };
        recRecorder.start(250);
        recStartedAt = Date.now();
        return { ok: true, reason: 'started' };
    } catch (err) {
        recRelease();
        return describeMicError(err);
    }
}

export function cancelRecording() {
    try { if (recRecorder && recRecorder.state !== 'inactive') recRecorder.stop(); } catch (e) { /* نادیده */ }
    recRelease();
}

/** پایان ضبط → Promise با base64 یا null (خیلی کوتاه/خطا). */
export function stopRecording() {
    return new Promise(resolve => {
        if (!recRecorder || recRecorder.state === 'inactive') { recRelease(); resolve(null); return; }
        const rec = recRecorder;
        const durationMs = Math.max(0, Date.now() - recStartedAt);
        rec.onstop = async () => {
            try {
                const type = (rec.mimeType || 'audio/webm').split(';')[0];
                const blob = new Blob(recChunks, { type: type });
                recRelease();
                if (!blob.size || durationMs < 500) { resolve(null); return; }
                const bytes = new Uint8Array(await blob.arrayBuffer());
                let binary = '';
                for (let i = 0; i < bytes.length; i += 0x8000) {
                    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
                }
                const ext = type.indexOf('mp4') >= 0 ? 'm4a' : (type.indexOf('ogg') >= 0 ? 'ogg' : 'webm');
                resolve({ base64: btoa(binary), ext: ext, contentType: type, sizeBytes: blob.size, durationMs: durationMs });
            } catch (err) {
                recRelease();
                resolve(null);
            }
        };
        try { rec.stop(); } catch (e) { recRelease(); resolve(null); }
    });
}

export function scrollToMessage(id) {
    document.getElementById('msg-' + Number(id))?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

const mobileQuery = '(max-width: 767.98px)';

export function isMobileView() {
    return window.matchMedia(mobileQuery).matches;
}

export function watchMobile(id) {
    const state = instances.get(id);
    if (!state || state.mobileWatch) return;
    const mq = window.matchMedia(mobileQuery);
    state.mobileWatch = event => {
        state.dotnet.invokeMethodAsync('MobileViewChanged', !!event.matches).catch(() => {});
    };
    if (mq.addEventListener) mq.addEventListener('change', state.mobileWatch);
    else mq.addListener(state.mobileWatch);
    state.mobileQuery = mq;
}

export function dispose(id) {
    const state = instances.get(id);
    if (!state) return;
    // آزادسازی میکروفون اگر صفحهٔ چت هنگام ضبط بسته شود
    cancelRecording();
    document.removeEventListener('visibilitychange', state.visibility);
    window.removeEventListener('focus', state.visibility);
    window.removeEventListener('blur', state.visibility);
    if (state.placeActions) document.removeEventListener('mouseover', state.placeActions);
    if (state.mobileWatch && state.mobileQuery) {
        if (state.mobileQuery.removeEventListener) state.mobileQuery.removeEventListener('change', state.mobileWatch);
        else state.mobileQuery.removeListener(state.mobileWatch);
    }
    if (state.element) state.element.removeEventListener('keydown', state.keydown);
    instances.delete(id);
}
