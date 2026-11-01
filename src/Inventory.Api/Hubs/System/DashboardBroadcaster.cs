using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;

namespace Inventory.Api.Hubs;

public class DashboardStats
{
    public int SystemsTotal { get; set; }
    public int SystemsApproved { get; set; }
    public int SystemsPending { get; set; }
    public int SystemsWithChanges { get; set; }
    public int CamerasTotal { get; set; }
    public int CamerasActive { get; set; }
    public int NvrsTotal { get; set; }
    public int NvrsActive { get; set; }
    public List<ChartItem> CamerasByLocation { get; set; } = new();
    public List<ChartItem> CamerasByNvr { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

public class ChartItem
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>محاسبهٔ آمار داشبورد و پخش بلادرنگ آن فقط برای گروه مجاز داشبورد مدیریت.</summary>
public class DashboardBroadcaster
{
    private readonly IHubContext<DashboardHub> _hub;
    private readonly IServiceScopeFactory _sf;

    private readonly HardwareMonitor _monitor;
    public DashboardBroadcaster(IHubContext<DashboardHub> hub, IServiceScopeFactory sf, HardwareMonitor monitor)
    {
        _hub = hub;
        _sf = sf;
        _monitor = monitor;
    }

    public static async Task<DashboardStats> BuildAsync(AppDbContext db)
    {
        var systems = await db.SystemInfos.AsNoTracking().ToListAsync();
        var cameras = await db.CctvCameras.AsNoTracking().ToListAsync();
        var nvrs = await db.CctvNvrs.AsNoTracking().ToListAsync();

        var s = new DashboardStats
        {
            SystemsTotal = systems.Count,
            SystemsApproved = systems.Count(x => x.IsApproved),
            SystemsPending = systems.Count(x => !x.IsApproved),
            SystemsWithChanges = systems.Count(x => !string.IsNullOrEmpty(x.PendingPayloadJson)),
            CamerasTotal = cameras.Count,
            CamerasActive = cameras.Count(c => c.IsActive),
            NvrsTotal = nvrs.Count,
            NvrsActive = nvrs.Count(n => n.IsActive),
            GeneratedAt = DateTime.Now
        };

        s.CamerasByLocation = cameras
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Location) ? "بدون محل مشخص" : c.Location!)
            .Select(g => new ChartItem { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToList();

        s.CamerasByNvr = cameras
            .GroupBy(c => nvrs.FirstOrDefault(n => n.Id == c.NvrId) is { } nv ? $"{nv.Model}" : "بدون NVR")
            .Select(g => new ChartItem { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        return s;
    }

    public async Task BroadcastAsync()
    {
        try
        {
            using var scope = _sf.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stats = await BuildAsync(db);
            await _hub.Clients.Group(DashboardHub.ManagementGroup).SendAsync("ReceiveStats", stats);
            await _monitor.BroadcastAsync();
        }
        catch { /* پخش ناموفق نباید عملیات اصلی را خراب کند */ }
    }
}
