// سرویس هشدارهای هوشمند (§۱۷) — ۷ بررسی روزانه، هر کدام با مجوز همان ماژول.
// خروجی ساخت‌یافته است تا هم بریفینگ پیام‌رسان (بدون لینک) و هم چت/آفلاین (با لینک) از آن استفاده کنند.
using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

public sealed class AiAlertSection
{
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Lines { get; set; } = new();
    public string? Link { get; set; }
    public string? LinkText { get; set; }
}

public class AiAlertsService
{
    private readonly AppDbContext _db;
    private readonly IWarehousingService _wh;

    public AiAlertsService(AppDbContext db, IWarehousingService wh)
    {
        _db = db;
        _wh = wh;
    }

    /// <summary>همه هشدارهای فعال یک کاربر؛ هر بخش خراب شود فقط همان رد می‌شود.</summary>
    public async Task<List<AiAlertSection>> GetAlertsAsync(int userId, CancellationToken ct)
    {
        var role = await _db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.Role).FirstOrDefaultAsync(ct);
        var list = new List<AiAlertSection>();
        await TryAdd(list, () => LowStockAsync(userId, role, ct));
        await TryAdd(list, () => BouncedChequesAsync(userId, role, ct));
        await TryAdd(list, () => OldDraftsAsync(userId, role, ct));
        await TryAdd(list, () => NewTicketsAsync(userId, role, ct));
        await TryAdd(list, () => ExpiringContractsAsync(userId, role, ct));
        await TryAdd(list, () => ReportNudgeAsync(userId, role, ct));
        await TryAdd(list, () => MinutesDueAsync(userId, role, ct));
        return list;
    }

    private static async Task TryAdd(List<AiAlertSection> list, Func<Task<AiAlertSection?>> build)
    {
        try
        {
            var s = await build();
            if (s != null) list.Add(s);
        }
        catch { /* هشدار خراب، بقیه را خراب نمی‌کند */ }
    }

    private async Task<bool> Can(int userId, string module, string? role, CancellationToken ct, string action = "Read")
        => await AiAccessHelper.UserHasAsync(_db, userId, module, action, role, ct);

    // ---------- ۷) اقدام‌های صورتجلسه نزدیک سررسید/معوق ----------
    private async Task<AiAlertSection?> MinutesDueAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "MeetingMinutes", role, ct, "View")) return null;
        var today = DateTime.Today;
        var rows = await _db.MeetingMinutesItems.AsNoTracking()
            .Where(i => i.ResponsibleUserId == userId && i.ItemStatus == MinutesItemStatus.InProgress
                && i.DueDate != null && i.DueDate <= today.AddDays(7)
                && i.Minutes != null && !i.Minutes.IsDeleted)
            .OrderBy(i => i.DueDate)
            .Select(i => new { i.MinutesId, Title = i.Minutes!.Title, i.RowNo, i.Description, Due = i.DueDate!.Value })
            .Take(6).ToListAsync(ct);
        if (rows.Count == 0) return null;
        var overdue = rows.Count(r => r.Due < today);
        var s = new AiAlertSection
        {
            Icon = overdue > 0 ? "\U0001F534" : "\U0001F7E1",
            Title = overdue > 0
                ? $"{FaNum(overdue)} اقدام معوق + {FaNum(rows.Count - overdue)} نزدیک سررسید در صورتجلسات"
                : $"{FaNum(rows.Count)} اقدام نزدیک سررسید در صورتجلسات",
            Link = "/misc/minutes",
            LinkText = "صورتجلسات",
        };
        foreach (var r in rows)
        {
            var flag = r.Due < today ? "معوق" : r.Due == today ? "امروز" : AiDateUtil.ToFaShort(r.Due);
            s.Lines.Add($"بند {FaNum(r.RowNo)} «{Truncate(r.Title, 35)}»: {Truncate(r.Description, 60)} — مهلت {flag}");
        }
        return s;
    }

    // ---------- ۱) کمبود انبار ----------
    private async Task<AiAlertSection?> LowStockAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "Products", role, ct)) return null;
        var page = await _wh.GetStockAsync(null, null, null, true, 1, 5);
        if (page.TotalCount == 0) return null;
        var s = new AiAlertSection
        {
            Icon = "📦",
            Title = $"{FaNum(page.TotalCount)} کالای زیر نقطه سفارش",
            Link = "/inv/stock",
            LinkText = "موجودی انبار",
        };
        foreach (var r in page.Items)
            s.Lines.Add($"{Truncate(r.ProductName, 40)} در {Truncate(r.WarehouseName, 25)}: موجودی {FmtQty(r.Quantity)} {r.Unit} (نقطه سفارش {FmtQty(r.ReorderPoint)})");
        if (page.TotalCount > page.Items.Count)
            s.Lines.Add($"…و {FaNum(page.TotalCount - page.Items.Count)} مورد دیگر");
        return s;
    }

    // ---------- ۲) چک‌های برگشتی باز ----------
    private async Task<AiAlertSection?> BouncedChequesAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "TrsCheques", role, ct)) return null;
        var q = _db.TrsCheques.AsNoTracking().Where(c => c.Status == ChequeStatus.Bounced);
        var count = await q.CountAsync(ct);
        if (count == 0) return null;
        var sum = await q.SumAsync(c => c.Amount, ct);
        var items = await q.OrderByDescending(c => c.DueDate).Take(5).ToListAsync(ct);
        var s = new AiAlertSection
        {
            Icon = "🚨",
            Title = $"{FaNum(count)} چک برگشتی باز (جمع {FaMoney(sum)})",
            Link = "/trs/cheques",
            LinkText = "چک‌ها",
        };
        foreach (var c in items)
            s.Lines.Add($"چک {c.Number} — {FaMoney(c.Amount)} — سررسید {AiDateUtil.ToFaShort(c.DueDate)}");
        return s;
    }

    // ---------- ۳) پیش‌فاکتورهای قدیمی ----------
    private async Task<AiAlertSection?> OldDraftsAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "FacInvoices", role, ct)) return null;
        var cutoff = DateTime.Today.AddDays(-7);
        var q = _db.FacInvoices.AsNoTracking()
            .Where(f => f.Status == InvoiceStatus.Draft && f.Date < cutoff);
        var count = await q.CountAsync(ct);
        if (count == 0) return null;
        var items = await q.OrderBy(f => f.Date).Take(5)
            .Select(f => new { f.Number, Party = f.Party != null ? f.Party.Name : null, f.Date })
            .ToListAsync(ct);
        var s = new AiAlertSection
        {
            Icon = "📝",
            Title = $"{FaNum(count)} پیش‌فاکتور قدیمی‌تر از یک هفته",
            Link = "/fac/invoices",
            LinkText = "فاکتورها",
        };
        foreach (var f in items)
            s.Lines.Add($"فاکتور {FaNum(f.Number)}{(f.Party != null ? $" — {Truncate(f.Party, 30)}" : "")} — {AiDateUtil.ToFaShort(f.Date)}");
        return s;
    }

    // ---------- ۴) تیکت‌های جدید HR ----------
    private async Task<AiAlertSection?> NewTicketsAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "FaCom", role, ct)) return null;
        var q = _db.FaComTickets.AsNoTracking().Where(t => t.Status == FaComTicketStatus.New);
        var count = await q.CountAsync(ct);
        if (count == 0) return null;
        var items = await q.OrderByDescending(t => t.CreatedAt).Take(5).ToListAsync(ct);
        var s = new AiAlertSection
        {
            Icon = "🎫",
            Title = $"{FaNum(count)} تیکت جدید",
            Link = "/fa-com/tickets",
            LinkText = "تیکت‌ها",
        };
        foreach (var t in items)
            s.Lines.Add($"{Truncate(t.Subject, 50)} — {AiDateUtil.ToFaShort(t.CreatedAt)}");
        return s;
    }

    // ---------- ۵) قراردادهای منقضی/رو به اتمام ----------
    private async Task<AiAlertSection?> ExpiringContractsAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "HrCore", role, ct)) return null;
        var today = DateTime.Today;
        var horizon = today.AddDays(30);
        var q = _db.HrContracts.AsNoTracking()
            .Where(c => c.IsActive && c.EndDate != null && c.EndDate.Value.Date <= horizon);
        var count = await q.CountAsync(ct);
        if (count == 0) return null;
        var items = await q.OrderBy(c => c.EndDate).Take(5).ToListAsync(ct);
        var s = new AiAlertSection
        {
            Icon = "⏰",
            Title = $"{FaNum(count)} قرارداد منقضی یا رو به اتمام (۳۰ روز)",
            Link = "/hr-core/contracts",
            LinkText = "قراردادها",
        };
        foreach (var c in items)
        {
            var end = c.EndDate!.Value.Date;
            var tail = end < today ? "منقضی شده ❌" : $"{FaNum((end - today).Days)} روز مانده";
            s.Lines.Add($"قرارداد {c.ContractNo} — پایان {AiDateUtil.ToFaShort(end)} ({tail})");
        }
        return s;
    }

    // ---------- ۶) یادآوری گزارش‌کار دیروز ----------
    private async Task<AiAlertSection?> ReportNudgeAsync(int userId, string? role, CancellationToken ct)
    {
        if (!await Can(userId, "ReportWorks", role, ct)) return null;
        var yesterday = DateTime.Today.AddDays(-1);
        var has = await _db.ReportWorks.AsNoTracking()
            .AnyAsync(r => r.UserId == userId && !r.IsDelete && r.ReportDate.Date == yesterday, ct);
        if (has) return null;
        return new AiAlertSection
        {
            Icon = "✍️",
            Title = "یادآوری گزارش‌کار",
            Lines = new() { "دیروز گزارش‌کاری ثبت نکردی؛ اگر کاری کردی ثبتش کن." },
            Link = "/report-works",
            LinkText = "گزارش‌کارها",
        };
    }

    private static string Truncate(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static string FaNum(int n) => AiTextUtil.ToFaDigits(n.ToString());

    private static string FmtQty(decimal q) =>
        AiTextUtil.ToFaDigits((q % 1 == 0 ? q.ToString("#,##0") : q.ToString("#,##0.0")));

    private static string FaMoney(decimal amount) =>
        AiTextUtil.ToFaDigits(amount.ToString("#,##0")) + " تومان";
}
