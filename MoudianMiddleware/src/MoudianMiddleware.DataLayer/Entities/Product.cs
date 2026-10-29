using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// کالا یا خدمت قابل فروش؛ حاوی شناسه‌ی کالا/خدمت (sstid)، واحد و نرخ مالیات بر ارزش افزوده.
/// </summary>
public class Product : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شناسه‌ی کالا/خدمت مطابق فهرست رسمی مودیان (۱۳ رقم).</summary>
    public string Sstid { get; set; } = string.Empty;

    /// <summary>شرح کالا/خدمت (فیلد <c>sstt</c>).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>شناسه‌ی واحد اندازه‌گیری.</summary>
    public int MeasuringUnitId { get; set; }

    /// <summary>مبلغ واحد پیش‌فرض (ریال).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده (درصد؛ مثلاً 10 یا 9 یا 0).</summary>
    public decimal VatRate { get; set; }

    /// <summary>آیا خدمت است (نه کالا)؟</summary>
    public bool IsService { get; set; }

    /// <summary>کد HS گمرکی (اختیاری، برای کالاهای صادراتی/وارداتی).</summary>
    public string? HsCode { get; set; }

    /// <summary>توضیحات داخلی.</summary>
    public string? Description { get; set; }

    /// <summary>آیا قابل انتخاب در صورتحساب است؟</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>واحد اندازه‌گیری مربوطه.</summary>
    public MeasuringUnit? MeasuringUnit { get; set; }
}
