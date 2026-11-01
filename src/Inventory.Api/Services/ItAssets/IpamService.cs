using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

// =====================================================================
// مدیریت آی‌پی (IPAM)
//   IpamTools    → تبدیل آی‌پی/عدد و پارس CIDR
//   IpamRegistry → آی‌پی‌های ثبت‌شده در «شناسنامه‌ها» (منبع رنگ سبز)
//   IpamScanner  → اسکن شبکه (پینگ + TCP + ARP) در پس‌زمینه و اسکن خودکار زمان‌بندی‌شده
// =====================================================================

public static class IpamTools
{
    public const int MinPrefix = 22;   // حداکثر ۱۰۲۴ آدرس در یک رنج (برای نمای شبکه‌ای)
    public const int MaxPrefix = 30;

    private static readonly Regex Ipv4Rx = new(@"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])", RegexOptions.Compiled);

    public static bool TryParseIp(string? text, out long num)
    {
        num = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var p = text.Trim().Split('.');
        if (p.Length != 4) return false;
        foreach (var part in p)
        {
            if (part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit)) return false;
            var v = int.Parse(part);
            if (v > 255) return false;
            num = (num << 8) | (uint)v;
        }
        return true;
    }

    public static string ToIp(long num) =>
        $"{(num >> 24) & 255}.{(num >> 16) & 255}.{(num >> 8) & 255}.{num & 255}";

    /// <summary>همهٔ آی‌پی‌های IPv4 معتبرِ داخل یک متن (مثلاً «192.168.1.5, fe80::1» یا چند آی‌پی با کاما).</summary>
    public static IEnumerable<long> ExtractIps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (Match m in Ipv4Rx.Matches(text))
        {
            if (!TryParseIp(m.Value, out var n)) continue;
            var first = n >> 24;
            if (first is 0 or 127 || n >> 16 == (169 << 8 | 254)) continue; // 0.x / loopback / APIPA
            yield return n;
        }
    }

    /// <summary>پارس «192.168.30.0/24» یا «192.168.30.17/24» (به آدرس شبکه نرمال می‌شود).</summary>
    public static bool TryParseCidr(string? text, out long network, out int prefix, out string? error)
    {
        network = 0; prefix = 0; error = null;
        var t = (text ?? "").Trim().Replace(" ", "");
        var slash = t.IndexOf('/');
        if (slash < 0)
        {
            // «192.168.30» → /24
            var parts = t.Split('.');
            if (parts.Length == 3) { t += ".0/24"; slash = t.IndexOf('/'); }
            else { error = "رنج را به شکل 192.168.30.0/24 وارد کنید."; return false; }
        }
        if (!TryParseIp(t[..slash], out var ip)) { error = "آدرس شبکه نامعتبر است."; return false; }
        if (!int.TryParse(t[(slash + 1)..], out prefix) || prefix < MinPrefix || prefix > MaxPrefix)
        {
            error = $"طول پیشوند باید بین /{MinPrefix} (۱۰۲۴ آدرس) تا /{MaxPrefix} باشد.";
            return false;
        }
        var mask = MaskOf(prefix);
        network = ip & mask;
        return true;
    }

    public static long MaskOf(int prefix) => prefix == 0 ? 0 : (0xFFFFFFFFL << (32 - prefix)) & 0xFFFFFFFFL;
    public static long SizeOf(int prefix) => 1L << (32 - prefix);
    public static long Broadcast(long network, int prefix) => network + SizeOf(prefix) - 1;
    public static string Cidr(long network, int prefix) => $"{ToIp(network)}/{prefix}";

    /// <summary>آدرس‌های قابل استفاده (بدون Network و Broadcast).</summary>
    public static (long First, long Last) HostRange(long network, int prefix) =>
        (network + 1, Broadcast(network, prefix) - 1);

    public static bool Contains(long network, int prefix, long ip) => (ip & MaskOf(prefix)) == network;

    public static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 12 || hex == "000000000000" || hex == "FFFFFFFFFFFF") return null;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }
}

