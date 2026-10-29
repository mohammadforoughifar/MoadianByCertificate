namespace MoudianMiddleware.Contracts.Common;

/// <summary>
/// پارامترهای پایه‌ی صفحه‌بندی و مرتب‌سازی؛ کلاس پایه‌ی همه‌ی فیلترهای فهرست.
/// </summary>
public class PaginationQuery
{
    /// <summary>حداقل شماره‌ی صفحه.</summary>
    public const int MinPageNumber = 1;

    /// <summary>اندازه‌ی پیش‌فرض صفحه.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>حداکثر اندازه‌ی صفحه (برای جلوگیری از کوئری سنگین).</summary>
    public const int MaxPageSize = 200;

    private int _pageNumber = MinPageNumber;
    private int _pageSize = DefaultPageSize;

    /// <summary>شماره‌ی صفحه (از ۱). مقادیر کوچک‌تر به ۱ اصلاح می‌شوند.</summary>
    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < MinPageNumber ? MinPageNumber : value;
    }

    /// <summary>اندازه‌ی صفحه؛ بین ۱ و <see cref="MaxPageSize"/> محدود می‌شود.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < MinPageNumber => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>تعداد رکوردهایی که باید رد شوند (برای Skip).</summary>
    public int Skip => (PageNumber - MinPageNumber) * PageSize;

    /// <summary>عبارت جست‌وجوی متنی (اختیاری).</summary>
    public string? Search { get; set; }

    /// <summary>نام ستون مرتب‌سازی (اختیاری).</summary>
    public string? SortBy { get; set; }

    /// <summary>مرتب‌سازی نزولی؟</summary>
    public bool Descending { get; set; }
}
