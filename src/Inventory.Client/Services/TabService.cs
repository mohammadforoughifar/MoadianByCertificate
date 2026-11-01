using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

// ============================================================
//  سرویسِ تب‌های کاری
//  • هر بخش/صفحه‌ای که کاربر باز می‌کند یک تب می‌گیرد (به‌جز پیام‌رسان و صفحات عمومی)
//  • صفحاتِ تب‌های غیرفعال در DOM می‌مانند و فقط مخفی می‌شوند،
//    بنابراین فیلترها، شماره صفحه، اسکرول و فرم‌های نیمه‌پر هنگام جابه‌جایی حفظ می‌شوند
//  • فهرست تب‌ها در localStorage ذخیره می‌شود تا بعد از رفرش مرورگر هم بماند
//    (محتوای هر تب در اولین نمایش دوباره ساخته می‌شود)
// ============================================================

public sealed class TabItem
{
    /// <summary>کلید یکتا — مسیر نسبیِ نرمال‌شده (شامل پارامترهای query)</summary>
    public string Key { get; set; } = "";

    /// <summary>مسیر کامل برای پیمایش</summary>
    public string Url { get; set; } = "";

    public string Title { get; set; } = "";
    public string Icon { get; set; } = "bi-circle";

    /// <summary>سنجاق‌شده — قابل بستن نیست (مثل تبِ داشبورد)</summary>
    public bool Pinned { get; set; }

    public bool Closable => !Pinned;

    public bool IsActive { get; set; }

    /// <summary>با افزایش این شماره، صفحه‌ی تب از نو ساخته می‌شود (دکمهٔ بازسازی)</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// داده‌های مسیرِ این تب. فقط برای تب‌هایی که در همین نشست باز شده‌اند مقدار دارد؛
    /// تب‌های بازیابی‌شده از localStorage تا زمانی که کاربر رویشان کلیک نکند خالی‌اند.
    /// </summary>
    [JsonIgnore]
    public RouteData? RouteData { get; set; }
}

public sealed class TabService
{
    private readonly IJSRuntime _js;
    private readonly NavigationManager _nav;
    private readonly LayoutState _layout;

    private const string StoreKey = "app.openTabs.v1";
    private const int MaxTabs = 25;

    private readonly List<TabItem> _tabs = new();
    private bool _loaded;
    private TabItem? _active;

    public IReadOnlyList<TabItem> Tabs => _tabs;
    public TabItem? Active => _active;

    /// <summary>هر تغییری در فهرست تب‌ها — نوار تب و نگه‌دارنده‌ی صفحات مشترک‌اند</summary>
    public event Action? Changed;

    public TabService(IJSRuntime js, NavigationManager nav, LayoutState layout)
    {
        _js = js;
        _nav = nav;
        _layout = layout;
        _layout.Changed += OnLayoutTitleChanged;
    }

    // ==================== عنوان و آیکون ====================

