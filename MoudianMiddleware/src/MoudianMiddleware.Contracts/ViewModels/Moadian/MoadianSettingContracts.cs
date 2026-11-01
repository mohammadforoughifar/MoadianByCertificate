using MoudianMiddleware.Contracts.Enums;

namespace MoudianMiddleware.Contracts.ViewModels.Moadian;

/// <summary>ورودی تنظیمات اتصال یک tenant به سامانه‌ی مودیان؛ بدون credential و کلیدها.</summary>
public sealed class MoadianSettingWriteRequest
{
    /// <summary>شناسه‌ی حافظه‌ی مالیاتی.</summary>
    public string MemoryId { get; init; } = string.Empty;
    /// <summary>شماره‌ی اقتصادی فروشنده.</summary>
    public string EconomicCode { get; init; } = string.Empty;
    /// <summary>شناسه‌ی ملی اختیاری فروشنده.</summary>
    public string? NationalId { get; init; }
    /// <summary>کد شعبه‌ی فروشنده.</summary>
    public string? BranchCode { get; init; }
    /// <summary>راهبرد امضا.</summary>
    public MoadianSignatureMode SignatureMode { get; init; } = MoadianSignatureMode.Pkcs8Certificate;
    /// <summary>تأیید صریح استفاده از راهبرد private-key-only آزمایشی.</summary>
    public bool IsSignatureExperimentalApproved { get; init; }
    /// <summary>محیط فعال.</summary>
    public MoadianEnvironment ActiveEnvironment { get; init; } = MoadianEnvironment.Sandbox;
    /// <summary>نشانی Sandbox؛ برای فعال کردن محیط Sandbox باید صریحاً تعیین شود.</summary>
    public string? SandboxBaseUrl { get; init; }
    /// <summary>نشانی Production سفارشی؛ در صورت خالی بودن مقدار پیش‌فرض gateway به کار می‌رود.</summary>
    public string? ProductionBaseUrl { get; init; }
    /// <summary>فعال بودن اتصال این tenant.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>نمایش تنظیمات اتصال مودیان، بدون هیچ ماده‌ی محرمانه‌ی امضا.</summary>
public sealed class MoadianSettingViewModel
{
    /// <summary>شناسه‌ی tenant.</summary>
    public int TenantId { get; init; }
    /// <summary>شناسه‌ی حافظه‌ی مالیاتی.</summary>
    public string MemoryId { get; init; } = string.Empty;
    /// <summary>شماره‌ی اقتصادی فروشنده.</summary>
    public string EconomicCode { get; init; } = string.Empty;
    /// <summary>شناسه‌ی ملی فروشنده.</summary>
    public string? NationalId { get; init; }
    /// <summary>کد شعبه‌ی فروشنده.</summary>
    public string? BranchCode { get; init; }
    /// <summary>راهبرد امضا.</summary>
    public MoadianSignatureMode SignatureMode { get; init; }
    /// <summary>تأیید صریح راهبرد آزمایشی.</summary>
    public bool IsSignatureExperimentalApproved { get; init; }
    /// <summary>محیط فعال.</summary>
    public MoadianEnvironment ActiveEnvironment { get; init; }
    /// <summary>نشانی Sandbox.</summary>
    public string? SandboxBaseUrl { get; init; }
    /// <summary>نشانی Production سفارشی.</summary>
    public string? ProductionBaseUrl { get; init; }
    /// <summary>زمان آخرین تست اتصال.</summary>
    public DateTimeOffset? LastConnectionTestAt { get; init; }
    /// <summary>نتیجه‌ی آخرین تست اتصال.</summary>
    public bool? LastConnectionTestSucceeded { get; init; }
    /// <summary>فعال بودن اتصال.</summary>
    public bool IsActive { get; init; }
}
