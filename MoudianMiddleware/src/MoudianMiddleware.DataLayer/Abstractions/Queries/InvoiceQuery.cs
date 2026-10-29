using MoudianMiddleware.Contracts.Enums;

namespace MoudianMiddleware.DataLayer.Abstractions.Queries;

/// <summary>
/// پارامترهای پرس‌وجوی فهرست صورتحساب‌ها (لایه‌ی داده، بدون وابستگی به ViewModel).
/// </summary>
/// <remarks>
/// سرویس‌ها این شیء را از ViewModel فیلتر می‌سازند؛ این تفکیک مانع نشت قرارداد UI به لایه‌ی داده می‌شود.
/// </remarks>
public sealed class InvoiceQuery
{
    /// <summary>فیلتر وضعیت (اختیاری).</summary>
    public InvoiceStatus? Status { get; init; }

    /// <summary>فیلتر نوع صورتحساب (اختیاری).</summary>
    public InvoiceType? Type { get; init; }

    /// <summary>ابتدا بازه‌ی تاریخ صدور (اختیاری).</summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>انتها بازه‌ی تاریخ صدور (اختیاری).</summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>شناسه‌ی مشتری (اختیاری).</summary>
    public int? CustomerId { get; init; }

    /// <summary>جست‌وجوی متنی روی شماره‌ی فاکتور، نام خریدار، شماره‌ی مالیاتی و ارجاع.</summary>
    public string? Search { get; init; }

    /// <summary>تعداد رکوردهای ردشده.</summary>
    public int Skip { get; init; }

    /// <summary>تعداد رکوردهای درخواستی.</summary>
    public int Take { get; init; } = 20;

    /// <summary>مرتب‌سازی نزولی بر اساس تاریخ صدور.</summary>
    public bool Descending { get; init; } = true;
}
