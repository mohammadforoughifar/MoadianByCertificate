// Shared page interop. Keep generated print HTML outside the HTML script parser.
// تعیین آدرس سرور API در زمان اجرا
// ================== ادیتور متن غنی ==================
// اجرای دستور روی ادیتورِ متمرکز/اول (برای سازگاری با کدهای قبلی)
// ============ کپی متن (با فالبک برای http بدون گواهی) ============
window.invCopyText = function (text) {
    try {
        if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text); return true; }
        var ta = document.createElement('textarea');
        ta.value = text; ta.setAttribute('readonly', '');
        ta.style.position = 'fixed'; ta.style.top = '-1000px'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select(); ta.setSelectionRange(0, ta.value.length);
        var ok = document.execCommand('copy');
        document.body.removeChild(ta);
        return ok;
    } catch (e) { return false; }
};

window.rteExec = function (cmd, value) {
    document.execCommand(cmd, false, value === undefined ? null : value);
};
// یافتن ریشه ادیتور
window.rteGetHtml = function (id) { var el = document.getElementById(id); return el ? el.innerHTML : ""; };
window.rteSetHtml = function (id, html) { var el = document.getElementById(id); if (el) el.innerHTML = html || ""; };
// طول HTML داخل ادیتور — برای «تأیید نشستن محتوا» پس از rteSetHtml.
// (برگرداندن خودِ HTML به .NET برای متن‌های چندمگابایتی پرهزینه بود؛ فقط طول برمی‌گردد.)
window.rteHtmlLength = function (id) {
    var el = document.getElementById(id);
    if (!el) return -1;
    var html = el.innerHTML || "";
    return html.length;
};
window.rteGetEl = function (el) { return el ? el.innerHTML : ""; };
window.rteSetEl = function (el, html) { if (el) el.innerHTML = html || ""; };
window.rteExecEl = function (el, cmd, value) {
    if (!el) return;
    el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand(cmd, false, value === undefined ? null : value);
};

// نگهداری آخرین انتخاب متن (برای اعمال فونت/رنگ/اندازه وقتی فوکوس به نوار ابزار می‌رود)
window.__rteSavedRange = null;
window.rteSaveSelection = function (id) {
    var el = document.getElementById(id);
    if (!el) return;
    var sel = window.getSelection();
    if (sel.rangeCount > 0 && el.contains(sel.anchorNode))
        window.__rteSavedRange = sel.getRangeAt(0).cloneRange();
};
window.rteRestoreSelection = function (id) {
    var el = document.getElementById(id);
    if (!el || !window.__rteSavedRange) return;
    el.focus();
    var sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(window.__rteSavedRange);
};

