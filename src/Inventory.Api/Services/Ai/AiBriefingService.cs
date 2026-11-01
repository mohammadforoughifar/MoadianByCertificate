using System.Text;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// گزارش صبحگاهی «فروغ آریا» — خلاصه روزانه خودکار برای هر کاربر:
// نامه‌های خوانده‌نشده، مهلت‌های نزدیک ارجاع، چک‌های نزدیک سررسید،
// مرخصی‌های در انتظار تأیید. ارسال با پیام‌رسان (بله/ایتا) به کاربران
// لینک‌شده‌ای که مجوز AiAssistant.Use دارند.
// =====================================================================

public class AiBriefingService
{
    private readonly AppDbContext _db;
    private readonly AiOptions _options;
    private readonly IInnerLetterService _letters;
    private readonly IFaAttService _faAtt;
    private readonly ITreasuryService _treasury;
    private readonly AiAlertsService _alerts;
    private readonly ILogger<AiBriefingService> _log;

    public AiBriefingService(
        AppDbContext db,
        IOptions<AiOptions> options,
        IInnerLetterService letters,
        IFaAttService faAtt,
        ITreasuryService treasury,
        AiAlertsService alerts,
        ILogger<AiBriefingService> log)
    {
        _db = db;
        _options = options.Value;
        _letters = letters;
        _faAtt = faAtt;
        _treasury = treasury;
        _alerts = alerts;
        _log = log;
    }

