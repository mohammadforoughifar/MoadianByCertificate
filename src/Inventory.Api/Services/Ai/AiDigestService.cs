using System.Text;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// خلاصه هفتگی مدیر «فروغ آریا» — جمع‌بندی ۷ روز گذشته برای مدیران:
// فروش و خرید هفته، منابع انسانی (مرخصی/مأموریت/تأییدها)، تیکت‌ها،
// ارجاع‌ها، چک‌های هفته آینده، گزارش‌کارها + هشدارهای جاری (§۱۷).
// مخاطب: کاربران لینک‌شده به پیام‌رسان که مجوز FaAtt.Manage دارند.
// ارسال هفتگی با AiDigestWorker؛ متن ساده برای پیام‌رسان — بدون لینک.
// =====================================================================

public class AiDigestService
{
    private readonly AppDbContext _db;
    private readonly AiOptions _options;
    private readonly IInvoicingService _invoicing;
    private readonly ITreasuryService _treasury;
    private readonly IFaAttService _faAtt;
    private readonly AiAlertsService _alerts;
    private readonly ILogger<AiDigestService> _log;

    public AiDigestService(
        AppDbContext db,
        IOptions<AiOptions> options,
        IInvoicingService invoicing,
        ITreasuryService treasury,
        IFaAttService faAtt,
        AiAlertsService alerts,
        ILogger<AiDigestService> log)
    {
        _db = db;
        _options = options.Value;
        _invoicing = invoicing;
        _treasury = treasury;
        _faAtt = faAtt;
        _alerts = alerts;
        _log = log;
    }

