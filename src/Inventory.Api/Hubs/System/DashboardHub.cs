using Inventory.Api.Services.Core;
using Microsoft.AspNetCore.SignalR;

namespace Inventory.Api.Hubs;

/// <summary>
/// هاب بلادرنگ داشبورد. هر اتصال فقط عضو گروهِ داشبوردی می‌شود که مجوز صریح آن را دارد؛
/// بنابراین دادهٔ مانیتورینگ یا دوربین‌ها به همهٔ کاربران واردشده پخش نمی‌شود.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize]
public class DashboardHub : Hub
{
    public const string ManagementGroup = "Dashboard_Management";
    public const string HardwareGroup = "Dashboard_Hardware";

    private readonly IServiceProvider _sp;
    private readonly HardwareMonitor _monitor;
    private readonly IEffectivePermissions _permissions;

    public DashboardHub(IServiceProvider sp, HardwareMonitor monitor, IEffectivePermissions permissions)
    {
        _sp = sp;
        _monitor = monitor;
        _permissions = permissions;
    }

    private async Task<bool> HasAsync(string action)
    {
        if (Context.User is null) return false;
        return await _permissions.IsLegacyAdminAsync(Context.User)
               || (await _permissions.GetAsync(Context.User)).Contains($"Dashboards.{action}");
    }

    public override async Task OnConnectedAsync()
    {
        var canSeeManagement = await HasAsync("Management");
        var canSeeHardware = await HasAsync("Hardware");
        if (!canSeeManagement && !canSeeHardware)
            throw new HubException("شما به هیچ‌یک از داشبوردهای بلادرنگ دسترسی ندارید.");

        // ارسال داده و عضویت در گروه‌ها دقیقاً مطابق همان مجوزی که صفحه نیاز دارد.
        if (canSeeManagement)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ManagementGroup);
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                await Clients.Caller.SendAsync("ReceiveStats", await DashboardBroadcaster.BuildAsync(db));
            }
            catch { /* اتصال هاب به دلیل خطای گذرای داده قطع نشود. */ }
        }

        if (canSeeHardware)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HardwareGroup);
            try { await Clients.Caller.SendAsync("ReceiveHardwareStats", await _monitor.BuildStatsAsync()); }
            catch { /* اتصال هاب به دلیل خطای گذرای داده قطع نشود. */ }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>درخواست یک دور بررسی فوری پینگ همهٔ دستگاه‌ها از سمت داشبورد سخت‌افزار.</summary>
    public async Task RequestHardwareCheck()
    {
        if (!await HasAsync("Hardware"))
            throw new HubException("شما مجوز اجرای بررسی سخت‌افزار را ندارید.");
        await _monitor.SweepAsync();
    }
}
