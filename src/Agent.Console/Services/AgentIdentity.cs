using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace InventoryAgent.Services;

/// <summary>
/// شناسه‌ی پایدار سیستم (AgentId) — سرور با همین کلید، سیستم‌ها را upsert می‌کند.
/// اولویت: ۱) مقدار دستی کانفیگ ۲) فایل ذخیره‌شده‌ی قبلی ۳) شناسه‌ی سخت‌افزاری سیستم.
/// </summary>
public static class AgentIdentity
{
    public static string Resolve(string? configuredId)
    {
        if (!string.IsNullOrWhiteSpace(configuredId))
            return configuredId.Trim();

        // ۱) اگر قبلاً ساخته و ذخیره شده، همان
        var saved = LoadSaved();
        if (!string.IsNullOrWhiteSpace(saved))
            return saved.Trim();

        // ۲) شناسه‌ی سخت‌افزاری پایدار
        var id = ReadHardwareId() ?? BuildFallbackId();

        // ۳) ذخیره برای اجراهای بعدی (بهترین تلاش — خطا مهم نیست)
        TrySave(id);
        return id;
    }

    // ---------- خواندن شناسه‌ی سخت‌افزاری ----------

    private static string? ReadHardwareId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var uuid = ReadWindowsUuid();
                if (!string.IsNullOrWhiteSpace(uuid))
                    return "WIN-" + uuid.Trim().ToUpperInvariant();
            }
            else if (OperatingSystem.IsLinux())
            {
                foreach (var p in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                {
                    try
                    {
                        if (File.Exists(p))
                        {
                            var mid = File.ReadAllText(p).Trim();
                            if (mid.Length >= 8) return "LINUX-" + mid.ToUpperInvariant();
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return null;
    }

    private static string? ReadWindowsUuid()
    {
        // جدا نگه داشته شده تا روی لینوکس حتی اسم System.Management لمس نشود
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return WindowsIdentityReader.ReadSystemUuid();
        }
        catch { return null; }
    }

    private static string BuildFallbackId()
    {
        // هش پایدار از MACها + نام ماشین (اگر WMI/فایل در دسترس نبود)
        try
        {
            var sb = new StringBuilder();
            sb.Append(Environment.MachineName);
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var mac = nic.GetPhysicalAddress().ToString();
                    if (mac.Length > 0) sb.Append('|').Append(mac);
                }
                catch { }
            }
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return "GEN-" + Convert.ToHexString(hash)[..12];
        }
        catch
        {
            return "GEN-" + Environment.MachineName.ToUpperInvariant();
        }
    }

    // ---------- ذخیره‌سازی ----------

    private static string? IdFilePath()
    {
        // ویندوز: ProgramData (مشترک همه‌ی کاربران) وگرنه کنار exe
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InventoryAgent");
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".w");
                File.WriteAllText(probe, "w");
                File.Delete(probe);
                return Path.Combine(dir, "agent.id");
            }
            catch { }
        }
        // لینوکس: etc وگرنه کنار exe
        if (OperatingSystem.IsLinux())
        {
            try
            {
                const string dir = "/etc/inventory-agent";
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".w");
                File.WriteAllText(probe, "w");
                File.Delete(probe);
                return Path.Combine(dir, "agent.id");
            }
            catch { }
        }
        try { return Path.Combine(AppContext.BaseDirectory, "agent.id"); }
        catch { return null; }
    }

    private static string? LoadSaved()
    {
        try
        {
            var p = IdFilePath();
            if (p != null && File.Exists(p))
            {
                var id = File.ReadAllText(p).Trim();
                if (id.Length > 0) return id;
            }
            // سازگاری عقب‌رو: فایل کنار exe
            var legacy = Path.Combine(AppContext.BaseDirectory, "agent.id");
            if (File.Exists(legacy))
            {
                var id = File.ReadAllText(legacy).Trim();
                if (id.Length > 0) return id;
            }
        }
        catch { }
        return null;
    }

    private static void TrySave(string id)
    {
        try
        {
            var p = IdFilePath();
            if (p != null) File.WriteAllText(p, id);
        }
        catch { }
    }
}

// ایزوله‌سازی دسترسی WMI برای خواندن UUID — فقط روی ویندوز صدا زده می‌شود.
internal static class WindowsIdentityReader
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string? ReadSystemUuid()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"\\.\root\CIMV2", "SELECT UUID FROM Win32_ComputerSystemProduct");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject mo in results)
            {
                try
                {
                    var uuid = mo["UUID"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(uuid)) return uuid;
                }
                catch { }
                finally { try { mo.Dispose(); } catch { } }
            }
        }
        catch { }
        return null;
    }
}
