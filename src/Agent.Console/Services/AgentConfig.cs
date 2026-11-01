using System.Text.Json;

namespace InventoryAgent.Services;

/// <summary>
/// تنظیمات ایجنت — از فایل agent.json کنار فایل اجرایی خوانده می‌شود
/// و هر سوییچ خط فرمان روی آن override می‌کند.
/// </summary>
public sealed class AgentConfig
{
    /// <summary>آدرس پایه‌ی API، مثل: http://192.168.1.10:5000</summary>
    public string Server { get; set; } = "http://localhost:5000";

    /// <summary>فاصله‌ی ارسال دوره‌ای در حالت watch (دقیقه)</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>پایش چاپگرهای محلی (USB) و ارسال وضعیت به ماژول «ماشین‌های اداری»</summary>
    public bool MonitorPrinters { get; set; } = true;

    /// <summary>فاصله‌ی ارسال وضعیت چاپگرها در حالت watch (دقیقه) — سبک است و جدا از گزارش سخت‌افزار</summary>
    public int PrinterIntervalMinutes { get; set; } = 5;

    /// <summary>در صورت خاموش بودن، لاگ چاپ ویندوز (PrintService/Operational) برای شمارش صفحات فعال شود</summary>
    public bool EnablePrintLog { get; set; } = true;

    /// <summary>شناسه‌ی ثابت سیستم — اگر خالی باشد خودکار ساخته و ذخیره می‌شود</summary>
    public string AgentId { get; set; } = "";

    /// <summary>توکن JWT (اختیاری — فعلاً لازم نیست چون endpoint ایجنت باز است)</summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// کلید مشترک ایجنت — اگر سرور مقدار Agent:Key داشته باشد، همین مقدار باید اینجا
    /// (یا با سوییچ --key) داده شود وگرنه سرور درخواست را با 401 رد می‌کند.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>ورود با نام کاربری/رمز (اختیاری — برای آینده اگر endpoint محافظت شد)</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>نادیده گرفتن خطای گواهی HTTPS (فقط شبکه‌ی داخلی!)</summary>
    public bool Insecure { get; set; } = false;

    public static string DefaultPath
        => Path.Combine(AppContext.BaseDirectory, "agent.json");

    public static AgentConfig Load(string? path)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AgentConfig>(json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
                if (cfg != null) return cfg;
            }
        }
        catch { }
        return new AgentConfig();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void Normalize()
    {
        Server = (Server ?? "").Trim().TrimEnd('/');
        if (Server.Length == 0) Server = "http://localhost:5000";
        if (IntervalMinutes <= 0) IntervalMinutes = 60;
        PrinterIntervalMinutes = Math.Clamp(PrinterIntervalMinutes <= 0 ? 5 : PrinterIntervalMinutes, 1, 1440);
        AgentId = (AgentId ?? "").Trim();
        Token = (Token ?? "").Trim();
        Key = (Key ?? "").Trim();
        Username = (Username ?? "").Trim();
    }
}
