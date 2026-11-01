namespace MoudianMiddleware.CoreLayer.Infrastructure.Security;

/// <summary>تنظیمات کلیدهای رمزنگاری envelope؛ مقدار کلیدها باید Base64 از ۳۲ بایت باشد.</summary>
public sealed class SecretProtectionOptions
{
    /// <summary>نام بخش پیکربندی.</summary>
    public const string SectionName = "SecretProtection";
    /// <summary>نسخه‌ی فعال برای رمزنگاری داده‌ی جدید.</summary>
    public int ActiveKeyVersion { get; set; } = 1;
    /// <summary>نگاشت نسخه به کلید ۲۵۶بیتی Base64؛ کلیدها فقط از secret store/environment تأمین شوند.</summary>
    public Dictionary<int, string> KeysBase64 { get; set; } = new();
}