// اجرای دستور روی ادیتور مشخص — فوکوس و dispatch
window.rteExecId = function (id, cmd, value) {
    var el = document.getElementById(id);
    if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand(cmd, false, value === undefined ? null : value);
};
// تنظیم قلم
window.rteFont = function (id, family) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("fontName", false, family);
};
// تنظیم اندازه قلم (به پیکسل) — با ترفند font[size=7]
window.rteSize = function (id, px) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    // برای ساخت تگ فونت خام، styleWithCSS را موقتاً خاموش می‌کنیم
    document.execCommand("styleWithCSS", false, false);
    document.execCommand("fontSize", false, "7");
    document.execCommand("styleWithCSS", false, true);
    var fac = (el.querySelectorAll('font[size="7"]'));
    for (var i = 0; i < fac.length; i++) {
        var s = document.createElement("span");
        s.style.fontSize = px;
        s.innerHTML = fac[i].innerHTML;
        fac[i].replaceWith(s);
    }
};
// رنگ متن
window.rteColor = function (id, color) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("foreColor", false, color);
};
// رنگ پس‌زمینه (هایلایت)
window.rteHilite = function (id, color) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("hiliteColor", false, color);
};
// درج لینک
window.rteLink = function (id) {
    var url = window.prompt("آدرس لینک را وارد کنید:", "https://");
    if (!url) return;
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("createLink", false, url);
};
// درج جدول
window.rteTable = function (id, rows, cols) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    rows = parseInt(rows) || 2; cols = parseInt(cols) || 2;
    var html = '<table class="table table-bordered my-2" style="width:100%; border-collapse:collapse; border:1px solid #cbd5e1;"><tbody>';
    for (var r = 0; r < rows; r++) {
        html += '<tr>';
        for (var c = 0; c < cols; c++) {
            html += '<td style="border:1px solid #cbd5e1; padding:8px;">&nbsp;</td>';
        }
        html += '</tr>';
    }
    html += '</tbody></table><p><br></p>';
    document.execCommand("insertHTML", false, html);
};
// درج محتوای HTML سفارشی
window.rteInsertHtml = function (id, html) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("insertHTML", false, html);
};
// تغییر جهت متن (RTL / LTR)
window.rteToggleDir = function (id) {
    var el = document.getElementById(id); if (!el) return;
    var current = el.getAttribute("dir") || "rtl";
    var next = current === "rtl" ? "ltr" : "rtl";
    el.setAttribute("dir", next);
    el.style.textAlign = next === "rtl" ? "right" : "left";
};
// چاپ نامه (اتوماسیون اداری)
window.ltxPrint = function () { window.print(); };
window.printWorkOrderDetail = function () { window.print(); };
// ---------- چاپ سند در یک iframe جداگانه ----------
// سند چاپ باید مستقل از استایل‌های صفحه و پوستهٔ اپ باشد؛ وگرنه قواعد
// @media print اپ (مخفی‌کردن سایدبار/محتوا و …) محتوای سند را هم می‌پوشاند
// و خروجی سفید می‌شود. اینجا یک سند کامل و خودبسنده در iframe می‌سازیم و
// پنجرهٔ چاپِ همان iframe را باز می‌کنیم.
// __FONT_BASE__ در سند با آدرس واقعی پوشهٔ فونت‌ها جایگزین می‌شود.
window.giPrint = function (html) {
    try {
        var base = document.baseURI.replace(/[^/]*$/, '');
        html = (html || '').split('__FONT_BASE__').join(base);

        var f = document.getElementById('gi-print-frame');
        if (!f) {
            f = document.createElement('iframe');
            f.id = 'gi-print-frame';
            f.setAttribute('aria-hidden', 'true');
            // صفر-بعد اما نه display:none — مرورگرهای Some از iframe پنهان چاپ نمی‌کنند
            f.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;opacity:0;';
            document.body.appendChild(f);
        }
        var w = f.contentWindow;
        w.document.open();
        w.document.write(html);
        w.document.close();
        w.focus();
        // کمی صبر تا مرورگر چیدمان و فونت‌ها را آماده کند
        setTimeout(function () { try { w.print(); } catch (e) { } }, 80);
        return true;
    } catch (e) {
        console.error('giPrint:', e);
        return false;
    }
};
// فوکوس روی یک المان با شناسه — ناوبری جدول سطرهای حواله با کلید Enter
// (select=true متن موجود را انتخاب می‌کند تا تایپ قدرت، قالب را جعل کند)
window.focusById = function (id, select) {
    var el = document.getElementById(id);
    if (!el || el.disabled) return false;
    el.focus();
    if (select && el.select) { try { el.select(); } catch (e) { } }
    return true;
};
// Normalize optimized Blazor byte-array interop (Uint8Array); retain base64 fallback for older callers.
function ltxPdfBytes(payload) {
    if (payload instanceof Uint8Array) return payload;
    if (payload instanceof ArrayBuffer) return new Uint8Array(payload);
    var bin = atob(String(payload || ''));
    var bytes = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
}
// باز کردن PDF نامه (چاپ روی سربرگ) در تب جدید — ورودی باینری ترجیح دارد
window.ltxOpenPdf = function (payload, fileName) {
    try {
        var bytes = ltxPdfBytes(payload);
        var blob = new Blob([bytes], { type: 'application/pdf' });
        var url = URL.createObjectURL(blob);
        var w = window.open(url, '_blank');
        if (!w) { // popup blocked → download
            var a = document.createElement('a');
            a.href = url; a.download = fileName || 'letter.pdf';
            document.body.appendChild(a); a.click(); a.remove();
        }
        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
    } catch (e) { console.error(e); }
};

// ================== آماده‌سازی و بازیابی جریان چاپ ==================
// پنجره در capture-phase کلیک رزرو می‌شود تا popup blocker متوجه gesture کاربر شود.
window.ltxPrintSessions = Object.create(null);
window.ltxPrintPendingWindows = [];
window.ltxPrintReserveByClick = function () {
    try {
        var popup = window.open('about:blank', '_blank');
        if (popup) window.ltxPrintPendingWindows.push(popup);
        return !!popup;
    } catch (error) {
        console.error('[letter-print] popup reservation failed', error);
        return false;
    }
};
document.addEventListener('click', function (event) {
    var target = event.target instanceof Element ? event.target.closest('[data-ltx-print-option]') : null;
    if (target) window.ltxPrintReserveByClick();
}, true);

