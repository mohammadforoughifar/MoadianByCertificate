using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;

namespace Inventory.Api.Hubs;

public class HardwareDeviceStatus
{
    public string Kind { get; set; } = "";       // system | camera | nvr
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Extra { get; set; } = "";      // محل استقرار / وضعیت تایید
    public bool? Online { get; set; }            // null = بدون IP قابل بررسی
    public int? Health { get; set; }             // امتیاز سلامت 0-100 (فقط سیستم‌ها)
}

public class HardwareStats
{
    public bool Sweeping { get; set; }
    public DateTime LastSweepAt { get; set; }
    public int SystemsTotal { get; set; }
    public int SystemsApproved { get; set; }
    public int SystemsPending { get; set; }
    public int? SystemsAvgHealth { get; set; }   // میانگین امتیاز سلامت سیستم‌ها (0-100)
    public int CamerasTotal { get; set; }
    public int CamerasActiveFlag { get; set; }
    public int NvrsTotal { get; set; }
    public int NvrsActiveFlag { get; set; }
    public List<HardwareDeviceStatus> Devices { get; set; } = new();
}

/// <summary>
/// پایش زنده‌ی سخت‌افزار: هر ۳۰ ثانیه (یا درخواست فوری) همه‌ی IP های ثبت‌شده
/// (کامپیوترها، دوربین‌ها، NVRها) پینگ/TCP می‌شوند و نتیجه با SignalR پخش می‌شود.
/// </summary>
public class HardwareMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _sf;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly SemaphoreSlim _sweepLock = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _cache = new(); // "kind:id" → آنلاین

    public bool Sweeping { get; private set; }
    public DateTime LastSweepAt { get; private set; } = DateTime.MinValue;

    public HardwareMonitor(IServiceScopeFactory sf, IHubContext<DashboardHub> hub)
    {
        _sf = sf;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(3000, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(); }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    /// <summary>یک دور کامل بررسی (پینگ + TCP) و پخش نتیجه.</summary>
    public async Task SweepAsync()
    {
        if (!await _sweepLock.WaitAsync(0)) return; // دوره‌ی قبلی هنوز در جریان است
        Sweeping = true;
        try
        {
            await BroadcastAsync(); // اطلاع‌رسانی حالت «در حال بررسی»

            using var scope = _sf.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var devices = await CollectDevicesAsync(db);

            using var sem = new SemaphoreSlim(48);
            var tasks = devices.Select(async d =>
            {
                if (string.IsNullOrWhiteSpace(d.Ip)) return;
                await sem.WaitAsync();
                try
                {
                    var online = await IsAliveAsync(d.Ip, d.Kind);
                    _cache[$"{d.Kind}:{d.Id}"] = online;
                }
                catch { _cache[$"{d.Kind}:{d.Id}"] = false; }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);

            LastSweepAt = DateTime.Now;
        }
        finally
        {
            Sweeping = false;
            _sweepLock.Release();
        }
        await BroadcastAsync();
    }

    private static async Task<bool> IsAliveAsync(string ip, string kind)
    {
        // ۱) پینگ
        try
        {
            using var pinger = new Ping();
            var reply = await pinger.SendPingAsync(ip, 600);
            if (reply.Status == IPStatus.Success) return true;
        }
        catch { }

        // ۲) TCP — برای دستگاه‌هایی که ICMP می‌بندند (دوربین/NVR معمولاً وب/RTSP دارند)
        int[] ports = kind switch
        {
            "camera" => new[] { 80, 554, 8000 },
            "nvr" => new[] { 80, 8000, 554 },
            _ => new[] { 445, 80, 139 }
        };
        foreach (var port in ports)
        {
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(350);
                await tcp.ConnectAsync(ip, port, cts.Token);
                if (tcp.Connected) return true;
            }
            catch { }
        }
        return false;
    }

    private async Task<List<HardwareDeviceStatus>> CollectDevicesAsync(AppDbContext db)
    {
        var list = new List<HardwareDeviceStatus>();

        // کامپیوترها — IP از جزئیات شبکه‌ی شناسنامه سیستم + امتیاز سلامت
        var systems = await db.SystemInfos.AsNoTracking().ToListAsync();
        var healths = await Services.SystemHealth.ComputeManyAsync(db, systems.Select(s => s.Id).ToList());
        foreach (var s in systems)
        {
            var ip = ExtractFirstIp(s.DetailsJson);
            list.Add(new HardwareDeviceStatus
            {
                Kind = "system",
                Id = s.Id,
                Name = string.IsNullOrWhiteSpace(s.Cpu) ? s.AgentId : Trunc(s.Cpu, 40),
                Ip = ip ?? "",
                Extra = s.IsApproved ? "تاییدشده" : "در انتظار تایید",
                Health = healths.TryGetValue(s.Id, out var rep) ? rep.Score : null
            });
        }

        // دوربین‌ها
        var cameras = await db.CctvCameras.AsNoTracking().ToListAsync();
        foreach (var c in cameras)
            list.Add(new HardwareDeviceStatus
            {
                Kind = "camera",
                Id = c.Id,
                Name = c.Model,
                Ip = c.Ip ?? "",
                Extra = (string.IsNullOrWhiteSpace(c.Location) ? "بدون محل" : c.Location) + (c.IsActive ? "" : " — غیرفعال")
            });

        // NVRها
        var nvrs = await db.CctvNvrs.AsNoTracking().ToListAsync();
        foreach (var n in nvrs)
            list.Add(new HardwareDeviceStatus
            {
                Kind = "nvr",
                Id = n.Id,
                Name = n.Model,
                Ip = n.Ip ?? "",
                Extra = string.IsNullOrWhiteSpace(n.Location) ? "" : n.Location
            });

        return list;
    }

    private static string? ExtractFirstIp(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(detailsJson);
            if (doc.RootElement.TryGetProperty("netAdapters", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    if (a.TryGetProperty("ipv4", out var ip) && ip.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = ip.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s.Split(',')[0].Trim();
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string Trunc(string s, int len) => s.Length <= len ? s : s[..len] + "…";

    /// <summary>ساخت آمار فعلی (شمارش‌ها + وضعیت آنلاین هر دستگاه از کش).</summary>
    public async Task<HardwareStats> BuildStatsAsync()
    {
        using var scope = _sf.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var devices = await CollectDevicesAsync(db);
        foreach (var d in devices)
            d.Online = _cache.TryGetValue($"{d.Kind}:{d.Id}", out var on) ? on : null;

        var systems = await db.SystemInfos.AsNoTracking().CountAsync();
        var approved = await db.SystemInfos.AsNoTracking().CountAsync(x => x.IsApproved);
        var camTotal = await db.CctvCameras.AsNoTracking().CountAsync();
        var camActive = await db.CctvCameras.AsNoTracking().CountAsync(c => c.IsActive);
        var nvrTotal = await db.CctvNvrs.AsNoTracking().CountAsync();
        var nvrActive = await db.CctvNvrs.AsNoTracking().CountAsync(n => n.IsActive);

        // میانگین امتیاز سلامت سیستم‌ها
        var sysHealths = devices.Where(d => d.Kind == "system" && d.Health.HasValue).Select(d => d.Health!.Value).ToList();
        var avgHealth = sysHealths.Count > 0 ? (int?)Math.Round(sysHealths.Average()) : null;

        return new HardwareStats
        {
            Sweeping = Sweeping,
            LastSweepAt = LastSweepAt,
            SystemsTotal = systems,
            SystemsApproved = approved,
            SystemsPending = systems - approved,
            SystemsAvgHealth = avgHealth,
            CamerasTotal = camTotal,
            CamerasActiveFlag = camActive,
            NvrsTotal = nvrTotal,
            NvrsActiveFlag = nvrActive,
            Devices = devices
        };
    }

    public async Task BroadcastAsync()
    {
        try
        {
            var stats = await BuildStatsAsync();
            await _hub.Clients.Group(DashboardHub.HardwareGroup).SendAsync("ReceiveHardwareStats", stats);
        }
        catch { }
    }
}