/// <summary>یک «صاحب» برای یک آی‌پی — رکوردی از شناسنامه‌ها که این آی‌پی را دارد.</summary>
public sealed class IpOwner
{
    public string Source { get; set; } = "";      // System | OfficeMachine | Camera | Nvr | Vault | Reserved
    public string SourceTitle { get; set; } = "";
    public int RefId { get; set; }
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public string? Link { get; set; }
    public string? Mac { get; set; }
}

public static class IpamRegistry
{
    /// <summary>
    /// نقشهٔ آی‌پی → صاحبان ثبت‌شده، برای یک شرکت.
    /// سیستم‌ها و گاوصندوق به شرکت فیلتر می‌شوند؛ ماشین اداری/دوربین/NVR فیلد شرکت ندارند و برای همه لحاظ می‌شوند.
    /// رزروهای دستی فقط از رنج‌های همان شرکت می‌آیند.
    /// </summary>
    public static async Task<Dictionary<long, List<IpOwner>>> BuildAsync(AppDbContext db, int companyId, CancellationToken ct = default)
    {
        var map = new Dictionary<long, List<IpOwner>>();
        void Add(long ip, IpOwner o)
        {
            if (!map.TryGetValue(ip, out var list)) map[ip] = list = new List<IpOwner>();
            if (!list.Any(x => x.Source == o.Source && x.RefId == o.RefId)) list.Add(o);
        }

        // ---- سیستم‌ها (شناسنامه سیستم — کارت‌های شبکهٔ گزارش‌شده توسط ایجنت یا ثبت دستی) ----
        try
        {
            var systems = await db.SystemInfos.AsNoTracking()
                .Where(s => s.CompanyId == companyId || s.CompanyId == null)
                .Select(s => new { s.Id, s.AgentId, s.UserId, s.IsApproved }).ToListAsync(ct);
            var sysIds = systems.Select(s => s.Id).ToList();
            var adapters = await db.SystemNetAdapters.AsNoTracking()
                .Where(a => sysIds.Contains(a.SystemInfoId))
                .Select(a => new { a.SystemInfoId, a.Name, a.Ipv4, a.MacAddress }).ToListAsync(ct);
            var userIds = systems.Where(s => s.UserId != null).Select(s => s.UserId!.Value).Distinct().ToList();
            var users = await db.SystemUsers.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).ToListAsync(ct);
            var userName = users.ToDictionary(u => u.Id, u =>
            {
                var n = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(n) ? u.Username : n;
            });
            var sysById = systems.ToDictionary(s => s.Id);
            foreach (var a in adapters)
            {
                var s = sysById[a.SystemInfoId];
                var owner = s.UserId != null && userName.TryGetValue(s.UserId.Value, out var un) ? un : null;
                var title = owner != null ? $"سیستم {owner}" : $"سیستم #{s.Id}";
                foreach (var ip in IpamTools.ExtractIps(a.Ipv4))
                    Add(ip, new IpOwner
                    {
                        Source = "System", SourceTitle = "شناسنامه سیستم", RefId = s.Id, Title = title,
                        Detail = string.Join(" — ", new[] { a.Name, s.AgentId, s.IsApproved ? null : "در انتظار تأیید" }
                            .Where(x => !string.IsNullOrWhiteSpace(x))),
                        Link = $"system-id/{s.Id}", Mac = IpamTools.NormalizeMac(a.MacAddress)
                    });
            }
        }
        catch { /* جدول‌های قطعات ممکن است در دیتابیس‌های قدیمی نباشند */ }

