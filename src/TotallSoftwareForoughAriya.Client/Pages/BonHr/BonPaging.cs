using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.BonHr;

/// <summary>
/// وضعیت صفحه‌بندیِ یک فهرست در صفحات بن‌سازه.
///
/// چرا لازم است: بعضی صفحات بن‌سازه چند فهرست مستقل دارند (مثلاً «ایمنی و حوادث»
/// پنج فهرست دارد) و هر فهرست باید شمارهٔ صفحهٔ **خودش** را داشته باشد، وگرنه
/// جلو رفتن در یکی، بقیه را هم جابه‌جا می‌کند. هر فهرست یک نمونهٔ جدا می‌گیرد.
///
/// الگوی استفاده:
/// <code>
/// private readonly BonPagerState _pg = new();
///
/// private async Task LoadAsync()
/// {
///     var res = await Api.ListPagedAsync&lt;Incident&gt;("api/hse/incidents", _pg.Page, _pg.Size);
///     _incidents = _pg.Take(res);      // هم Items را می‌دهد، هم Total را ثبت می‌کند
/// }
///
/// private void GoPage(int p) { _pg.Go(p); _ = LoadAsync(); }
/// </code>
/// و در markup:
/// <code>
/// &lt;Pager Page="_pg.Page" PageCount="_pg.Count" OnPageChanged="GoPage" /&gt;
/// </code>
/// </summary>
public sealed class BonPagerState
{
    /// <summary>شمارهٔ صفحهٔ جاری — از ۱.</summary>
    public int Page { get; private set; } = 1;

    /// <summary>اندازهٔ صفحه. پیش‌فرض ۲۵، مطابق بقیهٔ صفحات سامانه (مثل AuditLog).</summary>
    public int Size { get; set; } = 25;

    /// <summary>تعداد کل ردیف‌های منطبق با فیلتر — نه فقط ردیف‌های این صفحه.</summary>
    public int Total { get; private set; }

    /// <summary>تعداد صفحه‌ها؛ وقتی Total صفر است یک صفحه گزارش می‌شود تا Pager پنهان بماند.</summary>
    public int Count => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)Math.Max(1, Size));

    /// <summary>شمارهٔ نخستین ردیف این صفحه (۰ اگر فهرست خالی است) — برای «ردیف ۱ تا ۲۵ از ۳۴۰».</summary>
    public int From => Total == 0 ? 0 : (Page - 1) * Size + 1;

    /// <summary>شمارهٔ آخرین ردیف این صفحه.</summary>
    public int To => Math.Min(Page * Size, Total);

    /// <summary>
    /// نتیجهٔ یک فراخوانی صفحه‌دار را می‌گیرد: Total را ثبت می‌کند و ردیف‌های همان
    /// صفحه را برمی‌گرداند. اگر سرور صفحه‌بندی نکرده باشد (پاسخ آرایهٔ ساده)،
    /// ListOrPaged خودش TotalCount را برابر تعداد ردیف‌ها می‌گذارد، پس این کد فرقی نمی‌کند.
    /// </summary>
    public List<T> Take<T>(PagedResult<T> result)
    {
        Total = result.TotalCount;
        return result.Items ?? new List<T>();
    }

    /// <summary>رفتن به صفحهٔ p؛ مقدارهای نامعتبر نادیده گرفته می‌شوند.</summary>
    public void Go(int p)
    {
        if (p >= 1 && p <= Count) Page = p;
    }

    /// <summary>
    /// برگشت به صفحهٔ ۱ — باید هنگام تغییر فیلتر/جست‌وجو صدا زده شود، وگرنه کاربر
    /// در صفحهٔ ۷ یک فیلتر جدید می‌گذارد و نتیجهٔ خالی می‌بیند.
    /// </summary>
    public void Reset()
    {
        Page = 1;
        Total = 0;
    }

    /// <summary>
    /// تغییر اندازهٔ صفحه و برگشت به صفحهٔ ۱. بعد از صدا زدنش باید فهرست دوباره
    /// خوانده شود، چون ردیف‌های صفحهٔ جاری با اندازهٔ جدید فرق می‌کنند.
    /// </summary>
    public void SetSize(int size)
    {
        if (size < 1) return;
        Size = size;
        Page = 1;
    }
}
