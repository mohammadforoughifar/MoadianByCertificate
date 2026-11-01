using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.FaCom;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// سرویس پس‌زمینه هشدار انقضای مدارک.
/// روزی یک‌بار مدارکی را که به آستانه‌های تعریف‌شده رسیده‌اند پیدا می‌کند و
/// برای دارندگان «دسترسی کامل» کار کارتابل + اعلان می‌سازد.
/// برای هر مدرک/آستانه/تاریخ انقضا فقط یک‌بار هشدار می‌رود (جدول DocExpiryAlert).
/// </summary>
public class DocExpiryWatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DocExpiryWatcher> _log;
    private readonly IConfiguration _cfg;

    public DocExpiryWatcher(IServiceProvider sp, ILogger<DocExpiryWatcher> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    private const string FormName = "آرشیو اسناد و مدارک";

    /// <summary>آستانه‌های هشدار بر حسب روز مانده تا انقضا. 0 یعنی «امروز منقضی شد».</summary>
    private int[] Thresholds =>
        _cfg.GetSection("DocArchive:ExpiryThresholds").Get<int[]>() is { Length: > 0 } t
            ? t.OrderByDescending(x => x).ToArray()
            : new[] { 60, 30, 7, 0 };

    /// <summary>ساعتی از شبانه‌روز که بررسی انجام می‌شود (پیش‌فرض ۷ صبح).</summary>
    private int RunAtHour => _cfg.GetValue<int?>("DocArchive:ExpiryCheckHour") ?? 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // کمی صبر تا مایگریشن‌ها و راه‌اندازی برنامه تمام شود
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در بررسی انقضای مدارک");
            }

            // تا ساعت مقرر روز بعد صبر کن
            var now = DateTime.Now;
            var next = now.Date.AddHours(RunAtHour);
            if (next <= now) next = next.AddDays(1);

            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>یک دور کامل بررسی — از کنترلر هم برای اجرای دستی صدا زده می‌شود.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IDocAccessService>();
        var notify = scope.ServiceProvider.GetRequiredService<INotifyService>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();

        var today = DateTime.Today;
        var thresholds = Thresholds;
        var maxDays = thresholds.Max();

        // فقط مدارک فعال و حذف‌نشده‌ای که تاریخ انقضا دارند و در بازه دید ما هستند
        var horizon = today.AddDays(maxDays);
        var docs = await db.Documents.AsNoTracking()
            .Where(d => d.IsActive && !d.IsDeleted
                     && d.ExpireDate != null
                     && d.ExpireDate.Value.Date <= horizon)
            .ToListAsync(ct);

        if (docs.Count == 0) return 0;

        var docIds = docs.Select(d => d.Id).ToList();
        var sent = await db.DocExpiryAlerts.AsNoTracking()
            .Where(a => docIds.Contains(a.DocumentId))
            .ToListAsync(ct);

        var totalTasks = 0;
        var notifyQueue = new List<(List<int> Users, string Title, string Body, string Link)>();

        foreach (var d in docs)
        {
            var exp = d.ExpireDate!.Value.Date;
            var daysLeft = (exp - today).Days;

            // بزرگ‌ترین آستانه‌ای که هنوز رد نشده — مثلاً ۴۵ روز مانده ⇒ آستانه ۶۰
            // اگر مدرک منقضی شده (daysLeft < 0) آستانه ۰ در نظر گرفته می‌شود
            int? hit = daysLeft < 0
                ? 0
                : thresholds.Where(t => daysLeft <= t).DefaultIfEmpty(-1).Min();

            if (hit is null or < 0) continue;
            var threshold = hit.Value;

            // قبلاً برای همین مدرک، همین آستانه و همین تاریخ انقضا هشدار رفته؟
            if (sent.Any(a => a.DocumentId == d.Id
                           && a.ThresholdDays == threshold
                           && a.ExpireDate.Date == exp)) continue;

            var owners = (await access.UsersWithFullAccessAsync(d.Id)).Distinct().ToList();

            var label = threshold == 0
                ? (daysLeft < 0 ? $"{-daysLeft} روز است منقضی شده" : "امروز منقضی می‌شود")
                : $"{daysLeft} روز تا انقضا";

            var title = threshold == 0 && daysLeft < 0
                ? $"مدرک {d.Code} منقضی شده — {d.Title}"
                : $"انقضای مدرک {d.Code} ({label}) — {d.Title}";

            foreach (var uid in owners)
            {
                db.DocCartableTasks.Add(new DocCartableTask
                {
                    Kind = "ExpiryAlert",
                    UserId = uid,
                    DocumentId = d.Id,
                    Title = title
                });
                totalTasks++;
            }

            db.DocExpiryAlerts.Add(new DocExpiryAlert
            {
                DocumentId = d.Id,
                ThresholdDays = threshold,
                ExpireDate = exp,
                NotifiedCount = owners.Count
            });

            db.DocumentLogs.Add(new DocumentLog
            {
                DocumentId = d.Id,
                Action = "ExpiryAlert",
                Detail = $"هشدار انقضا ({label}) برای {owners.Count} کاربر ارسال شد.",
                UserId = 0,
                UserName = "سیستم"
            });

            if (owners.Count > 0)
                notifyQueue.Add((owners, title,
                    $"تاریخ انقضای مدرک «{d.Title}» ({d.Code}) {exp:yyyy/MM/dd} است — {label}.",
                    $"/doc-archive/documents/{d.Id}"));

            // اسکالیشن: اگر ۷ روز یا کمتر مانده یا منقضی شده، به مدیران آرشیو هم هشدار بده (حتی اگر مالک مدرک نیستند)
            if (threshold <= 7)
            {
                var managers = await db.UserRoles.AsNoTracking()
                    .Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive)
                        && db.RolePermissions.Any(rp => rp.RoleId == ur.RoleId && db.Permissions.Any(p => p.Id == rp.PermissionId && p.Module == "DocArchive" && p.Action == "Manage")))
                    .Select(ur => ur.UserId).Distinct().ToListAsync(ct);
                // ادمین‌های قدیمی بدون نقش هم مدیرند
                var legacyAdmins = await db.Users.AsNoTracking()
                    .Where(u => u.Role == "Admin" && u.IsActive && !db.UserRoles.Any(ur => ur.UserId == u.Id))
                    .Select(u => u.Id).ToListAsync(ct);
                managers.AddRange(legacyAdmins);
                managers = managers.Distinct().Where(id => !owners.Contains(id)).ToList();
                if (managers.Count > 0)
                {
                    var escTitle = $"[اسکالیشن] {title}";
                    var escBody = $"هشدار اسکالیشن: مدرک «{d.Title}» ({d.Code}) {label} و هنوز تمدید نشده — لطفاً پیگیری کنید. مالکان: {owners.Count} نفر.";
                    notifyQueue.Add((managers, escTitle, escBody, $"/doc-archive/documents/{d.Id}"));
                    // برای اسکالیشن هم کارتابل بساز
                    foreach (var mid in managers)
                    {
                        db.DocCartableTasks.Add(new DocCartableTask
                        {
                            Kind = "ExpiryEscalation",
                            UserId = mid,
                            DocumentId = d.Id,
                            Title = escTitle
                        });
                        totalTasks++;
                    }
                }
            }
        }

        if (totalTasks > 0 || notifyQueue.Count > 0)
        {
            await db.SaveChangesAsync(ct);

            foreach (var n in notifyQueue)
                await notify.SendManyAsync(n.Users, n.Title, n.Body, "سیستم", FormName, n.Link);

            try
            {
                if (sms.IsConfigured)
                {
                    var allUsers = notifyQueue.SelectMany(n => n.Users).Distinct().ToList();
                    var mobs = await db.HrEmployees.AsNoTracking()
                        .Where(e => e.SystemUserId != null && allUsers.Contains(e.SystemUserId.Value)
                            && e.Mobile != null && e.Mobile != "")
                        .Select(e => e.Mobile!).Distinct().ToListAsync(ct);
                    foreach (var m in mobs)
                        try { await sms.SendAsync(m.Trim(), "هشدار انقضای مدرک در آرشیو اسناد ثبت شد؛ لطفاً کارتابل خود را بررسی کنید."); }
                        catch { }
                }
            }
            catch { }

            await notify.BroadcastChangedAsync("doc-archive");
            _log.LogInformation("هشدار انقضای مدارک: {Count} کار کارتابل ساخته شد.", totalTasks);
        }

        return totalTasks;
    }
}