        // ---- ماشین‌های اداری (پرینتر/اسکنر شبکه‌ای) ----
        try
        {
            var machines = await db.OfficeMachines.AsNoTracking()
                .Where(m => m.IsActive && m.IpAddress != null && m.IpAddress != "")
                .Select(m => new { m.Id, m.Model, m.Location, m.SerialNumber, m.IpAddress }).ToListAsync(ct);
            foreach (var m in machines)
                foreach (var ip in IpamTools.ExtractIps(m.IpAddress))
                    Add(ip, new IpOwner
                    {
                        Source = "OfficeMachine", SourceTitle = "ماشین اداری", RefId = m.Id, Title = m.Model,
                        Detail = string.Join(" — ", new[] { m.Location, m.SerialNumber }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        Link = "office-machines"
                    });
        }
        catch { }

        // ---- دوربین‌ها و NVR ----
        try
        {
            var cams = await db.CctvCameras.AsNoTracking().Where(c => c.IsActive && c.Ip != null && c.Ip != "")
                .Select(c => new { c.Id, c.Model, c.Location, c.Ip, c.Mac }).ToListAsync(ct);
            foreach (var c in cams)
                foreach (var ip in IpamTools.ExtractIps(c.Ip))
                    Add(ip, new IpOwner
                    {
                        Source = "Camera", SourceTitle = "دوربین مداربسته", RefId = c.Id, Title = $"دوربین {c.Model}".Trim(),
                        Detail = c.Location, Link = "cctv-cameras", Mac = IpamTools.NormalizeMac(c.Mac)
                    });

            var nvrs = await db.CctvNvrs.AsNoTracking().Where(c => c.IsActive && c.Ip != null && c.Ip != "")
                .Select(c => new { c.Id, c.Model, c.Location, c.Ip, c.Mac }).ToListAsync(ct);
            foreach (var c in nvrs)
                foreach (var ip in IpamTools.ExtractIps(c.Ip))
                    Add(ip, new IpOwner
                    {
                        Source = "Nvr", SourceTitle = "دستگاه NVR", RefId = c.Id, Title = $"NVR {c.Model}".Trim(),
                        Detail = c.Location, Link = "cctv-nvrs", Mac = IpamTools.NormalizeMac(c.Mac)
                    });
        }
        catch { }

        // ---- گاوصندوق رمزها (سرور، سوییچ، میکروتیک، فایروال...) — فقط آدرس، بدون هیچ اطلاعات محرمانه ----
        try
        {
            var creds = await db.ItCredentials.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.IsActive && c.Host != null && c.Host != "")
                .Select(c => new { c.Id, c.Title, c.Category, c.Host, c.Location }).ToListAsync(ct);
            foreach (var c in creds)
                foreach (var ip in IpamTools.ExtractIps(c.Host))
                    Add(ip, new IpOwner
                    {
                        Source = "Vault", SourceTitle = "تجهیزات شبکه (گاوصندوق)", RefId = c.Id, Title = c.Title,
                        Detail = string.Join(" — ", new[] { CategoryFa(c.Category), c.Location }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        Link = "it-vault"
                    });
        }
        catch { }

        // ---- سرورهای مدیریت‌شده (آدرس مدیریتی iLO) ----
        // Credential ممکن است DNS داشته باشد؛ شناسنامهٔ سرور مرجع قطعی IP مدیریتی است.
        try
        {
            var servers = await db.ManagedServers.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.Status != "Retired" && s.IloAddress != "")
                .Select(s => new { s.Id, s.Name, s.Model, s.IloAddress, s.Rack, s.LastHealthStatus }).ToListAsync(ct);
            foreach (var s in servers)
                foreach (var ip in IpamTools.ExtractIps(s.IloAddress))
                    Add(ip, new IpOwner
                    {
                        Source = "ManagedServer", SourceTitle = "سرور مدیریت‌شده", RefId = s.Id, Title = s.Name,
                        Detail = string.Join(" — ", new[] { s.Model, s.Rack, s.LastHealthStatus }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        Link = "servers"
                    });
        }
        catch { /* امکان دارد جدول ماژول سرورها هنوز در دیتابیس قدیمی ساخته نشده باشد */ }

        // ---- رزرو دستی داخل همین بخش ----
        try
        {
            var subnetIds = await db.IpSubnets.AsNoTracking().Where(s => s.CompanyId == companyId).Select(s => s.Id).ToListAsync(ct);
            var res = await db.IpAddressEntries.AsNoTracking()
                .Where(e => subnetIds.Contains(e.SubnetId) && e.IsReserved)
                .Select(e => new { e.Id, e.IpNum, e.ReservedTitle, e.ReservedType, e.ReservedOwner }).ToListAsync(ct);
            foreach (var r in res)
                Add(r.IpNum, new IpOwner
                {
                    Source = "Reserved", SourceTitle = "رزرو دستی (مدیریت آی‌پی)", RefId = r.Id,
                    Title = string.IsNullOrWhiteSpace(r.ReservedTitle) ? "رزرو شده" : r.ReservedTitle!,
                    Detail = string.Join(" — ", new[] { TypeFa(r.ReservedType), r.ReservedOwner }.Where(x => !string.IsNullOrWhiteSpace(x)))
                });
        }
        catch { }

        return map;
    }