    /// <summary>نگاشتِ مسیر به (عنوان، آیکون) — برگرفته از آیتم‌های منوی برنامه</summary>
    private static readonly (string Prefix, string Title, string Icon)[] Known =
    {
        // سامانه مودیان (SDK 2.0.29) — ترتیب مهم است: خاص قبل از عمومی
        ("moadian/status-payments",     "سامانه مودیان — کارپوشه و پرداخت", "bi-credit-card-2-front"),
        ("moadian/profile",             "سامانه مودیان — پرونده و گواهی امضا", "bi-patch-check"),
        ("moadian/basic-info",          "سامانه مودیان — اطلاعات پایه", "bi-archive"),
        ("moadian/invoices/new",        "سامانه مودیان — صدور صورتحساب", "bi-journal-plus"),
        ("moadian/invoices",            "سامانه مودیان — صورتحساب‌ها",   "bi-journal-text"),
        ("moadian/guide",               "سامانه مودیان — راهنما",        "bi-journal-check"),
        ("moadian",                     "سامانه مودیان — نمای کلی",     "bi-speedometer2"),
        ("reports/kardex",              "کاردکس کالا",          "bi-bar-chart-line"),
        ("reports/reorder",             "نقطه سفارش",           "bi-exclamation-triangle"),
        ("outgoing-letters/dabirkhane", "دبیرخانه نامه صادره",  "bi-mailbox"),
        ("outgoing-letters",            "نامه صادره",           "bi-send"),
        ("incoming-letters",            "نامه وارده",           "bi-inbox-fill"),
        ("office/personal-email",       "ایمیل شخصی",           "bi-person-badge"),
        ("office/email",                "ایمیل سازمانی",        "bi-envelope-at"),
        ("doc-archive/cartable",        "کارتابل آرشیو",        "bi-inbox"),
        ("export-center",               "مرکز خروجی",           "bi-file-earmark-arrow-down"),
        ("doc-archive",                 "پوشه‌ها و مدارک",      "bi-folder2-open"),
        ("settings/letter-structure",   "ساختار شماره نامه",    "bi-sort-numeric-down"),
        ("settings/audit-log",          "لاگ حسابرسی",          "bi-clock-history"),
        ("settings",                    "تنظیمات",              "bi-gear-fill"),
        ("work-orders",                 "دستور کار",            "bi-card-checklist"),
        ("dashboard-cctv",              "داشبورد مدیریت",       "bi-graph-up-arrow"),
        ("dashboard-hardware",          "داشبورد سخت‌افزار",    "bi-motherboard"),
        ("my-dashboards",               "داشبورد من",           "bi-grid-1x2-fill"),
        ("report-studio",               "گزارش‌ساز",             "bi-clipboard-data-fill"),
        ("my-archive",                  "بایگانی شخصی",         "bi-archive-fill"),
        ("cartable",                    "کارتابل من",           "bi-inbox-fill"),
        ("project-cartable",            "کارتابل پروژه",        "bi-inboxes"),
        ("report-works",                "گزارش‌های کار",        "bi-clock-history"),
        ("type-factors",                "انواع فاکتور",         "bi-card-list"),
        ("orders/purchase",             "خرید",                 "bi-bag-plus"),
        ("orders/sale",                 "فروش",                 "bi-cart3"),
        ("referrer-wallets",            "کیف پول معرف‌ها",      "bi-wallet2"),
        ("system-department",           "واحدها",               "bi-diagram-3"),
        ("system-company",              "کمپانی‌ها",            "bi-building"),
        ("system-users",                "کاربران سیستم",        "bi-people-fill"),
        ("system-info",                 "سیستم‌های ثبت‌شده",    "bi-list-check"),
        ("system-id/manual",            "ثبت دستی سیستم",       "bi-pc-display-horizontal"),
        ("system-id",                   "شناسنامه سیستم",       "bi-cpu-fill"),
        ("network-scan",                "اسکن شبکه",            "bi-broadcast-pin"),
        ("office-machines",             "ماشین‌های اداری",      "bi-printer-fill"),
        ("cctv-cameras",                "دوربین‌ها",            "bi-camera-video-fill"),
        ("cctv-nvrs",                   "دستگاه‌های NVR",       "bi-hdd-stack-fill"),
        ("it-vault",                    "گاوصندوق رمزها",       "bi-shield-lock-fill"),
        ("ip-management",               "مدیریت آی‌پی",         "bi-diagram-2-fill"),
        ("it-requests",                 "درخواست خدمت IT",      "bi-headset"),
        ("warehouses",                  "انبارها",              "bi-buildings"),
        ("categories",                  "گروه‌های کالا",        "bi-tags"),
        ("products",                    "کالاها",               "bi-box-seam"),
        ("parties",                     "طرف حساب‌ها",          "bi-people"),
        ("referrers",                   "معرف‌ها",              "bi-person-badge"),
        ("projects",                    "پروژه‌ها",             "bi-arrow-left-right"),
        ("karfarmas",                   "کارفرماها",            "bi-briefcase"),
        ("letters",                     "نامه داخلی",           "bi-envelope-paper"),
        ("misc/minutes",                "صورتجلسه",             "bi-journal-text"),
        ("expenses",                    "هزینه‌ها",             "bi-cash-coin"),
        ("repairs",                     "تعمیرات",              "bi-tools"),
        ("stock",                       "موجودی انبار",         "bi-boxes"),
        ("units",                       "واحدهای شمارش",        "bi-rulers"),
        ("users",                       "کاربران",              "bi-person-gear"),
        ("my-wallet",                   "کیف پول من",           "bi-wallet2"),
        ("my-products",                 "کالاهای موجود",        "bi-box-seam"),
        ("my-card",                     "کارت ویزیت من",        "bi-person-vcard"),
    };

    /// <summary>مسیرهایی که تب نمی‌گیرند — صفحات تمام‌صفحه یا عمومی</summary>
    private static readonly string[] Excluded =
    {
        "chat",             // پیام‌رسان تمام‌صفحه
        "public-request",   // فرم عمومی ثبت درخواست (بدون ورود)
    };

    public static bool IsExcluded(string relativeUrl)
    {
        var p = (relativeUrl ?? "").Trim('/').ToLowerInvariant();
        if (p.Length == 0) return false;
        return Excluded.Any(x => p == x || p.StartsWith(x + "/", StringComparison.Ordinal)
                                       || p.StartsWith(x + "?", StringComparison.Ordinal));
    }

