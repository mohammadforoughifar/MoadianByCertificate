namespace Inventory.Shared.Dtos;

/// <summary>اطلاعات پایه و راه‌های تماسِ قابل‌مدیریت توسط خودِ کاربر.</summary>
public sealed class UserProfileDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Mobile { get; set; }
    public bool MobileVerified { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }

    /// <summary>نشانی در انتظار تأیید؛ تا تأیید کد، جایگزین راه تماس فعلی نمی‌شود.</summary>
    public string? PendingMobile { get; set; }
    public string? PendingEmail { get; set; }
    public bool MobileOtpPending { get; set; }
    public bool EmailOtpPending { get; set; }
    public bool MobileOtpLocked { get; set; }
    public bool EmailOtpLocked { get; set; }

    public bool MobileVerificationAvailable { get; set; }
    public bool EmailVerificationAvailable { get; set; }
}

/// <summary>نشانی مقصد دریافت کد یک‌بارمصرف.</summary>
public sealed class UserContactOtpRequestDto
{
    public string Destination { get; set; } = "";
}

/// <summary>کد یک‌بارمصرف واردشده توسط کاربر.</summary>
public sealed class UserContactOtpVerifyDto
{
    public string Code { get; set; } = "";
}