function ltxEnsurePrintFallbackStyles() {
    if (document.getElementById('ltx-print-fallback-style')) return;
    var style = document.createElement('style');
    style.id = 'ltx-print-fallback-style';
    style.textContent = '@media print {' +
        '@page{size:A4;margin:12mm}' +
        'body.ltx-print-direct-page .ltx-print-overlay,body.ltx-print-direct-page [data-print-hide],body.ltx-print-direct-page .no-print,body.ltx-print-direct-page nav,body.ltx-print-direct-page [role=navigation],body.ltx-print-direct-page .sidebar,body.ltx-print-direct-page .rail,body.ltx-print-direct-page .navbar,body.ltx-print-direct-page .topbar,body.ltx-print-direct-page .app-menu,body.ltx-print-direct-page button,body.ltx-print-direct-page input,body.ltx-print-direct-page select{display:none!important}' +
        'body.ltx-print-direct-page{direction:rtl;font-family:Vazirmatn,Tahoma,Arial,sans-serif!important;color:#111!important;background:#fff!important}' +
        'body.ltx-print-direct-page main,body.ltx-print-direct-page [role=main]{width:100%!important;max-width:none!important;overflow:visible!important}' +
        'body.ltx-print-direct-page p,body.ltx-print-direct-page li{orphans:3;widows:3}body.ltx-print-direct-page img,body.ltx-print-direct-page figure,body.ltx-print-direct-page tr{break-inside:avoid;page-break-inside:avoid}' +
        '}';
    document.head.appendChild(style);
}
function ltxPrintShellMarkup() {
    return '<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>چاپ نامه</title>' +
        '<style>html,body{margin:0;width:100%;height:100%;font-family:Vazirmatn,Tahoma,Arial,sans-serif;color:#1e293b;background:#f1f5f9}*{box-sizing:border-box}.bar{height:58px;display:flex;align-items:center;gap:8px;padding:8px 14px;background:#fff;border-bottom:1px solid #cbd5e1}.status{margin-left:auto;font-size:14px}.actions{display:flex;gap:8px}button{border:0;border-radius:6px;padding:8px 12px;background:#e2e8f0;color:#0f172a;font:inherit;cursor:pointer}button.primary{background:#0f766e;color:#fff}button[hidden]{display:none}iframe{display:block;width:100%;height:calc(100% - 58px);border:0;background:white}.error{padding:10px 14px;color:#b91c1c;background:#fef2f2;line-height:1.8}.error[hidden]{display:none}</style></head><body>' +
        '<div class="bar"><div id="ltx-print-status" class="status">در حال آماده‌سازی نسخهٔ چاپ…</div><div class="actions">' +
        '<button id="ltx-print-now" class="primary" hidden>چاپ PDF</button><button id="ltx-print-retry" hidden>تلاش مجدد</button><button id="ltx-print-direct" hidden>چاپ مستقیم صفحه</button><button id="ltx-print-close">بستن</button></div></div>' +
        '<div id="ltx-print-error" class="error" hidden></div><iframe id="ltx-print-frame" title="پیش‌نمایش PDF"></iframe></body></html>';
}
function ltxCreateOverlay(session) {
    ltxEnsurePrintFallbackStyles();
    var overlay = document.createElement('section');
    overlay.className = 'ltx-print-overlay';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', 'چاپ نامه');
    overlay.innerHTML = '<style>.ltx-print-overlay{position:fixed;z-index:2147483000;inset:0;background:#f1f5f9;display:flex;flex-direction:column;direction:rtl;font-family:Vazirmatn,Tahoma,Arial,sans-serif;color:#1e293b}.ltx-print-overlay .ltx-bar{min-height:58px;display:flex;align-items:center;gap:8px;padding:8px 14px;background:#fff;border-bottom:1px solid #cbd5e1}.ltx-print-overlay .ltx-status{margin-left:auto;font-size:14px}.ltx-print-overlay .ltx-actions{display:flex;gap:8px}.ltx-print-overlay button{border:0;border-radius:6px;padding:8px 12px;background:#e2e8f0;color:#0f172a;font:inherit;cursor:pointer}.ltx-print-overlay button.primary{background:#0f766e;color:#fff}.ltx-print-overlay button[hidden]{display:none}.ltx-print-overlay .ltx-error{padding:10px 14px;color:#b91c1c;background:#fef2f2;line-height:1.8}.ltx-print-overlay .ltx-error[hidden]{display:none}.ltx-print-overlay iframe{display:block;flex:1;width:100%;min-height:0;border:0;background:white}@media print{.ltx-print-overlay .ltx-bar,.ltx-print-overlay .ltx-error{display:none!important}.ltx-print-overlay iframe{height:100vh}}</style>' +
        '<div class="ltx-bar"><div class="ltx-status">در حال آماده‌سازی نسخهٔ چاپ…</div><div class="ltx-actions"><button class="ltx-print-now primary" hidden>چاپ PDF</button><button class="ltx-print-retry" hidden>تلاش مجدد</button><button class="ltx-print-direct" hidden>چاپ مستقیم صفحه</button><button class="ltx-print-close">بستن</button></div></div>' +
        '<div class="ltx-error" hidden></div><iframe class="ltx-print-frame" title="پیش‌نمایش PDF"></iframe>';
    document.body.appendChild(overlay);
    session.overlay = overlay;
    session.frame = overlay.querySelector('.ltx-print-frame');
    session.status = overlay.querySelector('.ltx-status');
    session.error = overlay.querySelector('.ltx-error');
    overlay.querySelector('.ltx-print-now').addEventListener('click', function () { ltxTriggerPrint(session); });
    overlay.querySelector('.ltx-print-retry').addEventListener('click', function () { ltxResolvePrintAction(session, 'retry'); });
    overlay.querySelector('.ltx-print-direct').addEventListener('click', function () { ltxResolvePrintAction(session, 'direct'); });
    overlay.querySelector('.ltx-print-close').addEventListener('click', function () { ltxResolvePrintAction(session, 'close'); });
    if (session.popupBlocked) ltxSetPrintStatus(session, 'پنجرهٔ بازشو مسدود شد؛ چاپ در همین صفحه ادامه می‌یابد. برای تجربهٔ بهتر، popup این سایت را مجاز کنید.');
}
function ltxSetPrintStatus(session, text) {
    if (session.status) session.status.textContent = text;
}
function ltxSetPrintError(session, message) {
    var actions = session.overlay ? session.overlay : session.win && !session.win.closed ? session.win.document : null;
    if (!actions) return;
    var status = session.status || actions.getElementById('ltx-print-status');
    var error = session.error || actions.getElementById('ltx-print-error');
    if (status) status.textContent = 'چاپ آماده نشد';
    if (error) { error.textContent = String(message || 'خطا در آماده‌سازی چاپ'); error.hidden = false; }
    var retry = session.overlay ? actions.querySelector('.ltx-print-retry') : actions.getElementById('ltx-print-retry');
    var direct = session.overlay ? actions.querySelector('.ltx-print-direct') : actions.getElementById('ltx-print-direct');
    if (retry) retry.hidden = false;
    if (direct) direct.hidden = false;
}
function ltxResolvePrintAction(session, action) {
    if (session.overlay) {
        if (session.resolveAction) { var resolve = session.resolveAction; session.resolveAction = null; resolve(action); }
        else session.queuedAction = action;
        return;
    }
    try {
        if (session.win && !session.win.closed && session.win.opener)
            session.win.opener.postMessage({ type: 'ltx-print-action', id: session.id, action: action }, window.location.origin);
    } catch (error) { console.error('[letter-print] could not notify opener', error); }
}
window.addEventListener('message', function (event) {
    if (event.origin !== window.location.origin || !event.data || event.data.type !== 'ltx-print-action') return;
    var session = window.ltxPrintSessions[event.data.id];
    if (!session || event.source !== session.win) return;
    if (event.data.action === 'print') { ltxTriggerPrint(session); return; }
    if (session.resolveAction) { var resolve = session.resolveAction; session.resolveAction = null; resolve(event.data.action); }
    else session.queuedAction = event.data.action;
});
function ltxAttachPopupControls(session) {
    var doc = session.win.document;
    session.frame = doc.getElementById('ltx-print-frame');
    session.status = doc.getElementById('ltx-print-status');
    session.error = doc.getElementById('ltx-print-error');
    doc.getElementById('ltx-print-now').addEventListener('click', function () { ltxResolvePrintAction(session, 'print'); });
    doc.getElementById('ltx-print-retry').addEventListener('click', function () { ltxResolvePrintAction(session, 'retry'); });
    doc.getElementById('ltx-print-direct').addEventListener('click', function () { ltxResolvePrintAction(session, 'direct'); });
    doc.getElementById('ltx-print-close').addEventListener('click', function () { session.win.close(); });
}
function ltxEnsurePrintSurface(session) {
    if (session.win && !session.win.closed) return true;
    session.win = null;
    if (!session.overlay) ltxCreateOverlay(session);
    return !!session.overlay;
}
function ltxPrintAttemptCurrent(session, attemptId) {
    return !!session && !session.cleaned && !session.failed && session.attemptId === attemptId;
}
function ltxPrintCleanup(session, closePopup) {
    if (!session || session.cleaned) return;
    session.cleaned = true;
    if (session.closePoll) clearInterval(session.closePoll);
    if (session.objectUrl) { URL.revokeObjectURL(session.objectUrl); session.objectUrl = null; }
    document.body.classList.remove('ltx-print-direct-page');
    if (session.overlay && session.overlay.parentNode) session.overlay.parentNode.removeChild(session.overlay);
    if (closePopup && session.win && !session.win.closed) {
        try { session.win.close(); window.focus(); }
        catch (error) { console.error('[letter-print] popup close failed', error); }
    }
    delete window.ltxPrintSessions[session.id];
}
function ltxPrintAfterPrint(session) {
    if (!session || (!session.printRequested && !session.directFallback)) return;
    ltxPrintCleanup(session, true);
}
function ltxWithPrintTimeout(promise, timeoutMs, message) {
    var timer;
    var timeout = new Promise(function (_, reject) {
        timer = setTimeout(function () { reject(new Error(message)); }, Math.max(1, timeoutMs));
    });
    return Promise.race([promise, timeout]).finally(function () { clearTimeout(timer); });
}
function ltxWaitForPrintAssets(doc, timeoutMs) {
    var tasks = [];
    try {
        if (doc.fonts && doc.fonts.ready) tasks.push(doc.fonts.ready);
        Array.prototype.forEach.call(doc.images || [], function (image) {
            tasks.push(new Promise(function (resolve, reject) {
                function loaded() {
                    image.removeEventListener('load', loaded); image.removeEventListener('error', failed);
                    if (image.naturalWidth === 0) reject(new Error('بارگذاری یکی از تصاویر صفحهٔ چاپ ناموفق بود.'));
                    else if (image.decode) image.decode().then(resolve, reject);
                    else resolve();
                }
                function failed() { image.removeEventListener('load', loaded); image.removeEventListener('error', failed); reject(new Error('بارگذاری یکی از تصاویر صفحهٔ چاپ ناموفق بود.')); }
                if (image.complete) loaded();
                else { image.addEventListener('load', loaded, { once: true }); image.addEventListener('error', failed, { once: true }); }
            }));
        });
    } catch (error) { return Promise.reject(error); }
    return ltxWithPrintTimeout(Promise.all(tasks), timeoutMs, 'بارگذاری فونت‌ها و تصاویر چاپ بیش از ۱۵ ثانیه طول کشید.');
}
function ltxTriggerPrint(session) {
    if (!session || !(session.ready || session.pdfLoaded) || !session.frame) return false;
    try {
        var target = session.frame.contentWindow;
        if (!target) throw new Error('صفحهٔ PDF در مرورگر آماده نیست.');
        target.focus();
        session.printRequested = true;
        target.print();
        return true;
    } catch (error) {
        session.printRequested = false;
        console.error('[letter-print] native print request failed', error);
        ltxSetPrintError(session, 'PDF آماده است، اما مرورگر اجازهٔ بازکردن پنجرهٔ چاپ را نداد. از دکمهٔ چاپ داخل نمایشگر PDF استفاده کنید یا دوباره تلاش کنید.');
        return false;
    }
}
window.ltxPrintBegin = function () {
    var id = 'pw' + Date.now().toString(36) + Math.random().toString(36).slice(2, 9);
    var popup = null;
    try { popup = window.ltxPrintPendingWindows.shift(); } catch (error) { console.error('[letter-print] reserved popup retrieval failed', error); }
    if (!popup || popup.closed) {
        try { popup = window.open('about:blank', '_blank'); } catch (error) { console.error('[letter-print] popup open failed', error); }
    }
    var session = { id: id, win: popup && !popup.closed ? popup : null, popupBlocked: !popup || popup.closed, overlay: null, frame: null, status: null, error: null, objectUrl: null, ready: false, cleaned: false, queuedAction: null, resolveAction: null };
    window.ltxPrintSessions[id] = session;
    try {
        if (session.win) {
            session.win.document.open(); session.win.document.write(ltxPrintShellMarkup()); session.win.document.close();
            ltxAttachPopupControls(session);
        } else ltxCreateOverlay(session);
    } catch (error) {
        console.error('[letter-print] popup setup failed; using in-page print surface', error);
        session.win = null; session.popupBlocked = true; ltxCreateOverlay(session);
    }
    window.addEventListener('afterprint', function () { ltxPrintAfterPrint(session); }, { once: true });
    if (session.win) {
        try { session.win.addEventListener('afterprint', function () { ltxPrintAfterPrint(session); }, { once: true }); } catch (error) { console.error('[letter-print] afterprint listener failed', error); }
    }
    return id;
};
window.ltxPrintReady = async function (id, payload, fileName, timeoutMs) {
    var session = window.ltxPrintSessions[id];
    if (!session || session.cleaned || !ltxEnsurePrintSurface(session)) return false;
    var timeout = Math.max(1, Number(timeoutMs) || 15000);
    var started = Date.now();
    try {
        var attemptId = (session.attemptId || 0) + 1;
        session.attemptId = attemptId;
        session.failed = false;
        if (session.objectUrl) URL.revokeObjectURL(session.objectUrl);
        var bytes = ltxPdfBytes(payload);
        session.objectUrl = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
        session.ready = false;
        session.pdfLoaded = false;
        session.printRequested = false;
        session.directFallback = false;
        session.frame = session.overlay ? session.overlay.querySelector('.ltx-print-frame') : session.win.document.getElementById('ltx-print-frame');
        if (!session.frame) throw new Error('صفحهٔ نمایش PDF ساخته نشد.');
        var loaded = new Promise(function (resolve, reject) {
            session.frame.onload = function () { resolve(); };
            session.frame.onerror = function () { reject(new Error('مرورگر نتوانست فایل PDF را باز کند.')); };
        });
        session.frame.src = session.objectUrl;
        await ltxWithPrintTimeout(loaded, timeout, 'بارگذاری PDF بیش از ۱۵ ثانیه طول کشید.');
        if (!ltxPrintAttemptCurrent(session, attemptId)) return false;
        session.pdfLoaded = true;
        var doc = session.overlay ? document : session.win.document;
        await ltxWaitForPrintAssets(doc, Math.max(1, timeout - (Date.now() - started)));
        if (!ltxPrintAttemptCurrent(session, attemptId)) return false;
        try {
            var pdfDoc = session.frame.contentDocument;
            if (pdfDoc && pdfDoc.contentType && pdfDoc.contentType !== 'application/pdf')
                await ltxWaitForPrintAssets(pdfDoc, Math.max(1, timeout - (Date.now() - started)));
        } catch (error) { console.info('[letter-print] browser PDF viewer does not expose its document; iframe load was used as readiness signal.'); }
        if (!ltxPrintAttemptCurrent(session, attemptId)) return false;
        var renderWindow = session.win || window;
        await new Promise(function (resolve) { renderWindow.requestAnimationFrame(function () { renderWindow.requestAnimationFrame(resolve); }); });
        if (!ltxPrintAttemptCurrent(session, attemptId)) return false;
        if (session.win) session.win.document.title = String(fileName || 'چاپ نامه');
        session.ready = true;
        ltxSetPrintStatus(session, 'PDF آماده است؛ پنجرهٔ چاپ باز می‌شود…');
        var printButton = session.overlay ? session.overlay.querySelector('.ltx-print-now') : session.win.document.getElementById('ltx-print-now');
        if (printButton) printButton.hidden = false;
        if (!ltxTriggerPrint(session)) throw new Error('مرورگر نتوانست پنجرهٔ چاپ را باز کند.');
        return true;
    } catch (error) {
        console.error('[letter-print] preparation failed', error);
        throw error;
    }
};
window.ltxPrintFail = function (id, message) {
    var session = window.ltxPrintSessions[id];
    if (!session) return false;
    if (!ltxEnsurePrintSurface(session)) return false;
    session.failed = true;
    session.attemptId = (session.attemptId || 0) + 1;
    session.ready = false;
    session.printRequested = false;
    var guidance = session.popupBlocked ? 'پنجرهٔ بازشو مسدود شد؛ popup این سایت را مجاز کنید. ' : '';
    ltxSetPrintError(session, guidance + String(message || 'خطا در آماده‌سازی چاپ'));
    return true;
};
// متن وضعیت را در حین آماده‌سازی به‌روز می‌کند (رندر طولانی PDF سمت سرور).
window.ltxPrintStatus = function (id, text) {
    var session = window.ltxPrintSessions[id];
    if (!session || session.failed) return false;
    if (!ltxEnsurePrintSurface(session)) return false;
    ltxSetPrintStatus(session, String(text || 'در حال آماده‌سازی نسخهٔ چاپ…'));
    return true;
};
window.ltxPrintWaitForAction = function (id) {
    var session = window.ltxPrintSessions[id];
    if (!session) return Promise.resolve('close');
    if (session.queuedAction) { var queued = session.queuedAction; session.queuedAction = null; return Promise.resolve(queued); }
    return new Promise(function (resolve) {
        session.resolveAction = resolve;
        session.closePoll = setInterval(function () {
            if (session.win && session.win.closed) {
                clearInterval(session.closePoll); session.closePoll = null;
                session.resolveAction = null; resolve('close');
            }
        }, 300);
    });
};
window.ltxPrintDirectPage = function (id) {
    var session = window.ltxPrintSessions[id];
    if (session && session.pdfLoaded && session.frame) {
        if (ltxTriggerPrint(session)) return true;
    }
    ltxEnsurePrintFallbackStyles();
    if (session) session.directFallback = true;
    document.body.classList.add('ltx-print-direct-page');
    try { window.print(); } catch (error) { console.error('[letter-print] direct page print failed', error); }
    if (session) {
        var done = function () { ltxPrintAfterPrint(session); };
        window.addEventListener('afterprint', done, { once: true });
        setTimeout(function () { if (!session.cleaned) { document.body.classList.remove('ltx-print-direct-page'); } }, 60000);
    } else {
        window.addEventListener('afterprint', function () { document.body.classList.remove('ltx-print-direct-page'); }, { once: true });
    }
    return true;
};
window.ltxPrintCancel = function (id) {
    var session = window.ltxPrintSessions[id];
    if (session) ltxPrintCleanup(session, true);
};

