using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Core;

public interface IUserProfileService
{
    Task<UserProfileDto> GetProfileAsync(int userId, CancellationToken cancellationToken = default);
    Task RequestMobileOtpAsync(int userId, string? mobile, CancellationToken cancellationToken = default);
    Task VerifyMobileOtpAsync(int userId, string? code, CancellationToken cancellationToken = default);
    Task RequestEmailOtpAsync(int userId, string? email, CancellationToken cancellationToken = default);
    Task VerifyEmailOtpAsync(int userId, string? code, CancellationToken cancellationToken = default);
    Task ClearEmailAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// مدیریت پروفایلِ خودِ کاربر. نام‌ها صرفاً خواندنی‌اند؛ موبایل/ایمیل فقط بعد از OTP در حساب ذخیره می‌شوند.
/// </summary>
public sealed class UserProfileService : IUserProfileService
{
    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
    private readonly AppDbContext _db;
    private readonly IUserContactOtpDelivery _delivery;
    private readonly IDataProtectionProvider _protection;
    private readonly UserProfileOtpOptions _options;

    public UserProfileService(
        AppDbContext db,
        IUserContactOtpDelivery delivery,
        IDataProtectionProvider protection,
        IOptions<UserProfileOtpOptions> options)
    {
        _db = db;
        _delivery = delivery;
        _protection = protection;
        _options = options.Value;
    }

    public async Task<UserProfileDto> GetProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);
        var challenges = await _db.UserContactOtpChallenges.AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var mobileChallenge = challenges.FirstOrDefault(c => c.Purpose == UserContactOtpChallenge.MobilePurpose);
        var emailChallenge = challenges.FirstOrDefault(c => c.Purpose == UserContactOtpChallenge.EmailPurpose);
        var mobilePending = IsPending(mobileChallenge, now);
        var emailPending = IsPending(emailChallenge, now);