    /// <summary>ارسال گزارش به همه کاربران واجد شرایط؛ برمی‌گرداند: تعداد ارسال موفق.</summary>
    public async Task<int> SendToAllAsync(IMessengerService messenger, CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.BaleChatId != null || u.EitaaChatId != null))
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName, u.Role })
            .Take(200)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var u in users)
        {
            try
            {
                if (!await AiAccessHelper.UserHasAsync(_db, u.Id, "AiAssistant", "Use", u.Role, ct)) continue;
                var name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
                if (name == "") name = u.Username;
                var text = await BuildBriefingAsync(u.Id, name, ct);
                var result = await messenger.SendToUserAsync(u.Id, "☀️ گزارش صبحگاهی", text);
                if (result.BaleSent || result.EitaaSent) sent++;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "ارسال گزارش صبحگاهی به کاربر {UserId} ناموفق بود.", u.Id);
            }
        }
        return sent;
    }

    /// <summary>متن گزارش صبحگاهی یک کاربر (متن ساده برای پیام‌رسان — بدون لینک).</summary>
    public async Task<string> BuildBriefingAsync(int userId, string displayName, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"☀️ گزارش صبحگاهی — {AiDateUtil.TodayFa()}");
        sb.AppendLine($"سلام {displayName}! 👋");
        var notable = 0;

        // ---------- ۱) نامه‌های خوانده‌نشده ----------
        try
        {
            var stats = await _letters.GetStatsAsync(userId);
            if (stats.InboxUnread > 0)
            {
                notable++;
                sb.AppendLine();
                sb.AppendLine($"📩 {FaNum(stats.InboxUnread)} نامه خوانده‌نشده داری:");
                var page = await _letters.GetInboxAsync(userId, null, true, 1, 3);
                foreach (var l in page.Items.Take(3))
                    sb.AppendLine($"- {Truncate(l.Title, 60)} (از {Truncate(l.Sender, 30)} — {AiDateUtil.ToFaShort(l.Date)})");
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۲) مهلت‌های نزدیک ارجاع ----------
        try
        {
            var horizon = DateTime.Now.AddDays(Math.Max(1, _options.BriefingDeadlineDays));
            var deadlines = await _db.Erjas.AsNoTracking()
                .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                            && e.MohlatPasokh != null && e.Answer == "" && e.MohlatPasokh <= horizon
                            && e.Source.InnerLetter != null && !e.Source.InnerLetter.IsDelete)
                .OrderBy(e => e.MohlatPasokh)
                .Select(e => new { Title = e.Source.InnerLetter!.Title, Deadline = e.MohlatPasokh!.Value })
                .Take(5)
                .ToListAsync(ct);
            if (deadlines.Count > 0)
            {
                notable++;
                sb.AppendLine();
                sb.AppendLine("⏰ مهلت‌های نزدیک ارجاع:");
                foreach (var d in deadlines)
                    sb.AppendLine($"- {Truncate(d.Title, 60)} — مهلت: {AiDateUtil.ToFaShort(d.Deadline)}");
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۳) چک‌های نزدیک سررسید (فقط با دسترسی خزانه) ----------
        try
        {
            var me = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
            if (await AiAccessHelper.UserHasAsync(_db, userId, "TrsCheques", "Read", me, ct))
            {
                var to = DateTime.Today.AddDays(Math.Max(1, _options.BriefingChequeDays));
                var cheques = await _treasury.GetChequesAsync(null, null, null, null, null,
                    DateTime.Today.AddDays(-30), to, true, 1, 10);
                if (cheques.Items.Count > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine($"🧾 {FaNum(cheques.Items.Count)} چک باز نزدیک سررسید:");
                    foreach (var c in cheques.Items.Take(5))
                        sb.AppendLine($"- چک {c.Number} — {FaMoney(c.Amount)} — سررسید {AiDateUtil.ToFaShort(c.DueDate)}");
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۴) مرخصی‌های در انتظار تأیید من (مدیران) ----------
        try
        {
            var meRole = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
            if (await AiAccessHelper.UserHasAsync(_db, userId, "FaAtt", "Read", meRole, ct))
            {
                var myEmpId = await _db.HrEmployees.AsNoTracking()
                    .Where(e => e.SystemUserId == userId).Select(e => e.Id).FirstOrDefaultAsync(ct);
                if (myEmpId != 0)
                {
                    var reportIds = await _db.HrEmployees.AsNoTracking()
                        .Where(e => e.ManagerId == myEmpId).Select(e => e.Id).ToListAsync(ct);
                    var pending = new List<(string Name, string? Type, DateTime From, DateTime To)>();
                    foreach (var empId in reportIds.Take(50))
                    {
                        var leaves = await _faAtt.ListLeavesAsync(empId, 0);
                        pending.AddRange(leaves.Where(l => l.Status == 0)
                            .Select(l => (l.EmployeeName ?? "همکار", l.LeaveTypeName, l.FromDate, l.ToDate)));
                        if (pending.Count >= 8) break;
                    }
                    if (pending.Count > 0)
                    {
                        notable++;
                        sb.AppendLine();
                        sb.AppendLine($"🏖️ {FaNum(pending.Count)} مرخصی در انتظار تأیید توست:");
                        foreach (var p in pending.Take(5))
                            sb.AppendLine($"- {p.Name}: {p.Type ?? "مرخصی"} ({AiDateUtil.ToFaShort(p.From)} تا {AiDateUtil.ToFaShort(p.To)})");
                    }
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۵) مرخصی‌های خودم در انتظار ----------
        try
        {
            var mine = (await _faAtt.MyLeavesAsync(userId)).Where(l => l.Status == 0).Take(3).ToList();
            if (mine.Count > 0)
            {
                notable++;
                sb.AppendLine();
                sb.AppendLine("📝 مرخصی‌های خودت در انتظار تأیید:");
                foreach (var l in mine)
                    sb.AppendLine($"- {l.LeaveTypeName ?? "مرخصی"} ({AiDateUtil.ToFaShort(l.FromDate)} تا {AiDateUtil.ToFaShort(l.ToDate)})");
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۶) هشدارهای هوشمند (§۱۷) ----------
        try
        {
            var alerts = await _alerts.GetAlertsAsync(userId, ct);
            foreach (var a in alerts)
            {
                notable++;
                sb.AppendLine();
                sb.AppendLine($"{a.Icon} {a.Title}:");
                foreach (var line in a.Lines.Take(6))
                    sb.AppendLine($"- {line}");
            }
        }
        catch { /* بخش اختیاری */ }

        if (notable == 0)
        {
            sb.AppendLine();
            sb.AppendLine("امروز مورد خاصی نداری؛ روز خوبی داشته باشی! 🎉");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("سؤالی داری؟ همین‌جا بنویس! 🤖");
        }

        var text = sb.ToString().Trim();
        return text.Length > 3500 ? text[..3500] + "…" : text;
    }

    private static string Truncate(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static string FaNum(int n) => AiTextUtil.ToFaDigits(n.ToString());

    private static string FaMoney(decimal amount) =>
        AiTextUtil.ToFaDigits(amount.ToString("#,##0")) + " تومان";
}

// =====================================================================
// ورکر روزانه: هر روز سر ساعت تنظیم‌شده، گزارش را برای کاربران واجد
// شرایط (لینک‌شده به بله/ایتا + دارای مجوز AiAssistant.Use) می‌فرستد.
// ساعت به وقت محلی سرور است.
// =====================================================================

public class AiBriefingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiBriefingWorker> _log;

    public AiBriefingWorker(IServiceScopeFactory scopes, ILogger<AiBriefingWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // کمی صبر تا اپ کاملاً بالا بیاید و سیدرها تمام شوند
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                if (!options.Enabled || !options.BriefingEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                var now = DateTime.Now;
                var runAt = ParseTime(options.BriefingTime);
                var next = now.Date.Add(runAt);
                if (next <= now.AddSeconds(5)) next = next.AddDays(1);
                var wait = next - now;
                _log.LogInformation("گزارش صبحگاهی بعدی: {Next} (حدود {Minutes} دقیقه دیگر)", next, (int)wait.TotalMinutes);
                await Task.Delay(wait, stoppingToken);

                await SendAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "خطا در چرخه گزارش صبحگاهی؛ تلاش مجدد بعداً.");
                try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task SendAllAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var briefing = sp.GetRequiredService<AiBriefingService>();
        var messenger = sp.GetRequiredService<IMessengerService>();
        var sent = await briefing.SendToAllAsync(messenger, ct);
        _log.LogInformation("گزارش صبحگاهی به {Sent} کاربر ارسال شد.", sent);
    }

    internal static TimeSpan ParseTime(string? value)
    {
        if (TimeSpan.TryParse((value ?? "").Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            return t;
        return new TimeSpan(7, 30, 0);
    }
}