// ================== آدرس سرور API (مهم برای ورود موفق) ==================
// آدرس پیش‌فرض این استقرار: اگر هیچ نشانهٔ دیگری نبود، از همین استفاده می‌شود.
window.DEFAULT_API_BASE = 'http://192.168.30.104:8181';
// مسیر بررسی سلامت سرور — برای آزمایش واقعی «آیا API در دسترس است؟»
window.API_HEALTH_PATH = '/api/health';

// آیا نشانیِ ذخیره‌شده در مرورگر قابل اعتماد است؟
// ⚠️ ریشهٔ خطای «پس از زدن رمز، ارتباط با سرور برقرار نشد»:
// اگر روی خود سرور یک‌بار برنامه باز شده باشد، مقدار «http://localhost:5100» در localStorage
// ذخیره می‌شد و بعداً روی کامپیوتر کاربران همان نشانی محلی استفاده می‌شد؛ یعنی درخواست‌ها به
// کامپیوتر خودِ کاربر می‌رفت، نه به سرور. اینجا نشانی محلی روی دستگاه غیرمحلی نامعتبر اعلام می‌شود.
window.apiBaseIsSane = function (url) {
    if (!url) return false;
    try {
        var u = new URL(url, window.location.href);
        if (u.protocol !== 'http:' && u.protocol !== 'https:') return false;
        var pageHost = window.location.hostname;
        var pageIsLocal = (pageHost === 'localhost' || pageHost === '127.0.0.1');
        var urlIsLocal = (u.hostname === 'localhost' || u.hostname === '127.0.0.1');
        if (urlIsLocal && !pageIsLocal) return false;
        return true;
    } catch (e) { return false; }
};

