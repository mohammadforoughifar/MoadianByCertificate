using InventoryAgent.Models;

namespace InventoryAgent.Collectors;

/// <summary>واسط جمع‌کننده‌ی سخت‌افزار — برای هر سیستم‌عامل یک پیاده‌سازی.</summary>
public interface IHardwareCollector
{
    /// <summary>نام سیستم‌عامل، مثل: Microsoft Windows 11 Pro</summary>
    string OsName { get; }

    /// <summary>جمع‌آوری همه‌ی قطعات. هرگز نباید exception بیرون بدهد (حداکثر بخشی خالی می‌ماند).</summary>
    DetailsDocument Collect();
}

public static class CollectorFactory
{
    public static IHardwareCollector Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCollector();
        if (OperatingSystem.IsLinux())
            return new LinuxCollector();
        throw new PlatformNotSupportedException(
            "این ایجنت فعلاً فقط روی Windows و Linux پشتیبانی می‌شود. / This agent supports Windows and Linux only.");
    }
}

public static class SizeUtil
{
    /// <summary>بایت به گیگابایت (GiB) گرد شده؛ اگر مثبت ولی زیر ۱GB بود، ۱ برمی‌گرداند.</summary>
    public static int BytesToGb(ulong bytes)
    {
        if (bytes == 0) return 0;
        var gb = (int)Math.Round(bytes / (1024.0 * 1024 * 1024));
        return gb <= 0 ? 1 : gb;
    }

    public static int BytesToGb(long bytes) => bytes <= 0 ? 0 : BytesToGb((ulong)bytes);

    public static string Clean(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? "" : s;
    }
}
