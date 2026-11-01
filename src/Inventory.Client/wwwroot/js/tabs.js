// ============================================================
//  ابزارهای نوار تب‌های کاری
//  • پیمایش افقی با دکمه‌های کنار نوار
//  • چرخ‌موشی عمودی به پیمایش افقی تبدیل می‌شود
//  • تبِ فعال هنگام جابه‌جایی به دید می‌آید
// ============================================================
window.appTabs = {

    scrollBy: function (el, dx) {
        if (!el) return;
        el.scrollBy({ left: dx, behavior: 'smooth' });
    },

    scrollToActive: function (el) {
        if (!el) return;
        var a = el.querySelector('.tab.on');
        if (!a) return;

        var r = a.getBoundingClientRect();
        var c = el.getBoundingClientRect();
        if (r.left < c.left) {
            el.scrollBy({ left: r.left - c.left - 12, behavior: 'smooth' });
        } else if (r.right > c.right) {
            el.scrollBy({ left: r.right - c.right + 12, behavior: 'smooth' });
        }
    },

    enableWheel: function (el) {
        if (!el || el.__tabWheel) return;
        el.__tabWheel = true;
        el.addEventListener('wheel', function (e) {
            // فقط وقتی حرکتِ عمودی غالب است آن را به افقی تبدیل می‌کنیم
            if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
                e.preventDefault();
                el.scrollLeft += e.deltaY;
            }
        }, { passive: false });
    }
};
