using System.Text;
using System.Text.Json;
using InventoryAgent.Collectors;
using InventoryAgent.Models;
using InventoryAgent.Services;

namespace InventoryAgent;

// ============================================================================
// ایجنت Inventory — جمع‌آوری مشخصات سخت‌افزار و ارسال به API مدیریت سخت‌افزار
//
//   InventoryAgent run                     اجرای یک‌باره: ارسال گزارش + اجرای دستورهای در انتظار
//   InventoryAgent watch                   حالت نگهبان: تکرار دوره‌ای (پیش‌فرض هر ۶۰ دقیقه)
//   InventoryAgent dry-run                 فقط جمع‌آوری و نمایش JSON (بدون ارسال به سرور)
//   InventoryAgent commands                فقط دریافت و اجرای دستورهای در انتظار
//   InventoryAgent printers                فقط ارسال وضعیت چاپگرهای محلی (سبک — مناسب زمان‌بندی هر ۵ دقیقه)
//   InventoryAgent init                    ساخت فایل نمونه‌ی تنظیمات agent.json
//
// سوییچ‌ها (روی همه‌ی دستورها):
//   -s, --server URL     آدرس API (مثل http://192.168.1.10:5000)
//   -i, --interval MIN   فاصله‌ی watch به دقیقه
//       --agent-id ID    شناسه‌ی ثابت سیستم (اختیاری — خودکار ساخته می‌شود)
//       --token T        توکن JWT (اختیاری)
//       --key K          کلید مشترک ایجنت (X-Agent-Key) — اگر سرور تنظیم کرده باشد
//       --username U --password P   ورود و گرفتن توکن (اختیاری)
//       --config PATH    مسیر فایل تنظیمات (پیش‌فرض: agent.json کنار exe)
//       --out FILE       خروجی JSON در dry-run
//       --insecure       نادیده گرفتن خطای گواهی HTTPS (فقط شبکه‌ی داخلی)
// ============================================================================

internal static class Program
{
    private static string AgentVersion
        => typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public static async Task<int> Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        var opts = ParseArgs(args);
        if (opts.ShowHelp) { PrintHelp(); return 0; }
        if (!string.IsNullOrEmpty(opts.UnknownPositional))
            return UsageError($"دستور ناشناخته: {opts.UnknownPositional}");

