using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// خریدار (مشتری)؛ حقیقی یا حقوقی، با کد ملی/شناسه‌ی ملی، شماره‌ی اقتصادی و کد پستی.
/// </summary>
/// <remarks>
/// اطلاعات خریدار هنگام صدور صورتحساب در خود فاکتور «عکس‌برداری» (snapshot) می‌شود؛
/// بنابراین ویرایش بعدی مشتری، فاکتورهای ارسال‌شده را تغییر نمی‌دهد.
/// </remarks>
public class Customer : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>کد داخلی مشتری (یکتا در هر مودی).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>نام مشتری یا نام حقوقی.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>نوع شخص (حقیقی/حقوقی).</summary>
    public BuyerType BuyerType { get; set; } = BuyerType.NaturalPerson;

    /// <summary>کد ملی (حقیقی) یا شناسه‌ی ملی (حقوقی).</summary>
    public string? NationalId { get; set; }

    /// <summary>شماره‌ی اقتصادی خریدار (فیلد <c>tinb</c>).</summary>
    public string? EconomicCode { get; set; }

    /// <summary>کد پستی خریدار (فیلد <c>bpc</c>، ۱۰ رقم).</summary>
    public string? PostalCode { get; set; }

    /// <summary>نشانی خریدار (فیلد <c>bbc</c>/آدرس).</summary>
    public string? Address { get; set; }

    /// <summary>تلفن.</summary>
    public string? Phone { get; set; }

    /// <summary>ایمیل.</summary>
    public string? Email { get; set; }

    /// <summary>کد مشتری در سیستم خارجی (ERP) برای نگاشت.</summary>
    public string? ExternalCode { get; set; }

    /// <summary>آیا مشتری فعال است؟</summary>
    public bool IsActive { get; set; } = true;
}
