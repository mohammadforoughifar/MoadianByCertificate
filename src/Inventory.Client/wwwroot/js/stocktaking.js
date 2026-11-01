// =====================================================================
// توابع کمکی ماژول انبارگردانی و بارکد
//   stkBeep(ok)    بوق کوتاه موفق/ناموفق پس از هر اسکن
//   stkPrint(id)   چاپ فقط یک بخش از صفحه (برگه برچسب یا گزارش)
// هیچ وابستگی بیرونی ندارد.
// =====================================================================

(function () {
    let audioCtx = null;

    /**
     * بوق کوتاه با Web Audio API.
     * موفق: یک نت بالا و کوتاه | ناموفق: دو نت پایین
     */
    window.stkBeep = function (ok) {
        try {
            if (!audioCtx) {
                const Ctx = window.AudioContext || window.webkitAudioContext;
                if (!Ctx) return;
                audioCtx = new Ctx();
            }
            if (audioCtx.state === 'suspended') audioCtx.resume();

            const play = (freq, start, duration) => {
                const osc = audioCtx.createOscillator();
                const gain = audioCtx.createGain();
                osc.type = 'sine';
                osc.frequency.value = freq;
                gain.gain.setValueAtTime(0.0001, audioCtx.currentTime + start);
                gain.gain.exponentialRampToValueAtTime(0.25, audioCtx.currentTime + start + 0.01);
                gain.gain.exponentialRampToValueAtTime(0.0001, audioCtx.currentTime + start + duration);
                osc.connect(gain);
                gain.connect(audioCtx.destination);
                osc.start(audioCtx.currentTime + start);
                osc.stop(audioCtx.currentTime + start + duration + 0.02);
            };

            if (ok) {
                play(1180, 0, 0.08);
            } else {
                play(320, 0, 0.13);
                play(240, 0.16, 0.20);
            }
        } catch (e) {
            /* صدا اختیاری است */
        }
    };

    /**
     * چاپ یک بخش مشخص از صفحه، بدون منو و نوار بالا.
     * محتوای عنصر در یک پنجره‌ی چاپ مستقل RTL رندر می‌شود.
     */
    window.stkPrint = function (elementId, title) {
        const el = document.getElementById(elementId);
        if (!el) return;

        const w = window.open('', '_blank', 'width=900,height=650');
        if (!w) return;

        // شیوه‌نامه‌های صفحه را می‌بریم تا ظاهر برچسب حفظ شود
        let styles = '';
        document.querySelectorAll('style').forEach(s => { styles += s.outerHTML; });
        document.querySelectorAll('link[rel="stylesheet"]').forEach(l => { styles += l.outerHTML; });

        w.document.write(
            '<!DOCTYPE html><html dir="rtl" lang="fa"><head><meta charset="utf-8">' +
            '<title>' + (title || 'چاپ') + '</title>' + styles +
            '<style>@page{margin:8mm}body{background:#fff;padding:0;margin:0}' +
            '.no-print{display:none !important}</style>' +
            '</head><body>' + el.innerHTML + '</body></html>'
        );
        w.document.close();

        // کمی صبر تا فونت و شیوه‌نامه بارگذاری شود
        setTimeout(function () {
            w.focus();
            w.print();
            w.close();
        }, 400);
    };
})();
