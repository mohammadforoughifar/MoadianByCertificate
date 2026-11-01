using System.Text.Json;

namespace Inventory.Api.Services;

/// <summary>محتوای فایل کلیدهای VAPID که از داخل «تنظیمات» سامانه ساخته می‌شود.</summary>
public sealed class PushKeyFile
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// نگهداری کلیدهای کلید اعلان (VAPID) در فایل <c>App_Data/push-vapid.json</c>.
/// هدف: مدیر بتواند کلیدها را از داخل خود نرم‌افزار (تنظیمات ← اعلان گوشی) بسازد و فعال کند،
/// بدون ویرایش appsettings.json و بدون ری‌استارت سرویس. اگر فایلی نبود، مقادیر فایل/متغیر محیطی سرور معتبر می‌مانند.
/// </summary>
public sealed class PushKeyStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly ILogger<PushKeyStore> _log;

    public PushKeyStore(IHostEnvironment env, ILogger<PushKeyStore> log)
    {
        _log = log;
        var configured = Environment.GetEnvironmentVariable("PUSH_VAPID_FILE");
        FilePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "push-vapid.json")
            : configured);
    }

    /// <summary>مسیر کامل فایل کلیدها (برای نمایش به مدیر در لاگ/UI).</summary>
    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public PushKeyFile? Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var file = JsonSerializer.Deserialize<PushKeyFile>(File.ReadAllText(FilePath), Json);
            return string.IsNullOrWhiteSpace(file?.PublicKey) || string.IsNullOrWhiteSpace(file?.PrivateKey) ? null : file;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "خواندن فایل کلیدهای اعلان ناموفق بود: {Path}", FilePath);
            return null;
        }
    }

    /// <summary>ذخیره‌ی اتمیک کلیدها (نوشتن در فایل موقت، سپس جابه‌جایی) با دسترسی محدود به کاربر سرویس.</summary>
    public void Save(PushKeyFile keys)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        keys.CreatedAtUtc = DateTime.UtcNow;
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(keys, Json));
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { /* سیستم فایل پشتیبانی نمی‌کند */ }
        }
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>حذف فایل کلیدها تا مقدار فایل/متغیر محیطی سرور دوباره مرجع شود.</summary>
    public bool Delete()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            File.Delete(FilePath);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "حذف فایل کلیدهای اعلان ناموفق بود: {Path}", FilePath);
            return false;
        }
    }

    /// <summary>در زمان راه‌اندازی سرویس: اگر کلیدی از داخل نرم‌افزار ساخته شده باشد، آن را زنده اعمال می‌کند.</summary>
    public void Load(PushSettings settings)
    {
        var keys = Read();
        if (keys == null)
        {
            if (settings.IsConfigured) _log.LogInformation("کلیدهای اعلان (VAPID) از فایل/متغیرهای محیطی سرور خوانده شد؛ منبع: {Source}", settings.Source);
            else _log.LogInformation("کلیدهای اعلان (VAPID) تنظیم نشده‌اند؛ مدیر می‌تواند از «تنظیمات ← اعلان گوشی» آن‌ها را بسازد.");
            return;
        }
        if (settings.Apply(keys.PublicKey, keys.PrivateKey, keys.Subject, out var error)) _log.LogInformation("کلیدهای اعلان از فایل {Path} اعمال شد.", FilePath);
        else _log.LogWarning("کلیدهای ذخیره‌شده در {Path} معتبر نیستند ({Error})؛ از کلیدهای فایل سرور استفاده می‌شود.", FilePath, error);
    }
}
