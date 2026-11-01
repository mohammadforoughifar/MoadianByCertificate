using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// وضعیت موقت و محدودشدهٔ درخواست OTP برای راه‌های تماس کاربر.
/// کد به‌صورت رمز‌شدهٔ Data Protection نگه‌داری می‌شود و هر کاربر برای هر کانال یک رکورد دارد.
/// </summary>
public sealed class UserContactOtpChallenge
{
    public const string MobilePurpose = "mobile";
    public const string EmailPurpose = "email";

    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(10)]
    public string Purpose { get; set; } = "";

    [MaxLength(254)]
    public string Destination { get; set; } = "";

    [MaxLength(1000)]
    public string ProtectedCode { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastSentAt { get; set; }
    public DateTime WindowStartedAt { get; set; }
    public int SendCount { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }

    public User? User { get; set; }
}
