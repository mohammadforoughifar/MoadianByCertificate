using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Enums;

namespace MoudianMiddleware.Contracts.ViewModels.Customers;

/// <summary>ورودی ایجاد یا ویرایش خریدار.</summary>
public sealed class CustomerWriteRequest
{
    /// <summary>کد داخلی مشتری در ERP.</summary>
    public string Code { get; init; } = string.Empty;
    /// <summary>نام خریدار یا نام حقوقی.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>نوع شخص.</summary>
    public BuyerType BuyerType { get; init; }
    /// <summary>کد ملی/شناسه‌ی ملی.</summary>
    public string? NationalId { get; init; }
    /// <summary>شماره‌ی اقتصادی.</summary>
    public string? EconomicCode { get; init; }
    /// <summary>کد پستی.</summary>
    public string? PostalCode { get; init; }
    /// <summary>نشانی.</summary>
    public string? Address { get; init; }
    /// <summary>شماره‌ی تلفن.</summary>
    public string? Phone { get; init; }
    /// <summary>نشانی ایمیل.</summary>
    public string? Email { get; init; }
    /// <summary>کد متناظر در ERP.</summary>
    public string? ExternalCode { get; init; }
    /// <summary>فعال بودن مشتری.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>مدل نمایشی مشتری.</summary>
public sealed class CustomerViewModel
{
    /// <summary>شناسه‌ی پایگاه داده.</summary>
    public int Id { get; init; }
    /// <summary>کد مشتری.</summary>
    public string Code { get; init; } = string.Empty;
    /// <summary>نام مشتری.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>نوع شخص.</summary>
    public BuyerType BuyerType { get; init; }
    /// <summary>کد ملی/شناسه‌ی ملی.</summary>
    public string? NationalId { get; init; }
    /// <summary>شماره‌ی اقتصادی.</summary>
    public string? EconomicCode { get; init; }
    /// <summary>کد پستی.</summary>
    public string? PostalCode { get; init; }
    /// <summary>نشانی.</summary>
    public string? Address { get; init; }
    /// <summary>شماره‌ی تلفن.</summary>
    public string? Phone { get; init; }
    /// <summary>نشانی ایمیل.</summary>
    public string? Email { get; init; }
    /// <summary>کد متناظر ERP.</summary>
    public string? ExternalCode { get; init; }
    /// <summary>فعال بودن مشتری.</summary>
    public bool IsActive { get; init; }
}

/// <summary>فیلتر فهرست مشتری‌ها.</summary>
public sealed class CustomerListQuery : PaginationQuery
{
    /// <summary>فیلتر اختیاری نوع خریدار.</summary>
    public BuyerType? BuyerType { get; init; }
    /// <summary>فیلتر اختیاری فعال بودن.</summary>
    public bool? IsActive { get; init; }
}