    /// <summary>ارسال خلاصه هفته به همه مدیران واجد شرایط؛ برمی‌گرداند: تعداد ارسال موفق.</summary>
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
                if (!await AiAccessHelper.UserHasAsync(_db, u.Id, "FaAtt", "Manage", u.Role, ct)) continue;
                var name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
                if (name == "") name = u.Username;
                var text = await BuildDigestAsync(u.Id, name, ct);
                var result = await messenger.SendToUserAsync(u.Id, "🗓 خلاصه هفته", text);
                if (result.BaleSent || result.EitaaSent) sent++;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "ارسال خلاصه هفته به کاربر {UserId} ناموفق بود.", u.Id);
            }
        }
        return sent;
    }

    /// <summary>متن خلاصه هفته یک مدیر (بازه: ۷ روز گذشته تا امروز).</summary>
    public async Task<string> BuildDigestAsync(int userId, string displayName, CancellationToken ct)
    {
        var to = DateTime.Today;
        var from = to.AddDays(-6);
        var sb = new StringBuilder();
        sb.AppendLine($"🗓 خلاصه هفته — از {AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}");
        sb.AppendLine($"سلام {displayName}! 👋");
        var notable = 0;
        var role = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);

        // ---------- ۱) فروش و خرید هفته ----------
        try
        {
            if (await AiAccessHelper.UserHasAsync(_db, userId, "FacInvoices", "Read", role, ct))
            {
                var d = await _invoicing.GetDashboardAsync(from, to);
                if (d.SaleCount > 0 || d.PurchaseTotal > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine("💰 فروش و خرید هفته:");
                    sb.AppendLine($"- فروش: {FaNum(d.SaleCount)} فاکتور — {FaMoney(d.SaleTotal)}");
                    if (d.PurchaseTotal > 0)
                        sb.AppendLine($"- خرید: {FaMoney(d.PurchaseTotal)}");
                    if (d.GrossProfit != 0)
                        sb.AppendLine($"- سود ناخالص تقریبی: {FaMoney(d.GrossProfit)}");
                    var top = d.TopProducts.FirstOrDefault();
                    if (top != null)
                        sb.AppendLine($"- پرفروش‌ترین: {Truncate(top.Title, 40)} ({FaMoney(top.Net)})");
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۲) منابع انسانی ----------
        try
        {
            if (await AiAccessHelper.UserHasAsync(_db, userId, "FaAtt", "Read", role, ct))
            {
                var weekEnd = to.AddDays(1);
                var leaves = await _db.FaAttLeaves.AsNoTracking()
                    .Where(l => l.FromDate >= from && l.FromDate < weekEnd)
                    .Select(l => l.Status).ToListAsync(ct);
                var missions = await _db.FaAttMissions.AsNoTracking()
                    .CountAsync(m => m.FromDate >= from && m.FromDate < weekEnd, ct);
                if (leaves.Count > 0 || missions > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine("🏖️ منابع انسانی هفته:");
                    if (leaves.Count > 0)
                        sb.AppendLine($"- {FaNum(leaves.Count)} مرخصی شروع شد " +
                            $"({FaNum(leaves.Count(s => s == FaAttRequestStatus.Approved))} تأیید، " +
                            $"{FaNum(leaves.Count(s => s == FaAttRequestStatus.Pending))} در انتظار)");
                    if (missions > 0)
                        sb.AppendLine($"- {FaNum(missions)} مأموریت");
                }
                // در انتظار تأیید من (تیم مستقیم)
                var myEmpId = await _db.HrEmployees.AsNoTracking()
                    .Where(e => e.SystemUserId == userId).Select(e => e.Id).FirstOrDefaultAsync(ct);
                if (myEmpId != 0)
                {
                    var reportIds = await _db.HrEmployees.AsNoTracking()
                        .Where(e => e.ManagerId == myEmpId).Select(e => e.Id).ToListAsync(ct);
                    var pending = new List<string>();
                    foreach (var empId in reportIds.Take(50))
                    {
                        var ls = await _faAtt.ListLeavesAsync(empId, 0);
                        pending.AddRange(ls.Where(l => l.Status == 0)
                            .Select(l => $"{l.EmployeeName ?? "همکار"} ({AiDateUtil.ToFaShort(l.FromDate)})"));
                        if (pending.Count >= 5) break;
                    }
                    if (pending.Count > 0)
                    {
                        notable++;
                        sb.AppendLine($"- ⏳ {FaNum(pending.Count)} مرخصی در انتظار تأیید تو: " +
                            string.Join("، ", pending.Take(3).Select(s => Truncate(s, 30))));
                    }
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۳) تیکت‌ها ----------
        try
        {
            if (await AiAccessHelper.UserHasAsync(_db, userId, "FaCom", "Manage", role, ct))
            {
                var fresh = await _db.FaComTickets.AsNoTracking()
                    .Where(t => t.CreatedAt >= from)
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => t.Subject).Take(2).ToListAsync(ct);
                var open = await _db.FaComTickets.AsNoTracking()
                    .CountAsync(t => t.Status != FaComTicketStatus.Closed, ct);
                if (fresh.Count > 0 || open > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine($"🎫 تیکت‌ها: {FaNum(fresh.Count)} جدید در هفته، {FaNum(open)} باز:");
                    foreach (var s in fresh)
                        sb.AppendLine($"- {Truncate(s, 60)}");
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۴) ارجاع‌های من ----------
        try
        {
            if (await AiAccessHelper.UserHasAsync(_db, userId, "InnerLetters", "Read", role, ct))
            {
                var fresh = await _db.Erjas.AsNoTracking()
                    .CountAsync(e => e.ReciverUserId == userId && !e.IsDelete && e.Date >= from, ct);
                var unanswered = await _db.Erjas.AsNoTracking()
                    .CountAsync(e => e.ReciverUserId == userId && !e.IsDelete && e.Answer == "", ct);
                if (fresh > 0 || unanswered > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine($"📨 ارجاع‌ها: {FaNum(fresh)} جدید در هفته، {FaNum(unanswered)} بی‌پاسخ.");
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۵) چک‌های هفته آینده ----------
        try
        {
            if (await AiAccessHelper.UserHasAsync(_db, userId, "TrsCheques", "Read", role, ct))
            {
                var cheques = await _treasury.GetChequesAsync(null, null, null, null, null,
                    DateTime.Today, to.AddDays(7), true, 1, 10);
                if (cheques.Items.Count > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine($"🧾 {FaNum(cheques.Items.Count)} چک باز با سررسید هفته آینده " +
                        $"({FaMoney(cheques.Items.Sum(c => c.Amount))}):");
                    foreach (var c in cheques.Items.Take(3))
                        sb.AppendLine($"- چک {c.Number} — {FaMoney(c.Amount)} — {AiDateUtil.ToFaShort(c.DueDate)}");
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۶) گزارش‌کارها ----------
        try
        {
            if (await AiAccessHelper.UserHasAsync(_db, userId, "ReportWorks", "Read", role, ct))
            {
                var weekEnd = to.AddDays(1);
                var reports = await _db.ReportWorks.AsNoTracking()
                    .Where(r => r.ReportDate >= from && r.ReportDate < weekEnd)
                    .Select(r => r.UserId).ToListAsync(ct);
                if (reports.Count > 0)
                {
                    notable++;
                    sb.AppendLine();
                    sb.AppendLine($"📋 {FaNum(reports.Count)} گزارش‌کار از {FaNum(reports.Distinct().Count())} همکار ثبت شد.");
                }
            }
        }
        catch { /* بخش اختیاری */ }

        // ---------- ۷) یادآوری‌های جاری (§۱۷) ----------
        try
        {
            var alerts = await _alerts.GetAlertsAsync(userId, ct);
            foreach (var a in alerts)
            {
                notable++;
                sb.AppendLine();
                sb.AppendLine($"{a.Icon} {a.Title}:");
                foreach (var line in a.Lines.Take(4))
                    sb.AppendLine($"- {line}");
            }
        }
        catch { /* بخش اختیاری */ }

        if (notable == 0)
        {
            sb.AppendLine();
            sb.AppendLine("هفته آرومی بود؛ خسته نباشی! 🌱");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("جزئیات بیشتر را همین‌جا بپرس! 🤖");
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
// ورکر هفتگی: هر هفته در روز و ساعت تنظیم‌شده، خلاصه را برای مدیران
// واجد شرایط (لینک‌شده به بله/ایتا + دارای مجوز FaAtt.Manage) می‌فرستد.
// روز هفته به وقت محلی سرور است؛ پیش‌فرض: شنبه ۰۷:۳۰.
// =====================================================================

public class AiDigestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiDigestWorker> _log;

    public AiDigestWorker(IServiceScopeFactory scopes, ILogger<AiDigestWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                if (!options.Enabled || !options.DigestEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                var next = NextRun(DateTime.Now, ParseDay(options.DigestDay), AiBriefingWorker.ParseTime(options.DigestTime));
                var wait = next - DateTime.Now;
                _log.LogInformation("خلاصه هفتگی بعدی: {Next} (حدود {Hours} ساعت دیگر)", next, (int)wait.TotalHours);
                await Task.Delay(wait, stoppingToken);

                await SendAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "خطا در چرخه خلاصه هفتگی؛ تلاش مجدد بعداً.");
                try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task SendAllAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var digest = sp.GetRequiredService<AiDigestService>();
        var messenger = sp.GetRequiredService<IMessengerService>();
        var sent = await digest.SendToAllAsync(messenger, ct);
        _log.LogInformation("خلاصه هفتگی به {Sent} مدیر ارسال شد.", sent);
    }

    internal static DateTime NextRun(DateTime now, DayOfWeek day, TimeSpan time)
    {
        var daysUntil = (((int)day - (int)now.DayOfWeek) + 7) % 7;
        var next = now.Date.AddDays(daysUntil).Add(time);
        return next <= now.AddSeconds(5) ? next.AddDays(7) : next;
    }

    internal static DayOfWeek ParseDay(string? value)
    {
        var v = AiTextUtil.NormalizeFa(value ?? "").Replace(" ", "").Replace("‌", "");
        return v switch
        {
            "شنبه" or "saturday" => DayOfWeek.Saturday,
            "یکشنبه" or "sunday" => DayOfWeek.Sunday,
            "دوشنبه" or "monday" => DayOfWeek.Monday,
            "سهشنبه" or "tuesday" => DayOfWeek.Tuesday,
            "چهارشنبه" or "wednesday" => DayOfWeek.Wednesday,
            "پنجشنبه" or "thursday" => DayOfWeek.Thursday,
            "جمعه" or "friday" => DayOfWeek.Friday,
            _ => DayOfWeek.Saturday,
        };
    }
}