// پورت‌های اجرای مستقیم پروژهٔ Blazor در Visual Studio. در این حالت رابط و API
// دو process مستقل هستند و UI نباید به‌علت یک مقدار قدیمی در localStorage به API
// سرور/نسخهٔ قبلی وصل شود؛ در غیر این صورت با F5 فقط Client بالا می‌آید و نتیجهٔ
// تغییرات جدید API دیده نمی‌شود.
window.inventoryIsLocalDevelopmentClient = function () {
    var host = window.location.hostname;
    var port = window.location.port;
    return (host === 'localhost' || host === '127.0.0.1') &&
        (port === '5210' || port === '7232');
};

window.inventoryApiBase = function (fallback) {
    var dflt = window.DEFAULT_API_BASE || fallback || 'http://localhost:5100';
    // ذخیرهٔ API فقط برای نصب‌های معمولی معتبر است. در Client توسعه، API محلیِ
    // متناظر باید قطعی انتخاب شود تا اجرای start.bat یا پاک‌سازی دستی مرورگر لازم نباشد.
    if (!window.inventoryIsLocalDevelopmentClient()) {
        try {
            var stored = localStorage.getItem('apiBaseUrl');
            if (window.apiBaseIsSane(stored)) return stored;
        } catch (e) { }
    }
    try {
        var h = window.location.host;
        // محیط پیش‌نمایش ابری: تبدیل پورت جاری به پورت API (5100)
        if (h.indexOf('.e2b.app') !== -1) {
            return window.location.protocol + '//' + h.replace(/^[0-9]+-/, '5100-');
        }
        // مسیر پایهٔ برنامه از base href — روی IIS اگر برنامه زیر یک Application/Virtual Directory
        // نصب شود (مثلاً http://server/erp/) سرور base href را «/erp/» می‌کند و API هم زیر همان مسیر است.
        var basePath = '';
        try { basePath = new URL(document.baseURI).pathname.replace(/\/+$/, ''); } catch (e) { }
        var sameOrigin = window.location.origin + basePath;
        var port = window.location.port;
        var host = window.location.hostname;
        if (host === 'localhost' || host === '127.0.0.1') {
            // روی خود سرور: انتشار تک‌سروره (IIS روی 80/443 یا پورت 5100/8181) هم‌مبدأ است.
            if (port === '' || port === '5100' || port === '8181') return sameOrigin;

            // Visual Studio: کلاینت روی 5210/7232 اجرا می‌شود و API روی 5100/7021.
            // قبلاً اینجا dflt (سرور 192.168...) برمی‌گشت؛ در نتیجه با وجود F5 کردن
            // API محلی، رابط توسعه همچنان به API قدیمی سرور وصل می‌شد.
            return window.location.protocol === 'https:'
                ? 'https://localhost:7021'
                : 'http://localhost:5100';
        }
        // هر میزبان دیگر (آی‌پی یا دامنه، با پورت یا بدون پورت — مثل IIS روی 80/443 یا سرور روی 8181):
        // UI و API هم‌مبدأ هستند. قبلاً حالت «بدون پورت» به http://localhost:5100 مرورگر کاربر می‌رفت
        // و روی IIS با binding پیش‌فرض هیچ درخواستی (از جمله ورود) به سرور نمی‌رسید.
        return sameOrigin;
    } catch (e) { }
    return dflt;
};

