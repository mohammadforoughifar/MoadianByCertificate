using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// نگاشت کد خطای سامانه‌ی مودیان به پیام فارسی و راهنمای اقدام (داده‌ی مرجع سراسری).
/// </summary>
public class MoadianErrorCode : AuditableEntity
{
    /// <summary>کد خطای سامانه (مثلاً <c>E1001</c>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>پیام فارسی قابل‌فهم برای کاربر.</summary>
    public string PersianMessage { get; set; } = string.Empty;

    /// <summary>پیام فنی/انگلیسی سامانه (برای پشتیبانی).</summary>
    public string? TechnicalMessage { get; set; }

    /// <summary>سطح اهمیت: Error/Warning (متن آزاد برای انعطاف).</summary>
    public string Severity { get; set; } = "Error";

    /// <summary>اقدام پیشنهادی برای رفع خطا.</summary>
    public string? SuggestedAction { get; set; }

    /// <summary>آیا این کد فعال است؟</summary>
    public bool IsActive { get; set; } = true;
}