    /// <summary>کلید یکتای تب برای یک مسیر نسبی</summary>
    public static string KeyOf(string relativeUrl)
        => (relativeUrl ?? "").Trim().TrimStart('/').TrimEnd('/').ToLowerInvariant();

    private static (string Title, string Icon) Resolve(string key)
    {
        if (key.Length == 0) return ("داشبورد", "bi-speedometer2");

        foreach (var k in Known)
            if (key == k.Prefix
                || key.StartsWith(k.Prefix + "/", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(k.Prefix + "?", StringComparison.OrdinalIgnoreCase))
                return (k.Title, k.Icon);

        // مسیرهای «منابع انسانی فروغ آریا» (برنامهٔ پیشین) — پیش از بررسی منوی اصلی،
        // چون دو نمای پیشین (فهرست پرسنل / چارت سازمانی) درون hr-core جا دارند.
        if (ForoughAryaHrNavigation.IsWorkspace(key))
        {
            var fa = ForoughAryaHrNavigation.Title(key);
            if (!string.IsNullOrWhiteSpace(fa)) return (fa, "bi-person-workspace");
        }

        // فقط مسیرهای خودِ منابع انسانی — وگرنه هر صفحهٔ ناشناخته «منابع انسانی» می‌شد
        if (HrNavigation.IsWorkspace(key))
        {
            var hr = HrNavigation.Title(key);
            if (!string.IsNullOrWhiteSpace(hr)) return (hr, "bi-people");
        }

        return (key.Split('?')[0].Trim('/'), "bi-circle");
    }

    // ==================== بارگیری / ذخیره ====================

    private sealed class Snapshot
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public bool Pinned { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>بازیابیِ فهرست تب‌ها از localStorage — یک‌بار در آغاز برنامه</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StoreKey);
            if (string.IsNullOrWhiteSpace(json)) return;

            var items = JsonSerializer.Deserialize<List<Snapshot>>(json, JsonOpts);
            if (items is null) return;

            foreach (var s in items)
            {
                if (string.IsNullOrWhiteSpace(s.Url)) continue;
                if (IsExcluded(s.Url)) continue;

                var key = KeyOf(s.Url);
                if (_tabs.Any(x => x.Key == key)) continue;

                var (title, icon) = Resolve(key);
                var savedTitle = s.Title ?? "";
                var staleHr = !HrNavigation.IsWorkspace(key)
                    && (savedTitle == "منابع انسانی" || savedTitle == "خانه منابع انسانی");
                _tabs.Add(new TabItem
                {
                    Key = key,
                    Url = s.Url,
                    Title = string.IsNullOrWhiteSpace(savedTitle) || staleHr ? title : savedTitle,
                    Icon = string.IsNullOrWhiteSpace(s.Icon) || staleHr ? icon : s.Icon,
                    // تبِ خانه همیشه سنجاق است
                    Pinned = s.Pinned || key.Length == 0
                });
            }
            Changed?.Invoke();
        }
        catch { /* localStorage در دسترس نبود — تب‌ها فقط در همین نشست می‌مانند */ }
    }

    private async Task SaveAsync()
    {
        if (!_loaded) return;
        try
        {
            var snap = _tabs.Select(t => new Snapshot
            {
                Url = t.Url,
                Title = t.Title,
                Icon = t.Icon,
                Pinned = t.Pinned
            }).ToList();
            await _js.InvokeVoidAsync("localStorage.setItem", StoreKey,
                JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch { }
    }

    // ==================== همگام‌سازی با مسیر جاری ====================

    /// <summary>
    /// هر پیمایش صدا می‌زند: تبِ متناظر را می‌سازد یا به‌روز می‌کند و فعال می‌کند.
    /// </summary>
    public void Sync(string relativeUrl, RouteData routeData)
    {
        var key = KeyOf(relativeUrl);
        var t = _tabs.FirstOrDefault(x => x.Key == key);

        if (t is null)
        {
            var (title, icon) = Resolve(key);
            t = new TabItem
            {
                Key = key,
                Url = relativeUrl,
                Title = title,
                Icon = icon,
                Pinned = key.Length == 0 // تبِ خانه سنجاق است
            };
            _tabs.Add(t);
            TrimExcess();
        }
        else
        {
            t.Url = relativeUrl;
            if (!HrNavigation.IsWorkspace(key)
                && (t.Title == "منابع انسانی" || t.Title == "خانه منابع انسانی"))
            {
                var (title, icon) = Resolve(key);
                t.Title = title;
                t.Icon = icon;
            }
        }

        t.RouteData = routeData;

        foreach (var x in _tabs) x.IsActive = ReferenceEquals(x, t);
        _active = t;

        Changed?.Invoke();
        _ = SaveAsync();
    }

    /// <summary>
    /// هیچ تب فعالی نشان داده نشود — برای صفحاتی که تب نمی‌گیرند
    /// (مثل پیام‌رسان یا هنگامی که کاربر وارد نشده است).
    /// </summary>
    public void SetNoActive()
    {
        if (_active is null) return;
        foreach (var x in _tabs) x.IsActive = false;
        _active = null;
        Changed?.Invoke();
    }

    /// <summary>اگر تعداد تب‌ها از حد مجاز گذشت، قدیمی‌ترین تبِ غیرسنجاق حذف می‌شود</summary>
    private void TrimExcess()
    {
        while (_tabs.Count > MaxTabs)
        {
            var victim = _tabs.FirstOrDefault(x => x.Closable && !x.IsActive);
            if (victim is null) break;
            _tabs.Remove(victim);
        }
    }

    /// <summary>وقتی صفحه‌ای عنوانِ خود را با Layout.SetTitle معرفی کرد، عنوانِ تب هم به‌روز می‌شود</summary>
    private void OnLayoutTitleChanged()
    {
        if (_active is null) return;
        var title = (_layout.Title ?? "").Trim();
        if (title.Length == 0 || title == _active.Title) return;
        _active.Title = title.Length > 46 ? title[..46] + "…" : title;
        Changed?.Invoke();
        _ = SaveAsync();
    }

    // ==================== عملیات روی تب‌ها ====================

    public bool HasTab(string relativeUrl)
    {
        var key = KeyOf(relativeUrl);
        return _tabs.Any(x => x.Key == key);
    }

    /// <summary>رفتن به یک تب — اگر قبلاً باز بود فقط فعال می‌شود (و وضعیتش حفظ می‌ماند)</summary>
    public void Activate(TabItem t)
    {
        if (t.IsActive) return;
        _nav.NavigateTo(t.Url);
    }

    public async Task CloseAsync(TabItem t)
    {
        if (!t.Closable) return;
        var i = _tabs.IndexOf(t);
        if (i < 0) return;

        _tabs.RemoveAt(i);

        if (t.IsActive)
        {
            // بعد از بستن، تبِ کناری فعال می‌شود؛ اگر تبِ بعدی نبود، قبلی
            var next = _tabs.ElementAtOrDefault(Math.Min(i, _tabs.Count - 1))
                       ?? _tabs.FirstOrDefault();
            _active = null;
            _nav.NavigateTo(next?.Url ?? "");
        }

        Changed?.Invoke();
        await SaveAsync();
    }

    public async Task CloseOthersAsync(TabItem keep)
    {
        var doomed = _tabs.Where(x => !ReferenceEquals(x, keep) && x.Closable).ToList();
        foreach (var d in doomed) _tabs.Remove(d);

        if (!keep.IsActive) _nav.NavigateTo(keep.Url);
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>بستنِ همه — تب‌های سنجاق (مثل خانه) می‌مانند</summary>
    public async Task CloseAllAsync()
    {
        var doomed = _tabs.Where(x => x.Closable).ToList();
        foreach (var d in doomed) _tabs.Remove(d);

        var home = _tabs.FirstOrDefault(x => x.Key.Length == 0);
        _nav.NavigateTo(home?.Url ?? "");
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>سنجاق / آزاد کردن یک تب</summary>
    public async Task TogglePinAsync(TabItem t)
    {
        t.Pinned = !t.Pinned;
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>بازسازیِ صفحه‌ی تب — برای وقتی که محتوا کهنه شده یا نمودار درست رسم نشده</summary>
    public void Refresh(TabItem t)
    {
        t.Version++;
        Changed?.Invoke();
    }

    /// <summary>جابه‌جاییِ تب با کشیدن و رها کردن</summary>
    public async Task MoveAsync(TabItem source, TabItem target)
    {
        var si = _tabs.IndexOf(source);
        var ti = _tabs.IndexOf(target);
        if (si < 0 || ti < 0 || si == ti) return;

        _tabs.RemoveAt(si);
        _tabs.Insert(ti, source);
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>آیا این نوع صفحه «لایه‌ی اختصاصی» دارد و نباید تب بگیرد؟</summary>
    public static bool HasOwnLayout(Type pageType) =>
        pageType.GetCustomAttribute<Microsoft.AspNetCore.Components.LayoutAttribute>() != null;
}
