namespace MoudianMiddleware.Contracts.Common;

/// <summary>
/// نتیجه‌ی صفحه‌بندی‌شده؛ برای همه‌ی فهرست‌های API یکسان است.
/// </summary>
/// <typeparam name="T">نوع آیتم‌های صفحه.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>آیتم‌های صفحه‌ی جاری.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>شماره‌ی صفحه (از ۱ شروع می‌شود).</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>اندازه‌ی صفحه.</summary>
    public int PageSize { get; init; } = PaginationQuery.DefaultPageSize;

    /// <summary>تعداد کل رکوردهای مطابق فیلتر.</summary>
    public int TotalCount { get; init; }

    /// <summary>تعداد کل صفحات.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>آیا صفحه‌ی قبلی وجود دارد؟</summary>
    public bool HasPrevious => PageNumber > 1;

    /// <summary>آیا صفحه‌ی بعدی وجود دارد؟</summary>
    public bool HasNext => PageNumber < TotalPages;

    /// <summary>ساخت نتیجه‌ی صفحه‌بندی‌شده.</summary>
    /// <param name="items">آیتم‌های صفحه.</param>
    /// <param name="totalCount">تعداد کل.</param>
    /// <param name="query">پارامترهای صفحه‌بندی.</param>
    /// <returns>نتیجه‌ی صفحه‌بندی‌شده.</returns>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, PaginationQuery query)
        => new()
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };

    /// <summary>ساخت نتیجه‌ی خالی.</summary>
    /// <param name="query">پارامترهای صفحه‌بندی.</param>
    /// <returns>نتیجه‌ی خالی.</returns>
    public static PagedResult<T> Empty(PaginationQuery query)
        => new() { Items = Array.Empty<T>(), TotalCount = 0, PageNumber = query.PageNumber, PageSize = query.PageSize };
}