    public static string? CategoryFa(string? c) => c switch
    {
        "Server" => "سرور", "Switch" => "سوییچ", "Mikrotik" => "میکروتیک", "Firewall" => "فایروال",
        "System" => "سامانه", "Other" => "سایر", _ => c
    };

    public static string? TypeFa(string? t) => t switch
    {
        "Server" => "سرور", "Switch" => "سوییچ", "Router" => "روتر/میکروتیک", "Firewall" => "فایروال",
        "AccessPoint" => "اکسس‌پوینت", "Printer" => "پرینتر", "Camera" => "دوربین", "Pc" => "کامپیوتر",
        "Phone" => "تلفن VoIP", "Other" => "سایر", _ => t
    };
}

/// <summary>وضعیت زندهٔ یک اسکن.</summary>
public sealed class IpamScanProgress
{
    public int SubnetId { get; set; }
    public bool Running { get; set; }
    public int Progress { get; set; }
    public int Total { get; set; }
    public int Scanned;
    public int Found;
    public string Phase { get; set; } = "";
    public string? StartedBy { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// اسکنر رنج‌ها — Singleton + BackgroundService.
/// تشخیص «اشغال»: پینگ ← اگر جواب نداد اتصال TCP به پورت‌های رایج (حتی «Connection refused» یعنی دستگاه روشن است)
/// ← و در پایان جدول ARP سرور (دستگاه‌هایی که پینگ را بسته‌اند ولی در همان شبکه به ARP جواب داده‌اند).
/// </summary>
public sealed class IpamScanner : BackgroundService
{
    private static readonly int[] TcpPorts = { 80, 443, 22, 445, 3389, 8291, 554, 8080, 23, 139, 135, 161, 9100 };

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IpamScanner> _log;
    private readonly ConcurrentDictionary<int, IpamScanProgress> _jobs = new();
    private readonly SemaphoreSlim _parallelScans = new(2); // حداکثر ۲ رنج هم‌زمان

    public IpamScanner(IServiceScopeFactory scopes, ILogger<IpamScanner> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public IpamScanProgress? GetProgress(int subnetId) => _jobs.TryGetValue(subnetId, out var p) ? p : null;
    public bool IsRunning(int subnetId) => _jobs.TryGetValue(subnetId, out var p) && p.Running;

    /// <summary>شروع اسکن یک رنج در پس‌زمینه. اگر همین الان در حال اسکن باشد false برمی‌گرداند.</summary>
    public bool TryStart(int subnetId, string? by)
    {
        var p = new IpamScanProgress { SubnetId = subnetId, Running = true, StartedBy = by, Phase = "در صف" };
        var added = false;
        _jobs.AddOrUpdate(subnetId, _ => { added = true; return p; }, (_, old) =>
        {
            if (old.Running) return old;
            added = true;
            return p;
        });
        if (!added) return false;
        _ = Task.Run(() => RunAsync(subnetId, p));
        return true;
    }

    private async Task RunAsync(int subnetId, IpamScanProgress p)
    {
        await _parallelScans.WaitAsync();
        var sw = Stopwatch.StartNew();
        try
        {
            long network; int prefix;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var s = await db.IpSubnets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subnetId);
                if (s is null) { p.Error = "رنج پیدا نشد."; return; }
                network = s.NetworkNum; prefix = s.Prefix;
            }

            var (first, last) = IpamTools.HostRange(network, prefix);
            var targets = new List<long>();
            for (var n = first; n <= last; n++) targets.Add(n);
            p.Total = targets.Count;

            // ---- فاز ۱: پینگ ----
            p.Phase = "پینگ";
            var found = new ConcurrentDictionary<long, string>();
            using var sem = new SemaphoreSlim(64);
            await Task.WhenAll(targets.Select(async n =>
            {
                await sem.WaitAsync();
                try { if (await PingAsync(IpamTools.ToIp(n), 600)) found.TryAdd(n, "ping"); }
                finally
                {
                    var c = Interlocked.Increment(ref p.Scanned);
                    p.Found = found.Count;
                    p.Progress = (int)(c * 50.0 / Math.Max(1, targets.Count));
                    sem.Release();
                }
            }));

            // ---- فاز ۲: TCP برای آن‌هایی که پینگ را جواب ندادند ----
            p.Phase = "بررسی پورت‌ها";
            var rest = targets.Where(t => !found.ContainsKey(t)).ToList();
            var done = 0;
            await Task.WhenAll(rest.Select(async n =>
            {
                await sem.WaitAsync();
                try { if (await TcpAliveAsync(IpamTools.ToIp(n), 300)) found.TryAdd(n, "tcp"); }
                finally
                {
                    var c = Interlocked.Increment(ref done);
                    p.Found = found.Count;
                    p.Progress = 50 + (int)(c * 40.0 / Math.Max(1, rest.Count));
                    sem.Release();
                }
            }));

            // ---- فاز ۳: ARP + نام‌ها ----
            p.Phase = "خواندن ARP و نام‌ها";
            p.Progress = 92;
            var arp = ReadArpTable();
            foreach (var kv in arp)
                if (IpamTools.TryParseIp(kv.Key, out var an) && an >= first && an <= last)
                    found.TryAdd(an, "arp");
            p.Found = found.Count;

            var names = new ConcurrentDictionary<long, string>();
            await Task.WhenAll(found.Keys.Select(async n =>
            {
                await sem.WaitAsync();
                try
                {
                    var h = await DnsAsync(IpamTools.ToIp(n), 900);
                    if (!string.IsNullOrWhiteSpace(h) && h != IpamTools.ToIp(n)) names[n] = h;
                }
                finally { sem.Release(); }
            }));

            // ---- ذخیره ----
            p.Phase = "ذخیره";
            p.Progress = 97;
            await SaveAsync(subnetId, first, last, found, arp, names, (int)sw.ElapsedMilliseconds);
            p.Progress = 100;
            p.Phase = "تمام شد";
        }
        catch (Exception ex)
        {
            p.Error = ex.Message;
            _log.LogWarning(ex, "IPAM scan failed for subnet {SubnetId}", subnetId);
        }
        finally
        {
            p.Running = false;
            p.FinishedAt = DateTime.Now;
            _parallelScans.Release();
        }
    }

    private async Task SaveAsync(int subnetId, long first, long last, IDictionary<long, string> found,
        Dictionary<string, string> arp, IDictionary<long, string> names, int durationMs)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.Now;
        var existing = await db.IpAddressEntries.Where(e => e.SubnetId == subnetId).ToDictionaryAsync(e => e.IpNum);

        foreach (var e in existing.Values)
            if (e.IpNum >= first && e.IpNum <= last && !found.ContainsKey(e.IpNum))
            {
                e.IsAlive = false;
                e.LastCheckedAt = now;
            }

        foreach (var (n, method) in found)
        {
            var ip = IpamTools.ToIp(n);
            if (!existing.TryGetValue(n, out var e))
            {
                e = new IpAddressEntry { SubnetId = subnetId, Ip = ip, IpNum = n, FirstSeenAt = now };
                db.IpAddressEntries.Add(e);
            }
            e.IsAlive = true;
            e.LastSeenAt = now;
            e.FirstSeenAt ??= now;
            e.LastCheckedAt = now;
            e.Method = method;
            if (arp.TryGetValue(ip, out var mac) && IpamTools.NormalizeMac(mac) is { } m) e.Mac = m;
            if (names.TryGetValue(n, out var h)) e.Hostname = h.Length > 200 ? h[..200] : h;
        }

        var s = await db.IpSubnets.FirstOrDefaultAsync(x => x.Id == subnetId);
        if (s != null)
        {
            s.LastScanAt = now;
            s.LastScanAliveCount = found.Count;
            s.LastScanDurationMs = durationMs;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>بررسی فوری یک آی‌پی (دکمهٔ «پینگ همین آی‌پی»).</summary>
    public async Task<(bool Alive, string? Method, string? Mac, string? Hostname)> ProbeOneAsync(string ip)
    {
        string? method = null;
        if (await PingAsync(ip, 1000)) method = "ping";
        else if (await TcpAliveAsync(ip, 600)) method = "tcp";
        var arp = ReadArpTable();
        arp.TryGetValue(ip, out var mac);
        if (method == null && IpamTools.NormalizeMac(mac) != null) method = "arp";
        var host = method != null ? await DnsAsync(ip, 1200) : null;
        return (method != null, method, IpamTools.NormalizeMac(mac), string.IsNullOrWhiteSpace(host) || host == ip ? null : host);
    }

    // ================================================================== اسکن خودکار

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.Now;
                var subnets = await db.IpSubnets.AsNoTracking()
                    .Where(s => s.AutoScanMinutes > 0)
                    .Select(s => new { s.Id, s.AutoScanMinutes, s.LastScanAt }).ToListAsync(stoppingToken);
                foreach (var s in subnets)
                    if (s.LastScanAt == null || s.LastScanAt.Value.AddMinutes(s.AutoScanMinutes) <= now)
                        TryStart(s.Id, "اسکن خودکار");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "IPAM auto-scan tick failed (tables may not exist yet)"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch { break; }
        }
    }

