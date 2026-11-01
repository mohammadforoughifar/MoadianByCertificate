using MoudianMiddleware.Contracts.Common;

namespace MoudianMiddleware.Contracts.ViewModels.Products;

/// <summary>ورودی ایجاد یا ویرایش کالا/خدمت.</summary>
public sealed class ProductWriteRequest
{
    /// <summary>شناسه‌ی کالا/خدمت سامانه‌ی مودیان.</summary>
    public string Sstid { get; init; } = string.Empty;
    /// <summary>عنوان نمایشی کالا/خدمت.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>شناسه‌ی واحد اندازه‌گیری.</summary>
    public int MeasuringUnitId { get; init; }
    /// <summary>مبلغ واحد پیش‌فرض به ریال.</summary>
    public decimal UnitPrice { get; init; }
    /// <summary>نرخ مالیات بر ارزش افزوده برحسب درصد.</summary>
    public decimal VatRate { get; init; }
    /// <summary>آیا قلم خدمت است؟</summary>
    public bool IsService { get; init; }
    /// <summary>کد HS گمرکی اختیاری.</summary>
    public string? HsCode { get; init; }
    /// <summary>شرح داخلی اختیاری.</summary>
    public string? Description { get; init; }
    /// <summary>فعال بودن قلم.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>نمایش امن و فقط‌خواندنی کالا/خدمت.</summary>
public sealed class ProductViewModel
{
    /// <summary>شناسه‌ی پایگاه داده.</summary>
    public int Id { get; init; }
    /// <summary>شناسه‌ی مودی مالک رکورد.</summary>
    public int TenantId { get; init; }
    /// <summary>شناسه‌ی کالا/خدمت.</summary>
    public string Sstid { get; init; } = string.Empty;
    /// <summary>عنوان نمایشی.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>شناسه‌ی واحد اندازه‌گیری.</summary>
    public int MeasuringUnitId { get; init; }
    /// <summary>کد واحد اندازه‌گیری.</summary>
    public string? MeasuringUnitCode { get; init; }
    /// <summary>مبلغ واحد به ریال.</summary>
    public decimal UnitPrice { get; init; }
    /// <summary>نرخ مالیات بر ارزش افزوده.</summary>
    public decimal VatRate { get; init; }
    /// <summary>آیا خدمت است؟</summary>
    public bool IsService { get; init; }
    /// <summary>کد HS اختیاری.</summary>
    public string? HsCode { get; init; }
    /// <summary>شرح داخلی.</summary>
    public string? Description { get; init; }
    /// <summary>فعال بودن قلم.</summary>
    public bool IsActive { get; init; }
}

/// <summary>فیلتر فهرست کالاها.</summary>
public sealed class ProductListQuery : PaginationQuery
{
    /// <summary>فقط کالاهای فعال یا غیرفعال؛ null یعنی همه.</summary>
    public bool? IsActive { get; init; }
}
