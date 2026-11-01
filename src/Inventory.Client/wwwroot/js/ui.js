// ابزار مشترک منوهای شناور (تقویم / کمبوباکس)
// مختصات المان را در مختصات viewport برمی‌گرداند تا منو با position:fixed
// دقیقا زیر (یا بالای) المان قرار بگیرد و دیگر زیر کارت‌ها نرود یا بریده نشود.
window.uiRect = function (el) {
    if (!el) {
        return { left: 8, top: 8, right: 238, bottom: 46, width: 230, height: 38, vw: window.innerWidth, vh: window.innerHeight };
    }
    var r = el.getBoundingClientRect();
    return {
        left: r.left,
        top: r.top,
        right: r.right,
        bottom: r.bottom,
        width: r.width,
        height: r.height,
        vw: window.innerWidth,
        vh: window.innerHeight
    };
};

// اسکرول یک المان با شناسه؛ حالت پیش‌فرض برای گزینه هایلایت‌شده در کمبوباکس‌ها
// است و صفحات راهنما می‌توانند block و behavior دلخواه (مثلاً start/smooth) بفرستند.
window.scrollIntoViewById = function (id, block, behavior) {
    var el = document.getElementById(id);
    if (el && typeof el.scrollIntoView === 'function') {
        el.scrollIntoView({
            block: block || 'nearest',
            inline: 'nearest',
            behavior: behavior || 'auto'
        });
    }
};

// فوکوس روی اینپوت کالای آخرین سطر فرم سند انبار
window.focusLastProductInput = function () {
    setTimeout(function () {
        var inputs = document.querySelectorAll('.doc-grid tbody tr:last-child .ss input');
        if (inputs && inputs.length) {
            inputs[0].focus();
            if (inputs[0].select) inputs[0].select();
        }
    }, 60);
};

// Print the whole guide, including collapsed FAQ answers, without changing the
// reader's open/closed choices when the print dialog is dismissed.
window.officeGuides = {
    // Workspace tabs remain mounted when hidden. Search only this guide,
    // not a same-id section in another tab's copy of the guide.
    scrollTo: function (root, id) {
        const section = root && Array.from(root.querySelectorAll('section[id]'))
            .find(element => element.id === id);
        if (section) section.scrollIntoView({ block: 'start', inline: 'nearest',
            behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    },
    print: function (root) {
        if (!root) return;
        const collapsed = Array.from(root.querySelectorAll('details:not([open])'));
        const restore = function () {
            collapsed.forEach(detail => detail.open = false);
            window.removeEventListener('afterprint', restore);
        };
        collapsed.forEach(detail => detail.open = true);
        window.addEventListener('afterprint', restore, { once: true });
        try {
            window.print();
        } catch (error) {
            restore();
            throw error;
        }
    }
};

// میان‌بر Enter در فرم پذیرش تعمیرات: پس از انتخاب گزینهٔ کمبوباکس،
// فوکوس به فیلد بعد می‌رود؛ Shift+Enter در textarea برای رفتن به خط بعد است.
window.repairFormKeyboard = {
    attach: function (root, focusCustomer) {
        if (!root) return;
        window.repairFormKeyboard.detach(root);
        const handler = function (event) {
            const target = event.target;
            if (!(target instanceof HTMLElement) || !root.contains(target)) return;

            // در مبلغ‌های ریالی، کلیدهای حرفی/نشانه‌ای پیش از ورود به فیلد متوقف می‌شوند؛ paste هم در oninput پاک‌سازی می‌شود.
            if (target.matches('[data-rial-input]') && event.key.length === 1 &&
                !event.ctrlKey && !event.metaKey && !event.altKey &&
                !/^[0-9۰-۹٠-٩,]$/.test(event.key)) {
                event.preventDefault();
                return;
            }

            if (event.key !== 'Enter' || event.isComposing || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
            if (!target.matches('input:not([type="hidden"]):not([disabled]):not([readonly]), select:not([disabled]), textarea:not([disabled])')) return;

            // کنترل‌های خودِ تقویم و گزینهٔ برجستهٔ SearchSelect رفتار Enter مستقل دارند.
            if (target.closest('.dp-popover')) return;
            const searchSelect = target.closest('.ss');
            if (searchSelect && searchSelect.querySelector('.ss-menu .ss-item')) return;

            const selector = 'input:not([type="hidden"]):not([disabled]):not([readonly]), select:not([disabled]), textarea:not([disabled])';
            const fields = Array.from(root.querySelectorAll(selector)).filter(function (field) {
                return field.tabIndex >= 0 && field.getClientRects().length > 0;
            });
            const currentIndex = fields.indexOf(target);
            if (currentIndex < 0 || currentIndex >= fields.length - 1) return;

            const nextField = fields[currentIndex + 1];
            if (target instanceof HTMLSelectElement ||
                (target instanceof HTMLInputElement && target.hasAttribute('list'))) {
                // انتخاب پیش‌فرض مرورگر در select/datalist اجرا شود؛ سپس فوکوس به فیلد بعدی برود.
                setTimeout(function () {
                    if (root.contains(nextField)) nextField.focus({ preventScroll: false });
                }, 0);
                return;
            }

            event.preventDefault();
            nextField.focus({ preventScroll: false });
        };
        root.__repairFormEnterHandler = handler;
        root.addEventListener('keydown', handler);

        if (focusCustomer !== false) {
            const customerInput = root.querySelector('.repair-customer-focus .ss input');
            if (customerInput) customerInput.focus({ preventScroll: true });
        }
    },
    focusDeviceType: function (root, index) {
        if (!root) return;
        const input = root.querySelector('[data-repair-device-type="' + index + '"]');
        if (!input) return;
        input.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        input.focus({ preventScroll: true });
    },
    detach: function (root) {
        if (!root || !root.__repairFormEnterHandler) return;
        root.removeEventListener('keydown', root.__repairFormEnterHandler);
        delete root.__repairFormEnterHandler;
    }
};

// دانلود محتوای باینری (base64) به‌صورت فایل — مثلاً کلید خصوصی مودیان
window.downloadFileFromBase64 = function (fileName, contentType, base64) {
    try {
        const byteChars = atob(base64);
        const byteNumbers = new Uint8Array(byteChars.length);
        for (let i = 0; i < byteChars.length; i++) byteNumbers[i] = byteChars.charCodeAt(i);
        const blob = new Blob([byteNumbers], { type: contentType || 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'download';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { document.body.removeChild(a); URL.revokeObjectURL(url); }, 0);
    } catch (e) { console.error('downloadFileFromBase64 failed', e); }
};