        return new UserProfileDto
        {
            UserId = user.Id,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Mobile = user.Mobile,
            MobileVerified = user.MobileVerified,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            PendingMobile = mobilePending ? mobileChallenge!.Destination : null,
            PendingEmail = emailPending ? emailChallenge!.Destination : null,
            MobileOtpPending = mobilePending,
            EmailOtpPending = emailPending,
            MobileOtpLocked = mobileChallenge?.LockedUntil is DateTime mobileLockedUntil && mobileLockedUntil > now,
            EmailOtpLocked = emailChallenge?.LockedUntil is DateTime emailLockedUntil && emailLockedUntil > now,
            MobileVerificationAvailable = _delivery.IsMobileConfigured,
            EmailVerificationAvailable = _delivery.IsEmailConfigured
        };
    }

    public async Task RequestMobileOtpAsync(int userId, string? mobile, CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);
        if (user.MobileVerified)
            throw new InvalidOperationException("شمارهٔ موبایلِ تأییدشده از تنظیمات شخصی قابل تغییر نیست.");
        if (!_delivery.IsMobileConfigured)
            throw new InvalidOperationException("درگاه کاوه‌نگار روی سرور فعال نیست؛ امکان تأیید شمارهٔ موبایل وجود ندارد.");

        var normalized = NormalizeMobile(mobile);
        if (normalized is null)
            throw new InvalidOperationException("شمارهٔ موبایل معتبر ایران را وارد کنید؛ مانند 09123456789.");
        var (_, code) = await PrepareChallengeAsync(
            userId, UserContactOtpChallenge.MobilePurpose, normalized, cancellationToken);
        await _delivery.SendMobileCodeAsync(normalized, code, cancellationToken);
    }

    public Task VerifyMobileOtpAsync(int userId, string? code, CancellationToken cancellationToken = default) =>
        VerifyAsync(userId, UserContactOtpChallenge.MobilePurpose, code, cancellationToken);

    public async Task RequestEmailOtpAsync(int userId, string? email, CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);
        var normalized = NormalizeEmail(email);
        if (user.EmailVerified && string.Equals(user.Email, normalized, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("همین نشانی ایمیل قبلاً تأیید شده است.");
        if (!_delivery.IsEmailConfigured)
            throw new InvalidOperationException("ارسال کد ایمیل روی سرور پیکربندی نشده است؛ با مدیر سامانه تماس بگیرید.");

        var (_, code) = await PrepareChallengeAsync(
            userId, UserContactOtpChallenge.EmailPurpose, normalized, cancellationToken);
        await _delivery.SendEmailCodeAsync(normalized, code, cancellationToken);
    }

    public Task VerifyEmailOtpAsync(int userId, string? code, CancellationToken cancellationToken = default) =>
        VerifyAsync(userId, UserContactOtpChallenge.EmailPurpose, code, cancellationToken);

    public async Task ClearEmailAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);
        user.Email = null;
        user.EmailVerified = false;

        var challenge = await FindChallengeAsync(userId, UserContactOtpChallenge.EmailPurpose, cancellationToken);
        if (challenge is not null)
            InvalidateChallenge(challenge, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(UserContactOtpChallenge Challenge, string Code)> PrepareChallengeAsync(
        int userId, string purpose, string destination, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var challenge = await FindChallengeAsync(userId, purpose, cancellationToken);
        if (challenge?.LockedUntil is DateTime lockedUntil && lockedUntil > now)
            throw new InvalidOperationException("پس از چند تلاش ناموفق، درخواست کد موقتاً مسدود شده است. کمی بعد دوباره تلاش کنید.");

        var cooldownSeconds = Math.Clamp(_options.ResendCooldownSeconds, 30, 3600);
        if (challenge is not null && challenge.LastSentAt > now.AddSeconds(-cooldownSeconds))
        {
            var secondsLeft = Math.Max(1, (int)Math.Ceiling((challenge.LastSentAt.AddSeconds(cooldownSeconds) - now).TotalSeconds));
            throw new InvalidOperationException($"برای ارسال دوبارهٔ کد، {secondsLeft} ثانیه صبر کنید.");
        }

        var maxSendsPerHour = Math.Clamp(_options.MaxSendsPerHour, 1, 10);
        if (challenge is null)
        {
            challenge = new UserContactOtpChallenge
            {
                UserId = userId,
                Purpose = purpose,
                WindowStartedAt = now
            };
            _db.UserContactOtpChallenges.Add(challenge);
        }
        else if (challenge.WindowStartedAt == default || now - challenge.WindowStartedAt >= SendWindow)
        {
            challenge.WindowStartedAt = now;
            challenge.SendCount = 0;
        }

        if (challenge.SendCount >= maxSendsPerHour)
            throw new InvalidOperationException("سقف ارسال کد تأیید در یک ساعت گذشته پر شده است. بعداً دوباره تلاش کنید.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        challenge.Destination = destination;
        challenge.ProtectedCode = Protector(userId, purpose, destination).Protect(code);
        challenge.CreatedAt = now;
        challenge.ExpiresAt = now.AddMinutes(Math.Clamp(_options.CodeTtlMinutes, 1, 15));
        challenge.LastSentAt = now;
        challenge.SendCount++;
        challenge.FailedAttempts = 0;
        challenge.LockedUntil = null;
        await _db.SaveChangesAsync(cancellationToken);
        return (challenge, code);
    }

    private async Task VerifyAsync(int userId, string purpose, string? submittedCode, CancellationToken cancellationToken)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);
        if (purpose == UserContactOtpChallenge.MobilePurpose && user.MobileVerified)
            throw new InvalidOperationException("شمارهٔ موبایلِ این حساب قبلاً تأیید شده است.");

        var challenge = await FindChallengeAsync(userId, purpose, cancellationToken);
        var now = DateTime.UtcNow;
        if (challenge is null)
            throw new InvalidOperationException("کد تأییدی برای این حساب در انتظار نیست؛ ابتدا کد جدید بگیرید.");
        if (challenge.LockedUntil is DateTime lockedUntil && lockedUntil > now)
            throw new InvalidOperationException("تعداد تلاش‌های ناموفق بیش از حد مجاز است؛ کمی بعد دوباره تلاش کنید.");
        if (challenge.ExpiresAt <= now || string.IsNullOrWhiteSpace(challenge.ProtectedCode)
            || string.IsNullOrWhiteSpace(challenge.Destination))
        {
            InvalidateChallenge(challenge, now);
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("کد تأیید منقضی شده است؛ کد جدید بگیرید.");
        }

        var normalizedCode = NormalizeOtpCode(submittedCode);
        string expectedCode;
        try
        {
            expectedCode = Protector(userId, purpose, challenge.Destination).Unprotect(challenge.ProtectedCode);
        }
        catch (CryptographicException)
        {
            InvalidateChallenge(challenge, now);
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("کد تأیید معتبر نیست؛ کد جدید بگیرید.");
        }

        if (!FixedTimeCodeEquals(expectedCode, normalizedCode))
        {
            challenge.FailedAttempts++;
            var maxAttempts = Math.Clamp(_options.MaxInvalidAttempts, 3, 10);
            if (challenge.FailedAttempts >= maxAttempts)
            {
                challenge.LockedUntil = now.AddMinutes(Math.Clamp(_options.LockoutMinutes, 1, 60));
                InvalidateChallenge(challenge, now, preserveLock: true);
            }
            await _db.SaveChangesAsync(cancellationToken);
            var isLocked = challenge.LockedUntil is DateTime lockUntil && lockUntil > now;
            throw new InvalidOperationException(isLocked
                ? "کد تأیید چند بار اشتباه وارد شد؛ درخواست کد موقتاً مسدود شده است."
                : "کد تأیید اشتباه است.");
        }

        if (purpose == UserContactOtpChallenge.MobilePurpose)
        {
            var normalizedMobile = NormalizeMobile(challenge.Destination);
            if (normalizedMobile is null)
                throw new InvalidOperationException("شمارهٔ مقصد معتبر نیست؛ کد جدید بگیرید.");
            user.Mobile = normalizedMobile;
            user.MobileVerified = true;
        }
        else
        {
            var normalizedEmail = NormalizeEmail(challenge.Destination);
            user.Email = normalizedEmail;
            user.EmailVerified = true;
        }

        InvalidateChallenge(challenge, now);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> GetActiveUserAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            throw new InvalidOperationException("کاربر فعال پیدا نشد؛ دوباره وارد سامانه شوید.");
        return user;
    }

    private Task<UserContactOtpChallenge?> FindChallengeAsync(int userId, string purpose, CancellationToken cancellationToken) =>
        _db.UserContactOtpChallenges.FirstOrDefaultAsync(
            c => c.UserId == userId && c.Purpose == purpose, cancellationToken);

    private IDataProtector Protector(int userId, string purpose, string destination) =>
        _protection.CreateProtector("Inventory.UserContactOtp.v1")
            .CreateProtector(userId.ToString(CultureInfo.InvariantCulture))
            .CreateProtector(purpose)
            .CreateProtector(destination);

    private static bool IsPending(UserContactOtpChallenge? challenge, DateTime now) =>
        challenge is not null
        && challenge.ExpiresAt > now
        && (!challenge.LockedUntil.HasValue || challenge.LockedUntil.Value <= now)
        && !string.IsNullOrWhiteSpace(challenge.Destination)
        && !string.IsNullOrWhiteSpace(challenge.ProtectedCode);

    private static void InvalidateChallenge(UserContactOtpChallenge challenge, DateTime now, bool preserveLock = false)
    {
        challenge.Destination = "";
        challenge.ProtectedCode = "";
        challenge.ExpiresAt = now;
        challenge.FailedAttempts = 0;
        if (!preserveLock) challenge.LockedUntil = null;
    }

    private static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalizedInput = NormalizeNumerals(value);
        if (normalizedInput.Any(ch => !IsAsciiDigit(ch) && !char.IsWhiteSpace(ch)
                                      && ch != '+' && ch != '-' && ch != '(' && ch != ')'
                                      && ch != '.' && ch != '/'))
            return null;
        var digits = normalizedInput.Where(IsAsciiDigit).ToArray();
        var number = new string(digits);
        if (number.StartsWith("0098", StringComparison.Ordinal) && number.Length == 14)
            number = "0" + number[4..];
        else if (number.StartsWith("98", StringComparison.Ordinal) && number.Length == 12)
            number = "0" + number[2..];
        else if (number.Length == 10 && number[0] == '9')
            number = "0" + number;

        return number.Length == 11 && number.StartsWith("09", StringComparison.Ordinal)
            ? number
            : null;
    }

    private static string NormalizeEmail(string? value)
    {
        var email = value?.Trim() ?? "";
        if (email.Length == 0)
            throw new InvalidOperationException("نشانی ایمیل را وارد کنید.");
        if (email.Length > 254 || email.Any(char.IsWhiteSpace)
            || !MailAddress.TryCreate(email, out var parsed)
            || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("نشانی ایمیل معتبر نیست.");
        return email.ToLowerInvariant();
    }

    private static string NormalizeOtpCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var digits = NormalizeNumerals(value).Where(IsAsciiDigit).ToArray();
        return digits.Length == 6 ? new string(digits) : "";
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private static string NormalizeNumerals(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= '\u06F0' and <= '\u06F9') result.Append((char)('0' + ch - '\u06F0'));
            else if (ch is >= '\u0660' and <= '\u0669') result.Append((char)('0' + ch - '\u0660'));
            else result.Append(ch);
        }
        return result.ToString();
    }

    private static bool FixedTimeCodeEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