// آزمایش واقعی در دسترس بودن یک آدرس API (اگر سرور پاسخ ندهد، false برمی‌گردد)
window.apiBaseIsReachable = async function (base) {
    try {
        var url = String(base).replace(/\/+$/, '') + (window.API_HEALTH_PATH || '/api/health');
        var ctrl = ('AbortController' in window) ? new AbortController() : null;
        var timer = setTimeout(function () { try { ctrl && ctrl.abort(); } catch (e) { } }, 3000);
        var res = await fetch(url, { method: 'GET', cache: 'no-store', signal: ctrl ? ctrl.signal : undefined });
        clearTimeout(timer);
        if (!res || !res.ok) return false;
        var ct = res.headers.get('content-type') || '';
        return ct.indexOf('application/json') !== -1;
    } catch (e) { return false; }
};

// انتخاب آدرس API با «آزمایش موازی» همهٔ گزینه‌ها:
//   ۱) تشخیص خودکار (هم‌مبدأ)   ۲) نشانی ذخیره‌شده   ۳) آدرس پیش‌فرض سرور
// در اجرای توسعهٔ Client (5210/7232) فقط API محلی آزمایش می‌شود؛ fallback به
// 192.168... باعث می‌شد بدون اجرای API جدید، رابط بی‌صدا به نسخهٔ قدیمی سرور متصل شود.
window.inventoryResolveApiBase = async function (fallback) {
    var candidates = [];
    function push(u) {
        if (!u) return;
        u = String(u).trim().replace(/\/+$/, '');
        if (u && candidates.indexOf(u) === -1) candidates.push(u);
    }
    var localDevelopment = window.inventoryIsLocalDevelopmentClient();
    push(window.inventoryApiBase(fallback));
    if (!localDevelopment) {
        try { push(localStorage.getItem('apiBaseUrl')); } catch (e) { }
        push(window.DEFAULT_API_BASE);
        push(fallback);
        push('http://localhost:5100');
    }

    var results = await Promise.all(candidates.map(function (c) { return window.apiBaseIsReachable(c); }));
    for (var i = 0; i < candidates.length; i++) {
        if (results[i]) {
            try { localStorage.setItem('apiBaseUrl', candidates[i]); } catch (e) { }
            return candidates[i];
        }
    }
    // هیچ‌کدام پاسخ ندادند: حدس اول را نگه می‌داریم (پیام خطای دقیق‌تر به کاربر نشان داده می‌شود)
    return candidates[0] || window.DEFAULT_API_BASE || fallback || '';
};

