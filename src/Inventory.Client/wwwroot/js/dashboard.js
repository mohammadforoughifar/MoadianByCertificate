// =====================================================================
//  داشبورد شخصی کاربر — چیدمان گرید، درگ‌اند‌دراپ، تغییر اندازه و نمودارها
//  همهٔ توابع زیر window.appDash قرار می‌گیرند و از Blazor صدا زده می‌شوند.
// =====================================================================
(function () {
    "use strict";

    const charts = {};
    const state = { ref: null, root: null, dragId: null };

    // ------------------------------------------------------------------
    //  نمودار (Chart.js از قبل در index.html بارگذاری شده)
    // ------------------------------------------------------------------
    function palette(i) {
        const c = ["#2563eb", "#16a34a", "#f59e0b", "#dc2626", "#7c3aed",
                   "#0891b2", "#db2777", "#65a30d", "#ea580c", "#475569"];
        return c[i % c.length];
    }

    function renderChart(canvasId, type, labels, datasets) {
        const el = document.getElementById(canvasId);
        if (!el || typeof Chart === "undefined") return;

        if (charts[canvasId]) { charts[canvasId].destroy(); delete charts[canvasId]; }

        const mapped = (datasets || []).map((s, i) => {
            const color = palette(i);
            const isPie = type === "pie" || type === "doughnut";
            return {
                label: s.name || "",
                data: s.values || [],
                borderColor: isPie ? "#fff" : color,
                backgroundColor: isPie
                    ? (s.values || []).map((_, j) => palette(j))
                    : (type === "line" ? color + "33" : color),
                borderWidth: isPie ? 2 : 2,
                tension: 0.35,
                fill: type === "line",
                borderRadius: type === "bar" ? 6 : 0,
                maxBarThickness: 34
            };
        });

        const isPie = type === "pie" || type === "doughnut";
        charts[canvasId] = new Chart(el.getContext("2d"), {
            type: type,
            data: { labels: labels || [], datasets: mapped },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                rtl: true,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: {
                        display: isPie || (datasets || []).length > 1,
                        position: "bottom",
                        rtl: true,
                        labels: { boxWidth: 12, font: { size: 11 }, padding: 10 }
                    },
                    tooltip: { rtl: true, textDirection: "rtl" }
                },
                scales: isPie ? {} : {
                    x: { grid: { display: false }, ticks: { font: { size: 10 } } },
                    y: { beginAtZero: true, grid: { color: "#eef2f7" }, ticks: { font: { size: 10 } } }
                }
            }
        });
    }

    function destroyChart(canvasId) {
        if (charts[canvasId]) { charts[canvasId].destroy(); delete charts[canvasId]; }
    }

    // ------------------------------------------------------------------
    //  گرید: درگ‌اند‌دراپ برای جابه‌جایی + دستگیرهٔ تغییر اندازه
    // ------------------------------------------------------------------
    function cardOf(node) {
        return node && node.closest ? node.closest("[data-wid]") : null;
    }

    // برمی‌گرداند: آیا گرید پیدا شد و رویدادها بسته شدند؟
    // (گرید تا وقتی ویجتی در داشبورد نباشد رندر نمی‌شود، پس Blazor باید دوباره تلاش کند.)
    function bind(rootId, dotNetRef) {
        const root = document.getElementById(rootId);
        if (!root) return false;
        state.ref = dotNetRef;
        state.root = root;
        if (root.dataset.bound === "1") return true;
        root.dataset.bound = "1";

        root.addEventListener("dragstart", function (e) {
            const card = cardOf(e.target);
            if (!card) return;
            state.dragId = card.dataset.wid;
            card.classList.add("dash-dragging");
            if (e.dataTransfer) { e.dataTransfer.effectAllowed = "move"; e.dataTransfer.setData("text/plain", state.dragId); }
        });

        root.addEventListener("dragend", function () {
            state.dragId = null;
            root.querySelectorAll(".dash-dragging, .dash-drop-before, .dash-drop-after")
                .forEach(function (el) { el.classList.remove("dash-dragging", "dash-drop-before", "dash-drop-after"); });
        });

        root.addEventListener("dragover", function (e) {
            if (!state.dragId) return;
            const card = cardOf(e.target);
            if (!card || card.dataset.wid === state.dragId) return;
            e.preventDefault();
            if (e.dataTransfer) e.dataTransfer.dropEffect = "move";
            const r = card.getBoundingClientRect();
            // در RTL «قبل» یعنی سمت راست کارت
            const before = (e.clientX - r.left) > r.width / 2;
            card.classList.toggle("dash-drop-after", before);
            card.classList.toggle("dash-drop-before", !before);
        });

        root.addEventListener("dragleave", function (e) {
            const card = cardOf(e.target);
            if (card) card.classList.remove("dash-drop-before", "dash-drop-after");
        });

        root.addEventListener("drop", function (e) {
            const card = cardOf(e.target);
            if (!card || !state.dragId) return;
            e.preventDefault();
            const r = card.getBoundingClientRect();
            const before = (e.clientX - r.left) > r.width / 2;
            const dragId = state.dragId;
            card.classList.remove("dash-drop-before", "dash-drop-after");
            if (dragId === card.dataset.wid) return;
            if (state.ref) state.ref.invokeMethodAsync("OnReorder", dragId, card.dataset.wid, !before);
        });

        // ---------- تغییر اندازه ----------
        root.addEventListener("pointerdown", function (e) {
            const handle = e.target.closest ? e.target.closest("[data-resize]") : null;
            if (!handle) return;
            const card = cardOf(handle);
            if (!card) return;
            e.preventDefault();

            const grid = card.parentElement;
            const colW = grid ? (grid.getBoundingClientRect().width / 12) : 100;
            const rowH = 92; // ارتفاع هر واحد ردیف (مطابق CSS)
            const startX = e.clientX, startY = e.clientY;
            const startW = parseInt(card.dataset.w || "3", 10);
            const startH = parseInt(card.dataset.h || "2", 10);
            card.classList.add("dash-resizing");

            function move(ev) {
                // در RTL حرکت به چپ = بزرگ‌تر شدن
                const dx = (startX - ev.clientX) / Math.max(colW, 1);
                const dy = (ev.clientY - startY) / rowH;
                const w = Math.min(12, Math.max(1, startW + Math.round(dx)));
                const h = Math.min(8, Math.max(1, startH + Math.round(dy)));
                card.style.gridColumn = "span " + w;
                card.style.gridRow = "span " + h;
                card.dataset.pendingW = w;
                card.dataset.pendingH = h;
            }
            function up() {
                window.removeEventListener("pointermove", move);
                window.removeEventListener("pointerup", up);
                card.classList.remove("dash-resizing");
                const w = parseInt(card.dataset.pendingW || startW, 10);
                const h = parseInt(card.dataset.pendingH || startH, 10);
                delete card.dataset.pendingW; delete card.dataset.pendingH;
                card.style.gridColumn = ""; card.style.gridRow = "";
                if (state.ref && (w !== startW || h !== startH))
                    state.ref.invokeMethodAsync("OnResize", card.dataset.wid, w, h);
            }
            window.addEventListener("pointermove", move);
            window.addEventListener("pointerup", up);
        });

        return true;
    }

    window.appDash = {
        renderChart: renderChart,
        destroyChart: destroyChart,
        bind: bind
    };
})();
