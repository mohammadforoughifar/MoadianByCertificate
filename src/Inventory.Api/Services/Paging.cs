using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// کمک‌صفحه‌بندی یکپارچهٔ بک‌اند بر پایهٔ الگوی skip/take.
/// برای فهرست‌های دیتابیسی از ResultAsync / QueryAsync یا Request همراه ToPageListAsync استفاده شود.
/// Result و Slice قدیمی فقط برای مسیرهای هنوز مهاجرت‌نکرده و داده‌های غیر دیتابیسی باقی مانده‌اند.
///
/// قرارداد پاسخ (سازگار با کلاینت موجود):
/// • اگر کلاینت skip/take نفرستد (take تهی یا صفر و skip صفر) ← همان «آرایهٔ کامل» قبلی برگردانده می‌شود؛
///   یعنی هیچ رفتاری برای مصرف‌کنندگان فعلی تغییر نمی‌کند.
/// • اگر صفحه‌بندی درخواست شود ← آبجکت { total, items } برمی‌گردد:
///   total = تعداد کل ردیف‌های منطبق با فیلتر (برای ساخت صفحه‌ها در کلاینت) و
///   items = فقط ردیف‌های همان صفحه (Skip/Take).
///   لایهٔ ListOrPaged در کلاینت این شکل پاسخ را می‌فهمد.
/// </summary>
public static partial class Paging
{
    /// <summary>آیا صفحه‌بندی درخواست شده است؟ (take مثبت یا skip مثبت)</summary>
    public static bool Requested(int skip, int? take) => take is > 0 || skip > 0;

    /// <summary>
    /// تبدیل ورودی skip/take به شماره صفحه — برای سرویس‌هایی که هنوز با page/pageSize کار می‌کنند.
    /// اگر take داده نشود، همان page قبلی استفاده می‌شود (سازگاری کامل با کلاینت‌های فعلی).
    /// </summary>
    public static int ToPage(int? skip, int? take, int fallbackPage)
        => take is > 0 ? Math.Max(0, skip ?? 0) / take.Value + 1 : fallbackPage;

    /// <summary>تبدیل ورودی take به اندازه صفحه — اگر take داده نشود، همان pageSize قبلی.</summary>
    public static int ToPageSize(int? take, int fallbackPageSize)
        => take is > 0 ? take.Value : fallbackPageSize;

    /// <summary>
    /// صفحه‌بندی در سطح دیتابیس روی IQueryable — Skip/Take قبل از materialize شدن اعمال می‌شود
    /// تا فقط ردیف‌های همان صفحه از دیتابیس خوانده شوند. بدون درخواست صفحه‌بندی ← فهرست کامل.
    /// </summary>
    public static async Task<object> ResultAsync<T>(IQueryable<T> query, int skip, int? take)
    {
        if (!Requested(skip, take))
            return await query.ToListAsync();

        var total = await query.CountAsync();
        var sliced = query;
        if (skip > 0) sliced = sliced.Skip(skip);
        if (take is > 0) sliced = sliced.Take(take.Value);
        var items = await sliced.ToListAsync();
        return new { total, items };
    }

    /// <summary>
    /// Legacy in-memory paging. Do not use for new database-backed endpoints.
    /// Use ResultAsync(request => service.ListAsync(pagination: request), skip, take) instead.
    /// صفحه‌بندی روی فهرست materialize شده (مسیرهای هنوز مهاجرت‌نکرده) با همان Skip/Take.
    /// بدون درخواست صفحه‌بندی ← همان فهرست کامل (بدون تغییر شکل پاسخ).
    /// </summary>
    public static object Result<T>(IEnumerable<T> all, int skip, int? take)
    {
        var list = all as IReadOnlyList<T> ?? all.ToList();
        if (!Requested(skip, take))
            return list;

        return new { total = list.Count, items = Slice(list, skip, take).ToList() };
    }

    /// <summary>
    /// صفحه‌بندی روی Dictionary — شکل «items» همان آبجکت کلید/مقدار می‌ماند (نه آرایه)
    /// تا پاسخ‌های lookup-مانند حتی در حالت صفحه‌بندی‌شده هم شکل خود را حفظ کنند.
    /// </summary>
    public static object Result<TKey, TValue>(IDictionary<TKey, TValue> all, int skip, int? take)
        where TKey : notnull
    {
        if (!Requested(skip, take))
            return all;

        var items = Slice(all, skip, take).ToDictionary(kv => kv.Key, kv => kv.Value);
        return new { total = all.Count, items };
    }

    /// <summary>اعمال Skip و Take با کمینه‌سازی (skip منفی ← صفر؛ take تهی/صفر ← بدون محدودیت).</summary>
    public static IEnumerable<T> Slice<T>(IEnumerable<T> source, int skip, int? take)
    {
        if (skip > 0) source = source.Skip(skip);
        if (take is > 0) source = source.Take(take.Value);
        return source;
    }

    /// <summary>
    /// نتیجهٔ یک کوئریِ صفحه‌بندی‌شده — برای endpointهایی که ردیف‌های خام را می‌خوانند و
    /// سپس در حافظه به DTO نگاشت می‌کنند. با Result() پاسخ نهایی (آرایه یا { total, items }) ساخته می‌شود.
    /// </summary>
    public sealed class Page<T>
    {
        public List<T> Rows { get; init; } = new();
        public int? Total { get; init; }
        public bool IsPaged { get; init; }

        /// <summary>پاسخ نهایی: حالت صفحه‌بندی‌شده ← { total, items }؛ در غیر این صورت ← همان آرایهٔ items (سازگار با قبل).</summary>
        public object Result<TDto>(IEnumerable<TDto> items)
            => IsPaged ? new { total = Total ?? Rows.Count, items } : items;

        /// <summary>پاسخ نهایی با خود ردیف‌ها (بدون نگاشت DTO).</summary>
        public object Result() => Result(Rows);
    }

    /// <summary>
    /// صفحه‌بندی سطح دیتابیس برای کوئری‌هایی که بعداً در حافظه نگاشت می‌شوند.
    /// defaultCap = سقف ردیف‌هایی که «بدون درخواست صفحه‌بندی» برگردانده می‌شود (رفتار قبلی بسیاری از endpointها).
    /// </summary>
    public static async Task<Page<T>> QueryAsync<T>(IQueryable<T> query, int skip, int? take, int? defaultCap = null)
    {
        if (!Requested(skip, take))
        {
            var uncapped = defaultCap is > 0 ? query.Take(defaultCap.Value) : query;
            return new Page<T> { Rows = await uncapped.ToListAsync(), IsPaged = false };
        }

        var total = await query.CountAsync();
        var sliced = query;
        if (skip > 0) sliced = sliced.Skip(skip);
        if (take is > 0) sliced = sliced.Take(take.Value);
        return new Page<T> { Rows = await sliced.ToListAsync(), Total = total, IsPaged = true };
    }
}
