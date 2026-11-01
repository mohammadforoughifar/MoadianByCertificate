// =====================================================================
// دستیار فروغ آریا — ورودی صوتی فارسی (Web Speech API مرورگر) + اسکرول چت
// بدون نیاز به سرور؛ در کروم/اج دسکتاپ و اندروید کار می‌کند.
// =====================================================================
window.aiVoice = (function () {
    var rec = null;
    var listening = false;

    function ctor() {
        var SR = window.SpeechRecognition || window.webkitSpeechRecognition;
        return SR ? new SR() : null;
    }

    return {
        isSupported: function () {
            return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
        },
        isListening: function () { return listening; },
        start: function (dotNetRef) {
            if (listening) return true;
            rec = ctor();
            if (!rec) return false;
            rec.lang = 'fa-IR';
            rec.interimResults = false;
            rec.maxAlternatives = 1;
            rec.onresult = function (e) {
                try {
                    var text = e.results && e.results[0] && e.results[0][0] ? e.results[0][0].transcript : '';
                    if (text) dotNetRef.invokeMethodAsync('OnVoiceResult', text);
                } catch (err) { /* نادیده */ }
            };
            rec.onerror = function (e) {
                try { dotNetRef.invokeMethodAsync('OnVoiceEnd', (e && e.error) || 'error'); } catch (err) { }
            };
            rec.onend = function () {
                listening = false;
                try { dotNetRef.invokeMethodAsync('OnVoiceEnd', ''); } catch (err) { }
            };
            try {
                rec.start();
                listening = true;
                return true;
            } catch (err) {
                listening = false;
                return false;
            }
        },
        stop: function () {
            try { if (rec && listening) rec.stop(); } catch (err) { }
            listening = false;
        },
        // جداسازی کامل: پس از آزادشدن DotNetObjectReference (بستن/تغییر صفحه)
        // رویدادهای onresult/onend دیگر نباید به .NET فراخوانی بزنند؛
        // در غیر این صورت خطای «There is no tracked object with id …» ثبت می‌شود.
        detach: function () {
            listening = false;
            if (!rec) return;
            try { rec.abort(); } catch (err) { }
            rec.onresult = null;
            rec.onerror = null;
            rec.onend = null;
            rec = null;
        },
        speak: function (text) {
            try {
                if (!('speechSynthesis' in window)) return false;
                window.speechSynthesis.cancel();
                // تمیزکاری مارک‌داون برای خوانش روان‌تر
                var clean = (text || '')
                    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
                    .replace(/[*_`#>]/g, '')
                    .replace(/https?:\S+/g, '');
                if (!clean.trim()) return false;
                var u = new SpeechSynthesisUtterance(clean.slice(0, 1500));
                u.lang = 'fa-IR';
                var voices = window.speechSynthesis.getVoices() || [];
                var fa = voices.find(function (v) { return v.lang && v.lang.toLowerCase().indexOf('fa') === 0; });
                if (fa) u.voice = fa;
                window.speechSynthesis.speak(u);
                return true;
            } catch (err) { return false; }
        },
        stopSpeak: function () {
            try { if ('speechSynthesis' in window) window.speechSynthesis.cancel(); } catch (err) { }
        }
    };
})();

window.aiChat = {
    scrollBottom: function (id) {
        try {
            var el = document.getElementById(id);
            if (el) el.scrollTop = el.scrollHeight;
        } catch (err) { }
    }
};
