// توابع تعامل جاوااسکریپت ماژول پروژه‌ها (چاپ گزارش / دانلود و نمایش پیوست‌ها)
// نکته: این کد عمداً در فایل جداگانه نگهداری می‌شود تا رشته‌های حاوی تگ‌های HTML
// (مانند تگ بستهٔ style و body) بلاک inline script را در index.html نشکنند.

(function () {
    'use strict';

    // ---------- ابزار: اسکیپ HTML ----------
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // ---------- ابزار: خواندن موقعیت و ابعاد عنصر در صفحه ----------
    window.getElementRect = function (el) {
        if (!el) return null;
        var rect = el.getBoundingClientRect();
        return {
            left: rect.left,
            top: rect.top,
            right: rect.right,
            bottom: rect.bottom,
            width: rect.width,
            height: rect.height
        };
    };

    // ---------- ابزار: خواندن مقدار با هر دو حالت camelCase و PascalCase ----------
    function pick(obj, name) {
        if (!obj) return undefined;
        if (obj[name] !== undefined) return obj[name];
        var pascal = name.charAt(0).toUpperCase() + name.slice(1);
        if (obj[pascal] !== undefined) return obj[pascal];
        var camel = name.charAt(0).toLowerCase() + name.slice(1);
        return obj[camel];
    }

    // ---------- ساخت HTML سند چاپ ----------
    function buildReportHtml(opt) {
        var title = pick(opt, 'title') || 'گزارش';
        var subTitle = pick(opt, 'subTitle') || '';
        var metaList = pick(opt, 'meta') || [];
        var headers = pick(opt, 'headers') || [];
        var rows = pick(opt, 'rows') || [];
        var footerTotal = pick(opt, 'footerTotal') || '';
        var printedAt = pick(opt, 'printedAt') || '';
        var printedBy = pick(opt, 'printedBy') || '';

        var meta = metaList.map(function (m) {
            return '<span class="chip">' + esc(m) + '</span>';
        }).join('');

        var head = headers.map(function (h) {
            return '<th>' + esc(h) + '</th>';
        }).join('');

        var body = rows.map(function (r) {
            var cells = r || [];
            var tds = cells.map(function (c, i) {
                var cls = (i === cells.length - 1) ? 'desc' : 'num';
                return '<td class="' + cls + '">' + esc(c) + '</td>';
            }).join('');
            return '<tr>' + tds + '</tr>';
        }).join('');

        var foot = footerTotal
            ? '<tfoot><tr><td colspan="' + headers.length + '">' + esc(footerTotal) + '</td></tr></tfoot>'
            : '';

        var css = [
            '* { box-sizing: border-box; }',
            'body { font-family: Vazirmatn, Tahoma, "Segoe UI", sans-serif; color: #1e293b; margin: 0; padding: 18px 22px; font-size: 11.5px; }',
            '.brand { display: flex; justify-content: space-between; align-items: baseline; border-bottom: 2.5px solid #4f46e5; padding-bottom: 8px; margin-bottom: 12px; }',
            '.brand .sys { font-size: 10px; color: #64748b; }',
            'h1 { font-size: 19px; margin: 0; color: #312e81; }',
            '.sub { font-size: 13px; color: #475569; margin: 2px 0 10px; font-weight: 700; }',
            '.chips { margin-bottom: 12px; line-height: 2.1; }',
            '.chip { display: inline-block; background: #eef2ff; border: 1px solid #c7d2fe; color: #3730a3; border-radius: 999px; padding: 1px 12px; margin-left: 6px; font-size: 10.5px; font-weight: 700; }',
            'table { width: 100%; border-collapse: collapse; }',
            'th { background: #312e81; color: #fff; font-weight: 700; padding: 6px 7px; border: 1px solid #312e81; font-size: 11px; white-space: nowrap; }',
            'td { border: 1px solid #cbd5e1; padding: 5px 7px; vertical-align: top; }',
            'td.num { text-align: center; white-space: nowrap; }',
            'td.desc { text-align: right; }',
            'tbody tr:nth-child(even) td { background: #f8fafc; }',
            'tfoot td { background: #fef3c7; border: 1px solid #d4a106; font-weight: 800; padding: 7px; font-size: 12px; color: #78350f; }',
            '.sign { margin-top: 34px; display: flex; justify-content: space-between; color: #475569; }',
            '.sign .line { display: inline-block; width: 170px; border-top: 1px dashed #94a3b8; padding-top: 4px; text-align: center; }',
            '.footer-meta { margin-top: 12px; font-size: 9.5px; color: #94a3b8; border-top: 1px solid #e2e8f0; padding-top: 6px; display: flex; justify-content: space-between; }',
            '@page { size: A4 portrait; margin: 10mm; }',
            '@media print { body { padding: 0; } thead { display: table-header-group; } tr { break-inside: avoid; } }'
        ].join('\n');

        var parts = [
            '<!DOCTYPE html>',
            '<html lang="fa" dir="rtl">',
            '<head>',
            '<meta charset="utf-8" />',
            '<title>' + esc(title) + '<' + '/title>',
            '<style>' + css + '<' + '/style>',
            '<' + '/head>',
            '<body>',
            '<div class="brand"><h1>' + esc(title) + '<' + '/h1><span class="sys">نرم افزار یکپارچه فروغ آریا<' + '/span><' + '/div>',
            subTitle ? '<div class="sub">' + esc(subTitle) + '<' + '/div>' : '',
            meta ? '<div class="chips">' + meta + '<' + '/div>' : '',
            '<table><thead><tr>' + head + '<' + '/tr><' + '/thead><tbody>' + body + '<' + '/tbody>' + foot + '<' + '/table>',
            '<div class="sign"><span>تحویل‌دهنده: <span class="line"><' + '/span><' + '/span>' +
            '<span>تأییدکننده: <span class="line"><' + '/span><' + '/span><' + '/div>',
            '<div class="footer-meta"><span>تاریخ چاپ: ' + esc(printedAt) + '<' + '/span>' +
            '<span>چاپ‌کننده: ' + esc(printedBy) + '<' + '/span><' + '/div>',
            '<' + '/body>',
            '<' + '/html>'
        ];
        return parts.join('\n');
    }

    // ---------- چاپ با iframe پنهان ----------
    function printViaIframe(html) {
        var old = document.getElementById('app-print-frame');
        if (old && old.parentNode) old.parentNode.removeChild(old);

        var frame = document.createElement('iframe');
        frame.id = 'app-print-frame';
        frame.setAttribute('aria-hidden', 'true');
        frame.style.position = 'fixed';
        frame.style.right = '0';
        frame.style.bottom = '0';
        frame.style.width = '0';
        frame.style.height = '0';
        frame.style.border = '0';
        // iframe نباید hidden باشد؛ Chrome/Edge در این حالت پنجرهٔ Print Preview را باز نمی‌کنند.
        // آن را خارج از دید کاربر نگه می‌داریم اما برای موتور چاپ قابل مشاهده می‌ماند.
        frame.style.left = '-10000px';
        frame.style.top = '0';
        frame.style.width = '100vw';
        frame.style.height = '100vh';
        frame.style.opacity = '1';
        frame.style.pointerEvents = 'none';
        document.body.appendChild(frame);

        var doc = frame.contentWindow || frame.contentDocument;
        if (doc && doc.document) doc = doc.document;
        if (!doc) throw new Error('iframe-unavailable');

        doc.open();
        doc.write(html);
        doc.close();

        var cleanedUp = false;
        function cleanup() {
            if (cleanedUp) return;
            cleanedUp = true;
            setTimeout(function () {
                if (frame && frame.parentNode) frame.parentNode.removeChild(frame);
            }, 1000);
        }

        var printStarted = false;
        function doPrint() {
            if (printStarted) return;
            printStarted = true;
            try {
                var win = frame.contentWindow;
                win.focus();
                if (win.matchMedia) {
                    var mq = win.matchMedia('print');
                    if (mq.addEventListener) {
                        mq.addEventListener('change', function (e) { if (!e.matches) cleanup(); });
                    }
                }
                win.onafterprint = cleanup;
                win.print();
                setTimeout(cleanup, 60000);
            } catch (e) {
                console.error('printTableReport (iframe print):', e);
                cleanup();
                throw e;
            }
        }

        if (frame.contentWindow && frame.contentWindow.document.readyState === 'complete') {
            setTimeout(doPrint, 150);
        } else {
            frame.onload = function () { setTimeout(doPrint, 150); };
            setTimeout(doPrint, 700);
        }
    }

    // ---------- چاپ با پنجرهٔ جدا ----------
    function printViaWindow(html) {
        var w = window.open('', '_blank', 'width=1020,height=760');
        if (!w) return false;
        w.document.open();
        w.document.write(html);
        w.document.close();
        w.focus();
        setTimeout(function () {
            try { w.print(); } catch (e) { console.error(e); }
        }, 350);
        w.onafterprint = function () { try { w.close(); } catch (e) { } };
        return true;
    }

    /**
     * چاپ گزارش جدولی (ماژول پروژه‌ها).
     */
    window.printTableReport = function (opt) {
        try {
            if (!opt) { alert('داده‌ای برای چاپ ارسال نشد.'); return false; }
            var html = buildReportHtml(opt);
            try {
                printViaIframe(html);
                return true;
            } catch (inner) {
                if (printViaWindow(html)) return true;
                alert('پنجره چاپ باز نشد — لطفاً مسدودکنندهٔ پاپ‌آپ مرورگر را برای این سایت غیرفعال کنید.');
                return false;
            }
        } catch (e) {
            console.error('printTableReport error:', e);
            alert('چاپ گزارش با خطا مواجه شد: ' + (e && e.message ? e.message : e));
            return false;
        }
    };

    // ---------- تبدیل Base64 به Blob ----------
    function base64ToBlob(base64, contentType) {
        var bin = atob(base64);
        var len = bin.length;
        var bytes = new Uint8Array(len);
        for (var i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
        return new Blob([bytes], { type: contentType || 'application/octet-stream' });
    }

    /**
     * ورود انبوه آرشیو — مرحلهٔ ۱: انتخاب پوشه و بررسی نام فایل‌ها.
     * هیچ فایلی در این مرحله آپلود نمی‌شود؛ فقط فهرست مسیرها و حجم‌ها به سرور می‌رود.
     */
    window.importPickFolder = async function (dotNetRef, callbackName, apiBase, token, rootFolderId) {
        var report = function (obj) {
            if (dotNetRef && callbackName) {
                try { dotNetRef.invokeMethodAsync(callbackName, obj); } catch (e) { console.error(e); }
            }
        };
        var fatal = function (message) {
            report({ stage: 'done', fatal: message || 'خطای نامشخص.' });
        };

        var input;
        try {
            input = document.createElement('input');
            input.type = 'file';
            input.setAttribute('webkitdirectory', '');
            input.setAttribute('directory', '');
            input.multiple = true;
            input.style.display = 'none';
            document.body.appendChild(input);
        } catch (e) {
            fatal('ساخت پنجرهٔ انتخاب پوشه ممکن نشد: ' + e.message);
            return;
        }

        var removeInput = function () {
            try { if (input && input.parentNode) input.parentNode.removeChild(input); } catch (e) { }
        };

        input.addEventListener('change', async function () {
            var files;
            try { files = Array.prototype.slice.call(input.files || []); } catch (e) { files = []; }
            removeInput();

            if (!files || files.length === 0) {
                fatal('فایلی در پوشهٔ انتخاب‌شده پیدا نشد. پوشه‌ای را انتخاب کنید که داخلش فایل باشد.');
                return;
            }

            // مسیر نسبی هر فایل — ساختار پوشه‌ها از همین‌جا بازسازی می‌شود
            var entries = files.map(function (f, i) {
                return { file: f, rel: f.webkitRelativePath || f.name || ('file-' + i) };
            });

            try {
                report({ stage: 'picked', total: entries.length });

                // پوشهٔ مقصدِ انتخاب‌شده هم به سرور می‌رود؛ وگرنه فایل‌ها به ریشهٔ آرشیو می‌رفتند
                var resp = await fetch(apiBase + '/batches', {
                    method: 'POST',
                    headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' },
                    body: JSON.stringify({ createMissingFolders: true, rootFolderId: (rootFolderId || null) })
                });
                if (!resp.ok) {
                    var m = '';
                    try { m = (await resp.json()).message || ''; } catch (e) { }
                    fatal(m || ('ساخت نوبت ورود ناموفق بود (HTTP ' + resp.status + ')'));
                    return;
                }
                var batch = await resp.json();
                report({ stage: 'batch', batchId: batch.id, total: entries.length });

                // بررسی نام‌ها دسته‌ای — هزار فایل در هر درخواست.
                // سرور در هر پاسخ، شمارش کل نوبت را برمی‌گرداند، پس دستهٔ آخر خلاصهٔ نهایی است.
                var CHUNK = 1000;
                var summary = null;
                for (var c = 0; c < entries.length; c += CHUNK) {
                    var slice = entries.slice(c, c + CHUNK).map(function (e) {
                        return { relativePath: e.rel, sizeBytes: e.file.size };
                    });
                    var pr = await fetch(apiBase + '/batches/' + batch.id + '/preview', {
                        method: 'POST',
                        headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' },
                        body: JSON.stringify({ items: slice })
                    });
                    if (!pr.ok) {
                        var pm = '';
                        try { pm = (await pr.json()).message || ''; } catch (e) { }
                        fatal(pm || ('بررسی نام فایل‌ها ناموفق بود (HTTP ' + pr.status + ')'));
                        return;
                    }
                    summary = await pr.json();
                    report({
                        stage: 'preview',
                        batchId: batch.id,
                        index: Math.min(c + CHUNK, entries.length),
                        total: entries.length
                    });
                }

                // فهرست فایل‌های آماده برای مرحلهٔ ورود نگه داشته می‌شود.
                // فایل‌های مشکل‌دار و تکراری اصلاً ارسال نمی‌شوند — دلیلشان همین‌جا
                // در گزارش آمده و فرستادنشان فقط ترافیک اضافه و پیام خطای خام است.
                var bad = {};
                ((summary && summary.problems) || []).forEach(function (p) { bad[p.relativePath] = true; });
                var readyEntries = entries.filter(function (e) { return !bad[e.rel]; });
                window.__docImport = {
                    batchId: batch.id, entries: readyEntries, all: entries.length, cancelled: false
                };

                report({
                    stage: 'previewed',
                    batchId: batch.id,
                    total: summary ? summary.total : entries.length,
                    ready: summary ? summary.ready : 0,
                    failed: summary ? summary.failed : 0,
                    skipped: summary ? summary.skipped : 0,
                    duplicate: summary ? summary.duplicate : 0,
                    problems: (summary && summary.problems ? summary.problems : []).slice(0, 200)
                });
            } catch (err) {
                fatal((err && err.message) ? err.message : String(err));
            }
        });

        input.addEventListener('cancel', function () {
            removeInput();
            report({ stage: 'done', cancelled: true });
        });

        try { input.click(); }
        catch (e) { removeInput(); fatal('باز کردن پنجرهٔ انتخاب پوشه ممکن نشد: ' + e.message); }
    };

    /**
     * ورود انبوه — مرحلهٔ ۲: فرستادن فایل‌های آماده، یکی‌یکی.
     * هر فایل در همان درخواست وارد آرشیو می‌شود؛ اگر مرورگر بسته شود،
     * با انتخاب دوبارهٔ همان پوشه، فایل‌های واردشده «تکراری» گزارش می‌شوند.
     */
    window.importRun = async function (dotNetRef, callbackName, apiBase, token) {
        var report = function (obj) {
            if (dotNetRef && callbackName) {
                try { dotNetRef.invokeMethodAsync(callbackName, obj); } catch (e) { console.error(e); }
            }
        };

        var session = window.__docImport;
        if (!session || !session.entries || session.entries.length === 0) {
            report({ stage: 'done', fatal: 'پوشه‌ای انتخاب نشده است. اول «انتخاب پوشه» را بزنید.' });
            return;
        }

        session.cancelled = false;
        var imported = 0, failed = 0, duplicate = 0, skipped = 0, failedNames = [];

        for (var i = 0; i < session.entries.length; i++) {
            if (session.cancelled) {
                report({ stage: 'done', cancelled: true, imported: imported, failed: failed,
                         duplicate: duplicate, skipped: skipped, total: session.entries.length, batchId: session.batchId });
                return;
            }

            var e = session.entries[i];
            var f = e.file;
            var name = e.rel.split('/').pop();

            if (!f || f.size === 0) {
                skipped++;
                report({ stage: 'progress', name: name, index: i + 1, total: session.entries.length,
                         imported: imported, failed: failed, duplicate: duplicate, skipped: skipped,
                         message: 'فایل خالی است' });
                continue;
            }

            var fd = new FormData();
            fd.append('file', f, name);
            fd.append('relativePath', e.rel);

            try {
                var resp = await fetch(apiBase + '/batches/' + session.batchId + '/files', {
                    method: 'POST',
                    headers: { 'Authorization': 'Bearer ' + token },
                    body: fd
                });
                var msg = '';
                try { var j = await resp.json(); msg = j.messageFa || j.message || ''; } catch (ex) { }

                if (resp.ok && j && j.status === 'Imported') {
                    imported++;
                    e.done = true;
                } else if (resp.ok && j && j.duplicate) {
                    duplicate++;
                    e.done = true;
                } else if (resp.ok) {
                    failed++;
                    if (failedNames.length < 20) failedNames.push(name + ': ' + (msg || 'وارد نشد'));
                } else {
                    failed++;
                    if (failedNames.length < 20) failedNames.push(name + ': ' + (msg || ('HTTP ' + resp.status)));
                }

                report({ stage: 'progress', name: name, index: i + 1, total: session.entries.length,
                         imported: imported, failed: failed, duplicate: duplicate, skipped: skipped,
                         message: msg });
            } catch (err) {
                failed++;
                var em = (err && err.message) ? err.message : String(err);
                if (failedNames.length < 20) failedNames.push(name + ': ' + em);
                report({ stage: 'progress', name: name, index: i + 1, total: session.entries.length,
                         imported: imported, failed: failed, duplicate: duplicate, skipped: skipped,
                         message: em });
            }
        }

        try {
            await fetch(apiBase + '/batches/' + session.batchId + '/finish', {
                method: 'POST',
                headers: { 'Authorization': 'Bearer ' + token }
            });
        } catch (e) { }

        report({ stage: 'done', batchId: session.batchId, total: session.entries.length,
                 imported: imported, failed: failed, duplicate: duplicate, skipped: skipped,
                 failedNames: failedNames });
    };

    /** لغو آپلود بین دو فایل. فایل‌های واردشده برنمی‌گردند. */
    window.importCancel = function () {
        if (window.__docImport) window.__docImport.cancelled = true;
    };


    /** دانلود فایل امن از داده Base64 — تعیین MIME تایپ دقیق اکسل و شلیک رویداد استاندارد کاربر */
    window.downloadBlob = function (fileName, contentType, base64) {
        try {
            var mime = contentType || 'application/octet-stream';
            // تنظیم تایپ رسمی اکسل برای جلوگیری از هشدار ناامنی مرورگر
            if (fileName && fileName.toLowerCase().endsWith('.xlsx')) {
                mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
            }
            var blob = base64ToBlob(base64, mime);
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = fileName || 'download.xlsx';
            a.rel = 'noopener';
            a.style.display = 'none';
            document.body.appendChild(a);

            // شلیک رویداد ساختگی کلیک کاربر جهت جلب اعتماد مرورگر
            var evt = new MouseEvent('click', {
                bubbles: true,
                cancelable: true,
                view: window
            });
            a.dispatchEvent(evt);

            setTimeout(function () {
                if (a.parentNode) a.parentNode.removeChild(a);
                URL.revokeObjectURL(url);
            }, 10000);
            return true;
        } catch (e) {
            console.error('downloadBlob error:', e);
            return false;
        }
    };

    /** باز کردن فایل (تصاویر و…) در تب جدید — از داده Base64 */
    window.openBlob = function (contentType, base64) {
        try {
            var url = URL.createObjectURL(base64ToBlob(base64, contentType));
            var win = window.open(url, '_blank');
            if (!win) {
                var a = document.createElement('a');
                a.href = url;
                a.target = '_blank';
                a.rel = 'noopener';
                document.body.appendChild(a);
                a.click();
                a.remove();
            }
            setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
            return true;
        } catch (e) {
            console.error('openBlob error:', e);
            return false;
        }
    };

    // ============================================================
    //  امنیت حضور و غیاب:
    //  ۱) شناسه‌ی یکتای دستگاه (Device ID) — ذخیره در localStorage
    //     (مرورگر MAC واقعی نمی‌دهد؛ این شناسه + IP + User-Agent همان نقش را دارد)
    //  ۲) موقعیت جغرافیایی (Geolocation API) برای محدوده‌ی مکانی مجاز
    // ============================================================

    var ATT_DEVICE_KEY = 'att_device_id_v1';

    /** خواندن یا ساخت شناسه‌ی یکتای دستگاه برای این مرورگر. */
    window.attGetDeviceId = function () {
        try {
            var id = localStorage.getItem(ATT_DEVICE_KEY);
            if (id && id.length >= 8) return id;
            id = 'dev-' + Date.now().toString(36) + '-' +
                Math.random().toString(36).slice(2, 10) + '-' +
                Math.random().toString(36).slice(2, 10);
            localStorage.setItem(ATT_DEVICE_KEY, id);
            return id;
        } catch (e) {
            // اگر localStorage در دسترس نبود (مثلاً حالت خصوصی)، یک شناسه موقت
            return 'dev-tmp-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
        }
    };

    /** حذف شناسه دستگاه (در صورت نیاز کاربر/مدیر به ریست). */
    window.attResetDeviceId = function () {
        try { localStorage.removeItem(ATT_DEVICE_KEY); } catch (e) { }
    };

    /**
     * موقعیت جغرافیایی فعلی (طول/عرض).
     * اگر کاربر اجازه ندهد یا در دسترس نباشد null برمی‌گرداند (هشدار مکانی ثبت نمی‌شود).
     * @returns {Promise<{lat:number, lng:number}|null>}
     */
    window.attGetPosition = function (timeoutMs) {
        return new Promise(function (resolve) {
            try {
                if (!navigator.geolocation) { resolve(null); return; }
                var to = (typeof timeoutMs === 'number' && timeoutMs > 0) ? timeoutMs : 8000;
                navigator.geolocation.getCurrentPosition(
                    function (pos) {
                        resolve({
                            lat: pos.coords.latitude,
                            lng: pos.coords.longitude
                        });
                    },
                    function () { resolve(null); },
                    { enableHighAccuracy: true, timeout: to, maximumAge: 30000 }
                );
            } catch (e) {
                resolve(null);
            }
        });
    };


})();

// =====================================================================
//  فیلد «فقط عدد» — تلفن / فکس / موبایل
//  هر <input data-digits> فقط رقم می‌پذیرد:
//   • ارقام فارسی/عربی به لاتین تبدیل می‌شوند (۰۹۱۲… ← 0912…)
//   • حروف و کاراکترهای دیگر (تایپ‌شده یا Paste‌شده) حذف می‌شوند
//   • data-digits="tel" اجازهٔ کاراکترهای متداول شمارهٔ ثابت را هم می‌دهد: + - ( ) و فاصله
//  چون شنونده در فاز capture ثبت می‌شود، قبل از هندلر Blazor اجرا می‌شود و
//  بنابراین مقدار پاک‌شده به مدل C# می‌رسد (bind سالم می‌ماند).
// =====================================================================
(function () {
    'use strict';

    var FA = '۰۱۲۳۴۵۶۷۸۹';
    var AR = '٠١٢٣٤٥٦٧٨٩';

    function normalizeDigits(s) {
        var out = '';
        for (var i = 0; i < s.length; i++) {
            var ch = s[i];
            var fi = FA.indexOf(ch);
            if (fi >= 0) { out += fi; continue; }
            var ai = AR.indexOf(ch);
            if (ai >= 0) { out += ai; continue; }
            out += ch;
        }
        return out;
    }

    function sanitize(value, mode) {
        var s = normalizeDigits(value || '');
        var allowed = (mode === 'tel') ? /[0-9+\-() ]/ : /[0-9]/;
        var out = '';
        for (var i = 0; i < s.length; i++) {
            if (allowed.test(s[i])) out += s[i];
        }
        return out;
    }

    function isDigitInput(el) {
        return el && el.tagName === 'INPUT' && el.hasAttribute && el.hasAttribute('data-digits');
    }

    function apply(el) {
        var mode = el.getAttribute('data-digits') || '';
        var before = el.value;
        var after = sanitize(before, mode);
        if (before === after) return;

        // حفظ محل مکان‌نما بعد از پاک‌سازی
        var pos = el.selectionStart == null ? after.length : el.selectionStart;
        var removedBefore = before.slice(0, pos).length - sanitize(before.slice(0, pos), mode).length;
        el.value = after;
        try {
            var np = Math.max(0, pos - removedBefore);
            el.setSelectionRange(np, np);
        } catch (e) { /* برخی نوع‌های input از setSelectionRange پشتیبانی نمی‌کنند */ }
    }

    document.addEventListener('input', function (e) {
        if (isDigitInput(e.target)) apply(e.target);
    }, true);

    document.addEventListener('change', function (e) {
        if (isDigitInput(e.target)) apply(e.target);
    }, true);

    // جلوگیری از تایپ کاراکتر غیرمجاز (تجربهٔ بهتر — پاک‌سازی بالا هم پشتیبان است)
    document.addEventListener('keypress', function (e) {
        var el = e.target;
        if (!isDigitInput(el) || e.ctrlKey || e.metaKey || e.altKey) return;
        var ch = e.key;
        if (!ch || ch.length !== 1) return;
        var mode = el.getAttribute('data-digits') || '';
        if (sanitize(ch, mode) === '') e.preventDefault();
    }, true);
})();
