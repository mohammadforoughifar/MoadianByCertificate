using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// تنظیمات اتصال هر مودی به سامانه‌ی مودیان (شناسه‌ی حافظه‌ی مالیاتی، شماره‌ی اقتصادی، آدرس محیط‌ها و حالت امضا).
/// </summary>
/// <remarks>
/// رابطه‌ی یک‌به‌یک با <see cref="Tenant"/> است و TenantId هم‌زمان کلید خارجی و ایندکس یکتاست.
/// هیچ کلید خصوصی در این جدول نگه‌داری نمی‌شود؛ کلید در <see cref="MoadianCredential"/> و به‌صورت رمزنگاری‌شده است.
/// </remarks>
public class MoadianSetting : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شناسه‌ی یکتای حافظه‌ی مالیاتی (۶ کاراکتر، مثل A11216).</summary>
    public string MemoryId { get; set; } = string.Empty;

    /// <summary>شماره‌ی اقتصادی فروشنده (فیلد <c>tins</c>، ۱۱ رقم).</summary>
    public string EconomicCode { get; set; } = string.Empty;

    /// <summary>شناسه/شماره‌ی ملی فروشنده (اختیاری، برای گزارش‌ها).</summary>
    public string? NationalId { get; set; }

    /// <summary>کد شعبه‌ی فروشنده (فیلد <c>sbc</c>، اختیاری).</summary>
    public string? BranchCode { get; set; }

    /// <summary>حالت امضای انتخاب‌شده برای این مودی.</summary>
    public MoadianSignatureMode SignatureMode { get; set; } = MoadianSignatureMode.Pkcs8Certificate;

    /// <summary>
    /// آیا حالت امضای آزمایشی (PrivateKeyOnly) توسط کاربر تأیید شده است؟
    /// </summary>
    /// <remarks>
    /// مقدار <c>false</c> یعنی اجازه‌ی استفاده از حالت آزمایشی وجود ندارد، حتی اگر SignatureMode آن را انتخاب کرده باشد.
    /// </remarks>
    public bool IsSignatureExperimentalApproved { get; set; }

    /// <summary>محیط فعال (آزمایشی/عملیاتی).</summary>
    public MoadianEnvironment ActiveEnvironment { get; set; } = MoadianEnvironment.Sandbox;

    /// <summary>آدرس سرویس محیط آزمایشی؛ تا دریافت آدرس رسمی خالی است («نیاز به تأیید»).</summary>
    public string? SandboxBaseUrl { get; set; }

    /// <summary>آدرس سرویس محیط عملیاتی (پیش‌فرض: https://tp.tax.gov.ir/requestsmanager).</summary>
    public string? ProductionBaseUrl { get; set; }

    /// <summary>زمان آخرین «تست اتصال».</summary>
    public DateTimeOffset? LastConnectionTestAt { get; set; }

    /// <summary>نتیجه‌ی آخرین تست اتصال.</summary>
    public bool? LastConnectionTestSucceeded { get; set; }

    /// <summary>پیام آخرین تست اتصال (برای نمایش به کاربر).</summary>
    public string? LastConnectionTestMessage { get; set; }

    /// <summary>آیا تنظیمات فعال است؟</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>مستأجر مربوطه.</summary>
    public Tenant? Tenant { get; set; }
}