        try
        {
            return opts.Command switch
            {
                "watch" => await WatchAsync(opts),
                "dry-run" => DryRun(opts),
                "commands" => await CommandsOnlyAsync(opts),
                "printers" => await PrintersOnlyAsync(opts),
                "init" => InitConfig(opts),
                "version" => ShowVersion(),
                "help" => ShowHelp(),
                "run" => await RunOnceAsync(opts, verbose: true),
                _ => UsageError($"دستور ناشناخته: {opts.Command}")
            };
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.WriteLine($"[خطا] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[خطای غیرمنتظره] {ex.Message}");
            return 1;
        }
    }

    // ================= دستور run: یک چرخه‌ی کامل =================

    private static async Task<int> RunOnceAsync(CliOptions opts, bool verbose, CancellationToken ct = default)
    {
        var config = LoadConfig(opts);
        IHardwareCollector collector;
        try
        {
            collector = CollectorFactory.Create();
        }
        catch (PlatformNotSupportedException)
        {
            Console.WriteLine("[خطا] سیستم‌عامل پشتیبانی نمی‌شود (فقط Windows و Linux).");
            return 1;
        }

        if (verbose) Console.WriteLine("در حال جمع‌آوری مشخصات سخت‌افزار...");
        var details = collector.Collect();
        details.AgentVersion = AgentVersion;
        details.MachineName = Environment.MachineName;
        details.CollectedAt = DateTime.UtcNow.ToString("o");

        var agentId = AgentIdentity.Resolve(string.IsNullOrWhiteSpace(opts.AgentId) ? config.AgentId : opts.AgentId);
        var report = ReportBuilder.Build(agentId, collector.OsName, details);

        if (verbose)
        {
            Console.WriteLine($"AgentId : {agentId}");
            Console.WriteLine($"OS      : {collector.OsName}");
            Console.WriteLine($"قطعات   : {details.Cpus.Count} CPU، {details.RamSticks.Count} رم ({report.TotalRamGb}GB)" +
                              $"، {details.Disks.Count} هارد، {details.Gpus.Count} گرافیک، {details.Monitors.Count} مانیتور");
            Console.WriteLine($"سرور    : {config.Server}");
        }

        using var api = new ApiClient(config.Server, config.Insecure, agentKey: config.Key);
        await EnsureAuthAsync(api, config, ct);

        var (ok, msg) = await api.SendReportAsync(report, ct);
        Console.WriteLine(ok ? $"[ارسال] {msg}" : $"[خطای ارسال] {msg}");
        if (!ok) return 1;

        if (config.MonitorPrinters) await SendPrintersAsync(api, config, agentId, collector.OsName, verbose, ct);
        await PollAndExecuteAsync(api, agentId, ct);
        return 0;
    }

    // ================= چاپگرها =================

    private static AgentPrinterReport BuildPrinterReport(AgentConfig config, string agentId, string osName)
    {
        var (printers, logEnabled) = PrinterCollector.Collect(config.EnablePrintLog);
        return new AgentPrinterReport
        {
            AgentId = agentId, MachineName = Environment.MachineName, AgentVersion = AgentVersion, OsName = osName,
            CollectedAt = DateTime.UtcNow.ToString("o"), PageLogEnabled = logEnabled, Printers = printers
        };
    }

    private static async Task<bool> SendPrintersAsync(ApiClient api, AgentConfig config, string agentId, string osName, bool verbose, CancellationToken ct)
    {
        var report = BuildPrinterReport(config, agentId, osName);
        if (verbose)
        {
            var real = report.Printers.Where(p => p.Kind is not ("Virtual" or "Connection")).ToList();
            Console.WriteLine($"[چاپگر] {real.Count} چاپگر فیزیکی ({report.Printers.Count} صف) — " +
                              string.Join("، ", real.Select(p => $"{p.Name} [{p.Kind}/{p.Status}]")));
        }
        var (ok, msg) = await api.SendPrinterReportAsync(report, ct);
        Console.WriteLine(ok ? "[چاپگر] وضعیت چاپگرها ارسال شد." : $"[چاپگر] ارسال ناموفق: {msg}");
        return ok;
    }

    private static async Task<int> PrintersOnlyAsync(CliOptions opts)
    {
        var config = LoadConfig(opts);
        var agentId = AgentIdentity.Resolve(string.IsNullOrWhiteSpace(opts.AgentId) ? config.AgentId : opts.AgentId);
        var osName = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? CollectorFactory.Create().OsName : Environment.OSVersion.ToString();
        using var api = new ApiClient(config.Server, config.Insecure, agentKey: config.Key);
        await EnsureAuthAsync(api, config);
        return await SendPrintersAsync(api, config, agentId, osName, verbose: true, CancellationToken.None) ? 0 : 1;
    }

    // ================= دستور watch: حلقه‌ی دوره‌ای =================

    private static async Task<int> WatchAsync(CliOptions opts)
    {
        var config = LoadConfig(opts);
        var printerEvery = config.MonitorPrinters ? Math.Min(config.PrinterIntervalMinutes, config.IntervalMinutes) : config.IntervalMinutes;
        Console.WriteLine($"حالت نگهبان — گزارش سخت‌افزار هر {config.IntervalMinutes} دقیقه" +
                          (config.MonitorPrinters ? $"، وضعیت چاپگرها هر {printerEvery} دقیقه" : "") + " (توقف: Ctrl+C)");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var lastFull = DateTime.MinValue;
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                if (DateTime.Now - lastFull >= TimeSpan.FromMinutes(config.IntervalMinutes))
                {
                    lastFull = DateTime.Now;
                    await RunOnceAsync(opts, verbose: true, cts.Token);   // شامل ارسال چاپگرها هم هست
                }
                else if (config.MonitorPrinters)
                {
                    var agentId = AgentIdentity.Resolve(string.IsNullOrWhiteSpace(opts.AgentId) ? config.AgentId : opts.AgentId);
                    using var api = new ApiClient(config.Server, config.Insecure, agentKey: config.Key);
                    await EnsureAuthAsync(api, config, cts.Token);
                    await SendPrintersAsync(api, config, agentId, CollectorFactory.Create().OsName, verbose: false, cts.Token);
                }
                Console.WriteLine($"--- {DateTime.Now:HH:mm:ss} ---");
                await Task.Delay(TimeSpan.FromMinutes(printerEvery), cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[خطا] {ex.Message} — تلاش مجدد در دوره‌ی بعد.");
                try { await Task.Delay(TimeSpan.FromMinutes(printerEvery), cts.Token); } catch (OperationCanceledException) { break; }
            }
        }
        Console.WriteLine("ایجنت متوقف شد.");
        return 0;
    }

    // ================= دستور commands: فقط دستورهای در انتظار =================

    private static async Task<int> CommandsOnlyAsync(CliOptions opts)
    {
        var config = LoadConfig(opts);
        var agentId = AgentIdentity.Resolve(string.IsNullOrWhiteSpace(opts.AgentId) ? config.AgentId : opts.AgentId);
        using var api = new ApiClient(config.Server, config.Insecure, agentKey: config.Key);
        await EnsureAuthAsync(api, config);
        Console.WriteLine($"AgentId: {agentId}");
        await PollAndExecuteAsync(api, agentId);
        return 0;
    }

    private static async Task PollAndExecuteAsync(ApiClient api, string agentId, CancellationToken ct = default)
    {
        var cmds = await api.GetPendingCommandsAsync(agentId, ct);
        if (cmds.Count == 0)
        {
            Console.WriteLine("[دستور] دستور در انتظاری نیست.");
            return;
        }
        foreach (var cmd in cmds)
        {
            Console.WriteLine($"[دستور] اجرای {cmd.Action} (id={cmd.Id})...");
            var (ok, msg) = CommandExecutor.Execute(cmd.Action);
            Console.WriteLine(ok ? $"[دستور] {msg}" : $"[خطای دستور] {msg}");
            await api.AckCommandAsync(cmd.Id, ok, msg, ct);
        }
    }

    private static async Task EnsureAuthAsync(ApiClient api, AgentConfig config, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(config.Token))
        {
            api.UseToken(config.Token);
            return;
        }
        if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
        {
            var (ok, msg) = await api.LoginAsync(config.Username, config.Password, ct);
            Console.WriteLine(ok ? "[احراز] ورود موفق." : $"[احراز] {msg} — ادامه بدون توکن.");
        }
    }

    // ================= دستور dry-run =================

    private static int DryRun(CliOptions opts)
    {
        IHardwareCollector collector;
        try { collector = CollectorFactory.Create(); }
        catch (PlatformNotSupportedException)
        {
            Console.WriteLine("[خطا] سیستم‌عامل پشتیبانی نمی‌شود (فقط Windows و Linux).");
            return 1;
        }

        var details = collector.Collect();
        details.AgentVersion = AgentVersion;
        details.MachineName = Environment.MachineName;
        details.CollectedAt = DateTime.UtcNow.ToString("o");

        var agentId = AgentIdentity.Resolve(opts.AgentId);
        var report = ReportBuilder.Build(agentId, collector.OsName, details);

        // نمایش خلاصه
        Console.WriteLine($"AgentId : {agentId}");
        Console.WriteLine($"OS      : {collector.OsName}");
        Console.WriteLine($"Board   : {details.Board}  |  Model: {details.ComputerModel}");
        Console.WriteLine($"CPUs    : {details.Cpus.Count} | RAM modules: {details.RamSticks.Count} ({report.TotalRamGb}GB) | " +
                          $"Disks: {details.Disks.Count} | GPUs: {details.Gpus.Count} | Monitors: {details.Monitors.Count} | " +
                          $"NICs: {details.NetAdapters.Count} | Volumes: {details.Volumes.Count}");
        Console.WriteLine(new string('-', 60));

        // همان JSONای که به سرور ارسال می‌شود (DetailsJson به‌صورت آبجکت باز برای خوانایی)
        using var detailsDoc = JsonDocument.Parse(report.DetailsJson ?? "{}");
        var preview = new
        {
            report.AgentId,
            report.Motherboard,
            report.Cpu,
            report.Ram,
            report.HardDisk,
            report.Graphics,
            report.Monitor,
            report.OsName,
            report.TotalRamGb,
            details = detailsDoc.RootElement
        };
        var json = JsonSerializer.Serialize(preview, AgentJson.PrettyOptions);
        Console.WriteLine(json);

        // چاپگرها (گزارش جداگانه)
        var dryConfig = LoadConfig(opts);
        if (dryConfig.MonitorPrinters)
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine("چاپگرها (POST api/OfficeMachines/agent/printers):");
            var pr = BuildPrinterReport(dryConfig, agentId, collector.OsName);
            Console.WriteLine(JsonSerializer.Serialize(pr, AgentJson.PrettyOptions));
        }

        if (!string.IsNullOrWhiteSpace(opts.OutFile))
        {
            File.WriteAllText(opts.OutFile, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"ذخیره شد در: {opts.OutFile}");
        }
        return 0;
    }

    // ================= دستور init / version / help =================

    private static int InitConfig(CliOptions opts)
    {
        var path = string.IsNullOrWhiteSpace(opts.ConfigPath) ? AgentConfig.DefaultPath : opts.ConfigPath;
        if (File.Exists(path) && !opts.Force)
        {
            Console.WriteLine($"فایل {path} از قبل وجود دارد. برای بازنویسی از --force استفاده کنید.");
            return 2;
        }
        var cfg = new AgentConfig();
        if (!string.IsNullOrWhiteSpace(opts.Server)) cfg.Server = opts.Server;
        if (opts.IntervalMinutes is > 0) cfg.IntervalMinutes = opts.IntervalMinutes.Value;
        if (!string.IsNullOrWhiteSpace(opts.AgentId)) cfg.AgentId = opts.AgentId;
        cfg.Normalize();
        cfg.Save(path);
        Console.WriteLine($"فایل تنظیمات ساخته شد: {path}");
        Console.WriteLine("آدرس سرور را در آن تنظیم کنید و سپس ایجنت را اجرا کنید.");
        return 0;
    }

    private static int ShowVersion()
    {
        Console.WriteLine($"InventoryAgent {AgentVersion} — {Environment.OSVersion}");
        return 0;
    }

    private static int ShowHelp() { PrintHelp(); return 0; }

    private static int UsageError(string msg)
    {
        Console.WriteLine($"[خطا] {msg}");
        Console.WriteLine("راهنما: InventoryAgent help");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            InventoryAgent {AgentVersion} — ایجنت جمع‌آوری سخت‌افزار

            دستورها:
              run            اجرای یک‌باره (پیش‌فرض): ارسال گزارش + اجرای دستورهای در انتظار
              watch          حالت نگهبان: تکرار دوره‌ای (Ctrl+C برای توقف)
              dry-run        فقط نمایش JSON جمع‌آوری‌شده، بدون ارسال
              commands       فقط دریافت و اجرای دستورهای در انتظار
              printers       فقط ارسال وضعیت چاپگرهای محلی (USB) به ماژول ماشین‌های اداری
              init           ساخت فایل تنظیمات agent.json
              version        نمایش نسخه

            سوییچ‌ها:
              -s, --server URL     آدرس API (پیش‌فرض http://localhost:5000)
              -i, --interval MIN   فاصله‌ی watch به دقیقه (پیش‌فرض ۶۰)
                  --agent-id ID    شناسه‌ی ثابت سیستم (اختیاری)
                  --token T        توکن JWT (اختیاری)
                  --key K          کلید مشترک ایجنت (اگر سرور Agent:Key داشته باشد)
                  --username U --password P
                  --config PATH    مسیر فایل تنظیمات
                  --out FILE       ذخیره‌ی خروجی dry-run
                  --insecure       نادیده گرفتن خطای گواهی HTTPS
                  --force          بازنویسی فایل در init

            مثال‌ها:
              InventoryAgent run -s http://192.168.1.10:5000
              InventoryAgent watch -s http://192.168.1.10:5000 -i 30
              InventoryAgent dry-run --out hw.json
            """);
    }

    // ================= پیکربندی و آرگومان‌ها =================

    private static AgentConfig LoadConfig(CliOptions opts)
    {
        var config = AgentConfig.Load(opts.ConfigPath);
        if (!string.IsNullOrWhiteSpace(opts.Server)) config.Server = opts.Server;
        if (opts.IntervalMinutes is > 0) config.IntervalMinutes = opts.IntervalMinutes.Value;
        if (!string.IsNullOrWhiteSpace(opts.AgentId)) config.AgentId = opts.AgentId;
        if (!string.IsNullOrWhiteSpace(opts.Token)) config.Token = opts.Token;
        if (!string.IsNullOrWhiteSpace(opts.Key)) config.Key = opts.Key;
        if (!string.IsNullOrWhiteSpace(opts.Username)) config.Username = opts.Username;
        if (!string.IsNullOrWhiteSpace(opts.Password)) config.Password = opts.Password;
        if (opts.Insecure) config.Insecure = true;
        config.Normalize();
        return config;
    }

    private sealed class CliOptions
    {
        public string Command { get; set; } = "run";
        public string? UnknownPositional { get; set; }
        public bool ShowHelp { get; set; }
        public bool Force { get; set; }
        public bool Insecure { get; set; }
        public string? Server { get; set; }
        public int? IntervalMinutes { get; set; }
        public string? AgentId { get; set; }
        public string? Token { get; set; }
        public string? Key { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? ConfigPath { get; set; }
        public string? OutFile { get; set; }
    }

    private static CliOptions ParseArgs(string[] args)
    {
        var o = new CliOptions();
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "run", "watch", "dry-run", "dryrun", "commands", "printers", "init", "version", "help" };

        string? Next(ref int i)
        {
            if (i + 1 >= args.Length) return null;
            i++;
            return args[i];
        }

        var commandSeen = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith('-'))
            {
                // اولین توکن غیرسوییچی = نام دستور؛ بقیه‌ی موارد اضافه خطای کاربردی است
                if (o.Command == "run" && o.UnknownPositional == null && !commandSeen)
                {
                    commandSeen = true;
                    if (commands.Contains(a))
                        o.Command = a.ToLowerInvariant() == "dryrun" ? "dry-run" : a.ToLowerInvariant();
                    else
                        o.UnknownPositional = a;
                }
                else if (o.UnknownPositional == null)
                {
                    o.UnknownPositional = a;
                }
                continue;
            }
            switch (a.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "-?":
                    o.ShowHelp = true;
                    break;
                case "-s":
                case "--server":
                    o.Server = Next(ref i);
                    break;
                case "-i":
                case "--interval":
                    if (int.TryParse(Next(ref i), out var m)) o.IntervalMinutes = m;
                    break;
                case "--agent-id":
                    o.AgentId = Next(ref i);
                    break;
                case "--token":
                    o.Token = Next(ref i);
                    break;
                case "--key":
                    o.Key = Next(ref i);
                    break;
                case "--username":
                    o.Username = Next(ref i);
                    break;
                case "--password":
                    o.Password = Next(ref i);
                    break;
                case "--config":
                    o.ConfigPath = Next(ref i);
                    break;
                case "--out":
                    o.OutFile = Next(ref i);
                    break;
                case "--insecure":
                    o.Insecure = true;
                    break;
                case "--force":
                    o.Force = true;
                    break;
                default:
                    if (a.StartsWith("--server=")) o.Server = a["--server=".Length..];
                    else if (a.StartsWith("--agent-id=")) o.AgentId = a["--agent-id=".Length..];
                    else if (a.StartsWith("--token=")) o.Token = a["--token=".Length..];
                    else if (a.StartsWith("--key=")) o.Key = a["--key=".Length..];
                    else if (a.StartsWith("--config=")) o.ConfigPath = a["--config=".Length..];
                    else if (a.StartsWith("--out=")) o.OutFile = a["--out=".Length..];
                    break;
            }
        }
        return o;
    }
}