// تشخیص صفحه کوچک‌تر از ~۱۲ اینچ (حالت موبایل/اندروید)
window.appShell = {
    isMobile: function () {
        return window.matchMedia('(max-width: 1279.98px)').matches;
    },
    _watching: [],
    watch: function (ref) {
        var mq = window.matchMedia('(max-width: 1279.98px)');
        var handler = function () {
            // پس از Dispose، مرجع .NET آزاد شده و فراخوانی آن خطای
            // «no tracked object with id» در کنسول می‌دهد؛ پس با پرچم متوقف می‌شود.
            if (handler.stopped) return;
            try { ref.invokeMethodAsync('OnScreenChanged', mq.matches); } catch (e) { handler.stopped = true; }
        };
        if (mq.addEventListener) mq.addEventListener('change', handler);
        else mq.addListener(handler);
        window.appShell._watching.push({ mq: mq, handler: handler });
    },
    unwatch: function () {
        var list = window.appShell._watching || [];
        for (var i = 0; i < list.length; i++) {
            var w = list[i];
            w.handler.stopped = true;
            try {
                if (w.mq.removeEventListener) w.mq.removeEventListener('change', w.handler);
                else w.mq.removeListener(w.handler);
            } catch (e) { }
        }
        window.appShell._watching = [];
    }
};
