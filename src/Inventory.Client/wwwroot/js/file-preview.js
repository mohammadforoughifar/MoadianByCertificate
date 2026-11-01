// A single implementation for authorized preview loading, local PDF rendering and lifecycle.
// Tokens are only sent in Authorization headers; never put them in blob URLs/query strings.
(function () {
    'use strict';
    const assetRoot = new URL('../', document.currentScript.src);
    const requests = new Map(), viewers = new Map(), modalHandlers = new Map();
    let pdfLibrary;
    function revoke(url) { if (url && url.startsWith('blob:')) URL.revokeObjectURL(url); }
    function failure(code, message, status) { return { success: false, code, message, status: status || 0 }; }
    async function responseError(response, operation) {
        let message = response.status === 401 ? 'نشست شما منقضی شده است؛ دوباره وارد شوید.'
            : response.status === 403 ? 'اجازه مشاهده این فایل را ندارید.'
            : response.status === 404 ? 'فایل یا مسیر پیش‌نمایش در سرور یافت نشد.'
            : 'پیش‌نمایش انجام نشد (خطای ' + response.status + ').';
        let body = {};
        try { body = await response.json(); } catch (_) { }
        if (typeof body.message === 'string' && body.message.trim()) message = body.message.substring(0, 600);
        return { success: false, status: response.status, message,
            code: typeof body.code === 'string' ? body.code : 'HTTP_ERROR',
            documentId: Number.isInteger(body.documentId) ? body.documentId : 0 };
    }
    function abort(key) { const request = requests.get(key); if (request) request.abort(); }
    async function load(url, token, kind, mimeHint, key) {
        abort(key);
        const controller = new AbortController(); requests.set(key, controller);
        let timedOut = false;
        const timeout = setTimeout(() => { timedOut = true; controller.abort(); }, 90000);
        try {
            const headers = { Accept: kind === 'html' ? 'text/html' : '*/*' };
            if (token) headers.Authorization = 'Bearer ' + token;
            const response = await fetch(url, { headers, signal: controller.signal, cache: 'no-store', credentials: 'same-origin' });
            if (!response.ok) return await responseError(response, 'preview');
            // Catch a stale/mistyped URL returning the SPA/login HTML with HTTP 200.
            if (!/^\s*inline\b/i.test(response.headers.get('Content-Disposition') || ''))
                return failure('INVALID_PREVIEW_RESPONSE', 'سرور به‌جای پیش‌نمایش فایل پاسخ نامعتبر برگرداند؛ مسیر API و نسخهٔ جدید برنامه را بررسی کنید.', response.status);
            let mime = (response.headers.get('Content-Type') || '').split(';')[0].trim().toLowerCase();
            if (!mime || mime === 'application/octet-stream') mime = mimeHint || 'application/octet-stream';
            if (kind === 'html' && mime !== 'text/html' || kind === 'pdf' && mime !== 'application/pdf'
                || kind === 'image' && !mime.startsWith('image/'))
                return failure('INVALID_PREVIEW_MIME', 'نوع پاسخ سرور با این فایل تطابق ندارد؛ پیش‌نمایش ساخته نشد.', response.status);
            const buffer = await response.arrayBuffer();
            if (controller.signal.aborted) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
            if (buffer.byteLength === 0) return failure('EMPTY_FILE', 'فایل خالی است.');
            if (kind === 'pdf') {
                const head = new TextDecoder('latin1').decode(new Uint8Array(buffer, 0, Math.min(1024, buffer.byteLength)));
                if (!head.includes('%PDF-')) return failure('INVALID_PDF', 'محتوای فایل PDF معتبر نیست یا آسیب دیده است.');
            }
            if (kind === 'text') return { success: true, text: new TextDecoder('utf-8').decode(buffer), mime };
            return { success: true, url: URL.createObjectURL(new Blob([buffer], { type: mime })), mime };
        } catch (error) {
            return timedOut ? failure('TIMEOUT', 'زمان دریافت پیش‌نمایش به پایان رسید؛ اتصال و سرور را بررسی و دوباره تلاش کنید.')
                : error.name === 'AbortError' ? failure('CANCELLED', 'پیش‌نمایش لغو شد.')
                : failure('NETWORK_ERROR', 'دریافت پیش‌نمایش انجام نشد؛ اتصال شبکه و دسترسی به سرور را بررسی کنید.');
        } finally {
            clearTimeout(timeout);
            if (requests.get(key) === controller) requests.delete(key);
        }
    }
    async function bounded(operation, milliseconds) {
        let timer;
        try {
            return await Promise.race([operation, new Promise((_, reject) => {
                timer = setTimeout(() => { const error = new Error('Preview timeout'); error.name = 'PreviewTimeout'; reject(error); }, milliseconds);
            })]);
        } finally { clearTimeout(timer); }
    }
    async function library() {
        if (!pdfLibrary) {
            pdfLibrary = import(new URL('lib/pdfjs/pdf.js?v=6.3.289', assetRoot).href).then(lib => {
                lib.GlobalWorkerOptions.workerSrc = new URL('lib/pdfjs/pdf.worker.js?v=6.3.289', assetRoot).href;
                return lib;
            }).catch(error => { pdfLibrary = null; throw error; });
        }
        return pdfLibrary;
    }
    async function destroyPdf(key) {
        const viewer = viewers.get(key); if (!viewer) return;
        const host = viewer.host;
        viewers.delete(key); viewer.closed = true; viewer.generation++;
        if (viewer.render) { try { viewer.render.cancel(); } catch (_) { } }
        try { if (viewer.task) await viewer.task.destroy(); else if (viewer.document) await viewer.document.destroy(); } catch (_) { }
        host.replaceChildren();
    }
    async function renderPdf(key, number, zoom) {
        const viewer = viewers.get(key);
        if (!viewer || viewer.closed || !viewer.document) return failure('CANCELLED', 'نمایشگر بسته شد.');
        const host = viewer.host;
        const generation = ++viewer.generation;
        if (viewer.render) { try { viewer.render.cancel(); } catch (_) { } }
        const previous = viewer.queue || Promise.resolve();
        const task = (async () => {
            try {
                await previous;
                if (viewer.closed || generation !== viewer.generation || !host.isConnected) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
                const page = await viewer.document.getPage(Math.max(1, Math.min(viewer.document.numPages, number)));
                if (viewer.closed || generation !== viewer.generation) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
                const unscaled = page.getViewport({ scale: 1 });
                const fit = Math.max(180, host.clientWidth - 32) / unscaled.width;
                const viewport = page.getViewport({ scale: fit * Math.max(.25, Math.min(3, zoom)) });
                const pixelRatio = Math.min(window.devicePixelRatio || 1, 2, 8192 / Math.max(viewport.width, viewport.height), Math.sqrt(16000000 / (viewport.width * viewport.height)));
                const canvas = document.createElement('canvas');
                canvas.className = 'fpv-pdf-canvas'; canvas.setAttribute('aria-label', 'صفحه ' + number + ' PDF');
                canvas.width = Math.max(1, Math.floor(viewport.width * pixelRatio));
                canvas.height = Math.max(1, Math.floor(viewport.height * pixelRatio));
                canvas.style.width = Math.floor(viewport.width) + 'px'; canvas.style.height = Math.floor(viewport.height) + 'px';
                const context = canvas.getContext('2d', { alpha: false });
                if (!context) return failure('PDF_CANVAS_UNAVAILABLE', 'مرورگر امکان نمایش PDF را ندارد؛ از Edge، Chrome یا Firefox به‌روز استفاده کنید.');
                const render = page.render({ canvasContext: context, viewport,
                    transform: pixelRatio !== 1 ? [pixelRatio, 0, 0, pixelRatio, 0, 0] : null });
                viewer.render = render;
                await bounded(render.promise, 45000);
                if (viewer.closed || generation !== viewer.generation || !host.isConnected) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
                host.replaceChildren(canvas); page.cleanup();
                return { success: true, pages: viewer.document.numPages };
            } catch (error) {
                if (viewer.render) { try { viewer.render.cancel(); } catch (_) { } }
                return viewer.closed || generation !== viewer.generation || error.name === 'RenderingCancelledException'
                    ? failure('CANCELLED', 'پیش‌نمایش لغو شد.')
                    : failure('PDF_RENDER_FAILED', 'نمایش این صفحهٔ PDF ممکن نشد؛ فایل آسیب‌دیده یا دارای محتوای غیرقابل پشتیبانی است.');
            }
        })();
        viewer.queue = task.then(() => {}); return await task;
    }
    async function openPdf(host, url, password, key) {
        key = key || host;
        await destroyPdf(key);
        // Stable component key: a Blazor render can remove the DOM before Close() finishes.
        const viewer = { generation: 0, closed: false, host }; viewers.set(key, viewer);
        try {
            const lib = await bounded(library(), 45000);
            if (viewer.closed || !host.isConnected) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
            viewer.task = lib.getDocument({ url, password: password || undefined,
                cMapUrl: new URL('lib/pdfjs/cmaps/', assetRoot).href, cMapPacked: true,
                standardFontDataUrl: new URL('lib/pdfjs/standard_fonts/', assetRoot).href,
                wasmUrl: new URL('lib/pdfjs/wasm/', assetRoot).href,
                isEvalSupported: false, enableXfa: false });
            // A PDF must never navigate the app, open links, execute document JS or print itself.
            viewer.document = await bounded(viewer.task.promise, 60000);
            if (viewer.closed || !host.isConnected) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
            return await renderPdf(key, 1, 1);
        } catch (error) {
            const closed = viewer.closed;
            await destroyPdf(key);
            if (closed) return failure('CANCELLED', 'پیش‌نمایش لغو شد.');
            if (error.name === 'PasswordException') return failure('PDF_PASSWORD_REQUIRED', password ? 'رمز فایل PDF درست نیست؛ دوباره وارد کنید.' : 'این فایل PDF رمز دارد؛ رمز خودِ فایل را وارد کنید.');
            if (error.name === 'PreviewTimeout') return failure('PDF_TIMEOUT', 'زمان بارگذاری نمایشگر یا فایل PDF به پایان رسید؛ اتصال سرور را بررسی و دوباره تلاش کنید.');
            if (error.name === 'InvalidPDFException') return failure('INVALID_PDF', 'ساختار فایل PDF نامعتبر یا آسیب‌دیده است.');
            return failure('PDF_VIEWER_FAILED', 'نمایشگر PDF بارگذاری نشد؛ فایل‌های lib/pdfjs را همراه برنامه منتشر کنید و از مرورگر به‌روز استفاده کنید.');
        }
    }
    function attachModal(element, dotnet, key) {
        key = key || element;
        if (!element || modalHandlers.has(key)) return;
        const owner = element.ownerDocument, previousFocus = owner.activeElement;
        // Disabled page/zoom buttons can move focus to BODY. Escape must still close the active modal.
        const handler = event => {
            const active = Array.from(owner.querySelectorAll('.fpv-dialog')).pop();
            if (event.key === 'Escape' && element.isConnected && active === element) {
                event.preventDefault(); event.stopPropagation(); dotnet.invokeMethodAsync('CloseFromJs').catch(() => {});
            }
        };
        owner.addEventListener('keydown', handler, true); modalHandlers.set(key, { handler, owner, previousFocus }); element.focus({ preventScroll: true });
    }
    function detachModal(key) {
        const entry = key && modalHandlers.get(key);
        if (entry) {
            entry.owner.removeEventListener('keydown', entry.handler, true); modalHandlers.delete(key);
            if (entry.previousFocus && entry.previousFocus.isConnected) entry.previousFocus.focus({ preventScroll: true });
        }
    }
    window.filePreview = {
        apiVersion: 5, load, abort, revoke, openPdf, renderPdf, destroyPdf, attachModal, detachModal,
        // Compatibility aliases for any existing callers; real UI uses structured load().
        fetchBlobUrl: async (url, token, hint) => {
            const kind = hint === 'text/html' ? 'html' : hint === 'application/pdf' ? 'pdf' : hint && hint.startsWith('image/') ? 'image' : 'other';
            const r = await load(url, token, kind, hint, 'compat-' + crypto.randomUUID());
            if (!r.success) throw new Error(r.message); return r.url;
        },
        fetchText: async (url, token) => { const r = await load(url, token, 'text', 'text/plain', 'compat-' + crypto.randomUUID()); if (!r.success) throw new Error(r.message); return r.text; },
        htmlToBlobUrl: html => URL.createObjectURL(new Blob([html], { type: 'text/html;charset=utf-8' })),
        toBlobUrl: (base64, mime) => { const bin = atob(base64); return URL.createObjectURL(new Blob([Uint8Array.from(bin, c => c.charCodeAt(0))], { type: mime || 'application/octet-stream' })); },
        diagnostics: () => ({ requests: requests.size, pdfViewers: viewers.size, modalHandlers: modalHandlers.size }),
    download: async function (url, token, fallbackName) {
        try {
            const headers = {};
            if (token) headers.Authorization = 'Bearer ' + token;
            const response = await fetch(url, { headers: headers, cache: 'no-store' });
            if (!response.ok) {
                let message = response.status === 401 ? 'نشست شما منقضی شده است؛ دوباره وارد شوید.'
                    : response.status === 403 ? 'اجازه دانلود این فایل را ندارید.'
                    : response.status === 404 ? 'فایل در سرور یافت نشد.'
                    : 'دانلود فایل ناموفق بود (خطای ' + response.status + ').';
                let error = {};
                try { error = await response.json(); } catch (_) { }
                if (error && typeof error.message === 'string' && error.message.trim()) message = error.message;
                return { success: false, message: message,
                    code: error && typeof error.code === 'string' ? error.code : null,
                    documentId: error && Number.isInteger(error.documentId) ? error.documentId : 0 };
            }
            const disposition = response.headers.get('Content-Disposition') || '';
            if (!/^\s*attachment\b/i.test(disposition)) {
                return { success: false, message: 'سرور به‌جای فایل پاسخ نامعتبر برگرداند؛ صفحه را تازه کنید و دوباره تلاش کنید.' };
            }
            let fileName = fallbackName || 'download';
            const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(disposition);
            const quoted = /filename\s*=\s*"((?:[^"\\]|\\.)*)"/i.exec(disposition);
            const plain = /filename\s*=\s*([^;]+)/i.exec(disposition);
            if (encoded) {
                try { fileName = decodeURIComponent(encoded[1].trim()); } catch (_) { }
            } else if (quoted) fileName = quoted[1].replace(/\\(.)/g, '$1');
            else if (plain) fileName = plain[1].trim();
            fileName = fileName.split(/[\\/]/).pop().replace(/[\x00-\x1f\x7f]/g, '') || 'download';
            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            try {
                anchor.href = objectUrl;
                anchor.download = fileName;
                anchor.style.display = 'none';
                document.body.appendChild(anchor);
                anchor.click();
            } finally {
                anchor.remove();
                // Keep the object URL alive until browsers have accepted the download.
                setTimeout(() => URL.revokeObjectURL(objectUrl), 60000);
            }
            return { success: true };
        } catch (_) {
            return { success: false, message: 'دریافت فایل انجام نشد؛ اتصال شبکه و دسترسی به سرور را بررسی کنید.' };
        }
    }
    };
    window.fpFetchBlobUrl = window.filePreview.fetchBlobUrl;
    window.fpFetchText = window.filePreview.fetchText;
    window.fpHtmlToBlobUrl = window.filePreview.htmlToBlobUrl;
    window.fpRevoke = window.filePreview.revoke;
})();