    // ================================================================== ابزارهای شبکه

    private static async Task<bool> PingAsync(string ip, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var r = await ping.SendPingAsync(ip, timeoutMs);
            return r.Status == IPStatus.Success;
        }
        catch { return false; }
    }

    /// <summary>اگر به یکی از پورت‌ها وصل شد یا «Connection refused» گرفت، دستگاه روشن است.</summary>
    private static async Task<bool> TcpAliveAsync(string ip, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var tasks = TcpPorts.Select(async port =>
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(ip, port, cts.Token);
                return true;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionRefused) { return true; }
            catch { return false; }
        }).ToList();
        while (tasks.Count > 0)
        {
            var t = await Task.WhenAny(tasks);
            tasks.Remove(t);
            if (await t) { cts.Cancel(); return true; }
        }
        return false;
    }

    private static async Task<string?> DnsAsync(string ip, int timeoutMs)
    {
        try
        {
            var task = Dns.GetHostEntryAsync(IPAddress.Parse(ip));
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (done == task && task.IsCompletedSuccessfully) return task.Result.HostName;
        }
        catch { }
        return null;
    }

    /// <summary>جدول ARP سرور: ip → MAC (ویندوز: arp -a — لینوکس: ip neigh).</summary>
    public static Dictionary<string, string> ReadArpTable()
    {
        var map = new Dictionary<string, string>();
        static string Run(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return output;
            }
            catch { return ""; }
        }

        var arp = Run("arp", "-a");
        foreach (Match m in Regex.Matches(arp, @"(\d+\.\d+\.\d+\.\d+)\)?\s+(?:at\s+)?([0-9a-fA-F]{1,2}[:-][0-9a-fA-F]{1,2}[:-][0-9a-fA-F]{1,2}[:-][0-9a-fA-F]{1,2}[:-][0-9a-fA-F]{1,2}[:-][0-9a-fA-F]{1,2})"))
            if (IpamTools.NormalizeMac(m.Groups[2].Value) is { } mac) map[m.Groups[1].Value] = mac;

        if (map.Count == 0)
        {
            var neigh = Run("ip", "neigh");
            foreach (Match m in Regex.Matches(neigh, @"(\d+\.\d+\.\d+\.\d+).*?lladdr\s+([0-9a-fA-F:]{17})(?!.*FAILED)"))
                if (IpamTools.NormalizeMac(m.Groups[2].Value) is { } mac) map[m.Groups[1].Value] = mac;
        }
        return map;
    }
}
