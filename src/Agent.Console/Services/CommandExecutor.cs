using System.Diagnostics;

namespace InventoryAgent.Services;

/// <summary>اجرای دستورهای از راه دور سرور: Reboot | Shutdown | Lock</summary>
public static class CommandExecutor
{
    public static (bool ok, string message) Execute(string action)
    {
        var a = (action ?? "").Trim().ToLowerInvariant();
        return a switch
        {
            "reboot" or "restart" => RunReboot(),
            "shutdown" or "poweroff" => RunShutdown(),
            "lock" or "lockworkstation" => RunLock(),
            _ => (false, $"دستور ناشناخته: {action}")
        };
    }

    private static (bool ok, string message) RunReboot()
    {
        if (OperatingSystem.IsWindows())
            return Shell("shutdown.exe", "/r /t 10 /c \"Inventory agent remote reboot\"", "ری‌استارت در ۱۰ ثانیه...");
        // لینوکس
        if (ShellOk("systemctl", "reboot")) return (true, "ری‌استارت صادر شد (systemctl).");
        if (ShellOk("shutdown", "-r now")) return (true, "ری‌استارت صادر شد (shutdown).");
        if (ShellOk("/sbin/shutdown", "-r now")) return (true, "ری‌استارت صادر شد.");
        return (false, "اجرای reboot ناموفق بود (دسترسی root لازم است).");
    }

    private static (bool ok, string message) RunShutdown()
    {
        if (OperatingSystem.IsWindows())
            return Shell("shutdown.exe", "/s /t 10 /c \"Inventory agent remote shutdown\"", "خاموش شدن در ۱۰ ثانیه...");
        if (ShellOk("systemctl", "poweroff")) return (true, "خاموشی صادر شد (systemctl).");
        if (ShellOk("shutdown", "-h now")) return (true, "خاموشی صادر شد (shutdown).");
        if (ShellOk("/sbin/shutdown", "-h now")) return (true, "خاموشی صادر شد.");
        return (false, "اجرای shutdown ناموفق بود (دسترسی root لازم است).");
    }

    private static (bool ok, string message) RunLock()
    {
        if (OperatingSystem.IsWindows())
            return Shell("rundll32.exe", "user32.dll,LockWorkStation", "سیستم قفل شد.");
        // لینوکس: چند روش رایج قفل صفحه
        if (ShellOk("loginctl", "lock-session")) return (true, "صفحه قفل شد (loginctl).");
        if (ShellOk("gnome-screensaver-command", "-l")) return (true, "صفحه قفل شد (gnome).");
        if (ShellOk("xdg-screensaver", "lock")) return (true, "صفحه قفل شد.");
        if (ShellOk("dm-tool", "lock")) return (true, "صفحه قفل شد (dm-tool).");
        return (false, "ابزار قفل صفحه پیدا نشد.");
    }

    private static (bool ok, string message) Shell(string file, string args, string okMessage)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return (true, okMessage);
        }
        catch (Exception ex)
        {
            return (false, $"اجرا ناموفق: {ex.Message}");
        }
    }

    private static bool ShellOk(string file, string args)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!p.Start()) return false;
            if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
