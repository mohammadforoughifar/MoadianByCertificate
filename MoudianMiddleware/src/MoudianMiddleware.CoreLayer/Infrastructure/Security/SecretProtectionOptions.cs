namespace MoudianMiddleware.CoreLayer.Infrastructure.Security;

/// <summary>تنظیمات کلیدهای رمزنگاری envelope؛ مقدار کلیدها باید Base64 از ۳۲ بایت باشد.</summary>
public sealed class SecretProtectionOptions
{
    /// <summary>نام بخش پیکربندی.</summary>
    public const string SectionName = "SecretProtection";
    /// <summary>نسخه‌ی فعال برای رمزنگاری داده‌ی جدید.</summary>
    public int ActiveKeyVersion { get; set; } = 1;
    /// <summary>نگاشت نسخه به کلید ۲۵۶بیتی Base64؛ این مقادیر فقط از متغیرهای محیطی بارگذاری می‌شوند.</summary>
    public Dictionary<int, string> KeysBase64 { get; set; } = new();

    /// <summary>بارگذاری کلیدها فقط از MOUDIAN__MASTER_KEY و MOUDIAN__MASTER_KEY_V{version}.</summary>
    public static SecretProtectionOptions FromEnvironment()
    {
        var options = new SecretProtectionOptions();
        var active = Environment.GetEnvironmentVariable("MOUDIAN__MASTER_KEY_ACTIVE_VERSION");
        if (!string.IsNullOrWhiteSpace(active))
        {
            if (!int.TryParse(active, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) || version <= 0)
                throw new InvalidOperationException("MOUDIAN__MASTER_KEY_ACTIVE_VERSION must be a positive integer.");
            options.ActiveKeyVersion = version;
        }

        var legacy = Environment.GetEnvironmentVariable("MOUDIAN__MASTER_KEY");
        if (!string.IsNullOrWhiteSpace(legacy)) options.KeysBase64[1] = legacy;
        const string prefix = "MOUDIAN__MASTER_KEY_V";
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name || entry.Value is not string value || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(name[prefix.Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) || version <= 0)
                throw new InvalidOperationException($"Invalid KEK environment variable name: {name}.");
            options.KeysBase64[version] = value;
        }
        options.Validate();
        return options;
    }

    /// <summary>اعتبارسنجی نسخه‌ی فعال و طول تمام کلیدهای نگه‌داری‌شده.</summary>
    public void Validate()
    {
        if (ActiveKeyVersion <= 0 || !KeysBase64.ContainsKey(ActiveKeyVersion))
            throw new InvalidOperationException("The active KEK must be configured in environment variables.");
        foreach (var (version, encoded) in KeysBase64)
        {
            if (version <= 0) throw new InvalidOperationException("KEK versions must be positive integers.");
            byte[] key;
            try { key = Convert.FromBase64String(encoded); }
            catch (FormatException exception) { throw new InvalidOperationException($"KEK version {version} must be valid Base64.", exception); }
            try
            {
                if (key.Length != 32) throw new InvalidOperationException($"KEK version {version} must contain exactly 32 bytes.");
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
        }
    }
}
