using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Cartable;

/// <summary>
/// زمینهٔ کاربر برای منابع کارتابل — یک‌بار در <see cref="CartableService"/> ساخته می‌شود
/// و همهٔ منابع همان را می‌گیرند (به‌جای N بار کوئری مجوزها).
/// </summary>
public sealed record CartableContext(
    int UserId,
    string UserName,
    IReadOnlySet<string> Permissions,
    bool IsAdmin,
    int? ReferrerId)
{
    public bool Has(string permission) =>
        IsAdmin || Permissions.Contains(permission);
}

/// <summary>
/// قرارداد منبع کارتابل.
/// <para>
/// هر ماژول آیتم‌های «منتظر اقدام من» و «اطلاع» خودش را معرفی می‌کند؛ افزودن ماژول
/// جدید هیچ تغییری در صفحهٔ کارتابل یا بقیهٔ ماژول‌ها لازم ندارد.
/// </para>
/// </summary>
public interface ICartableSource
{
    /// <summary>کلید یکتا — مثلاً sales-visit</summary>
    string Key { get; }

    Task<IEnumerable<CartableItemDto>> GetMineAsync(CartableContext ctx, CancellationToken ct = default);
}

public interface ICartableService
{
    Task<CartableResponseDto> GetMineAsync(CancellationToken ct = default);

    /// <summary>فقط شمارندهٔ آیتم‌های «منتظر اقدام» — برای badge منو زنگ سربرگ.</summary>
    Task<int> CountActionableAsync(CancellationToken ct = default);
}

public class CartableService : ICartableService
{
    private readonly IEnumerable<ICartableSource> _sources;
    private readonly AppDbContext _db;
    private readonly Inventory.Api.Services.Core.IEffectivePermissions _permissions;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<CartableService> _log;

    public CartableService(IEnumerable<ICartableSource> sources, AppDbContext db,
        Inventory.Api.Services.Core.IEffectivePermissions permissions,
        IHttpContextAccessor http, ILogger<CartableService> log)
    {
        _sources = sources;
        _db = db;
        _permissions = permissions;
        _http = http;
        _log = log;
    }

    private async Task<CartableContext?> BuildContextAsync()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        if (!int.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) || uid <= 0)
            return null;

        var perms = await _permissions.GetAsync(user);
        var isAdmin = await _permissions.IsLegacyAdminAsync(user);

        var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
        var name = string.IsNullOrWhiteSpace($"{dbUser?.FirstName} {dbUser?.LastName}".Trim())
            ? (dbUser?.Username ?? user.Identity?.Name ?? "")
            : $"{dbUser?.FirstName} {dbUser?.LastName}".Trim();

        return new CartableContext(uid, name, perms, isAdmin, dbUser?.ReferrerId);
    }

    public async Task<CartableResponseDto> GetMineAsync(CancellationToken ct = default)
    {
        var response = new CartableResponseDto();
        var ctx = await BuildContextAsync();
        if (ctx is null) return response;

        foreach (var source in _sources)
        {
            try
            {
                var items = await source.GetMineAsync(ctx, ct);
                response.Items.AddRange(items.Where(i => i is not null));
            }
            catch (Exception ex)
            {
                // خطای یک منبع، کارتابل بقیهٔ ماژول‌ها را نمی‌شکند.
                _log.LogWarning(ex, "منبع کارتابل {Key} خطا داد", source.Key);
            }
        }

        // اولویت‌دار و سپس تازه‌ترها
        response.Items = response.Items
            .OrderByDescending(i => i.Actionable)
            .ThenByDescending(i => i.Priority)
            .ThenBy(i => i.DueAt ?? DateTime.MaxValue)
            .ToList();

        return response;
    }

    public async Task<int> CountActionableAsync(CancellationToken ct = default)
    {
        var res = await GetMineAsync(ct);
        return res.ActionableCount;
    }
}

/// <summary>
/// منبع کارتابل ماژول ویزیتوری:
/// <list type="bullet">
/// <item>ویزیتور: سفارش برگشتی برای اصلاح، پیش‌نویس رهاشده</item>
/// <item>مدیر فروش: سفارش‌های ارسال‌شده در انتظار تأیید (+ مورد تخفیف بالای سقف)</item>
/// <item>مالی: سفارش تأییدشده در کارتابل مالی (ثبت فروش با نقد/چک/اقساط) و فروش‌های منتقل‌شده</item>
/// </list>
/// </summary>
public class SalesVisitCartableSource : ICartableSource
{
    private readonly AppDbContext _db;

    public SalesVisitCartableSource(AppDbContext db) => _db = db;

    public string Key => "sales-visit";

    public async Task<IEnumerable<CartableItemDto>> GetMineAsync(CartableContext ctx, CancellationToken ct = default)
    {
        var items = new List<CartableItemDto>();
        var canApprove = ctx.Has("SalesVisit.Approve");
        var canFinance = ctx.Has("SalesVisit.FinanceView");
        var canCreate = ctx.Has("SalesVisit.Create");

        // ---------------- ویزیتور ----------------
        if (canCreate && ctx.ReferrerId is > 0)
        {
            var myReturned = await _db.VisOrders.AsNoTracking()
                .Where(o => o.ReferrerId == ctx.ReferrerId && o.Status == VisOrderStatus.Returned)
                .CountAsync(ct);
            if (myReturned > 0)
                items.Add(new CartableItemDto
                {
                    Source = Key,
                    Kind = "returned",
                    Title = $"{Fa.Digits(myReturned)} سفارش برگشتی برای اصلاح",
                    Subtitle = "دلیل برگشت را ببینید و دوباره ارسال کنید",
                    Link = "/sales/visits?status=Returned",
                    Icon = "bi-arrow-return-right",
                    Color = "#b45309",
                    Priority = CartablePriority.High,
                    Actionable = true,
                    BadgeCount = myReturned
                });

            var myDrafts = await _db.VisOrders.AsNoTracking()
                .Where(o => o.ReferrerId == ctx.ReferrerId && o.Status == VisOrderStatus.Draft)
                .CountAsync(ct);
            if (myDrafts > 0)
                items.Add(new CartableItemDto
                {
                    Source = Key,
                    Kind = "draft",
                    Title = $"{Fa.Digits(myDrafts)} پیش‌نویس ارسال‌نشده",
                    Subtitle = "این سفارش‌ها هنوز به مدیر نرسیده‌اند",
                    Link = "/sales/visits?status=Draft",
                    Icon = "bi-pencil-square",
                    Color = "#64748b",
                    Priority = CartablePriority.Normal,
                    Actionable = true,
                    BadgeCount = myDrafts
                });
        }

        // ---------------- مدیر فروش ----------------
        if (canApprove)
        {
            var submitted = await _db.VisOrders.AsNoTracking()
                .Where(o => o.Status == VisOrderStatus.Submitted)
                .CountAsync(ct);
            if (submitted > 0)
                items.Add(new CartableItemDto
                {
                    Source = Key,
                    Kind = "approval-pending",
                    Title = $"{Fa.Digits(submitted)} سفارش ویزیتور منتظر تأیید شما",
                    Subtitle = "بررسی موجودی، سقف تخفیف و اعتبار مشتری",
                    Link = "/sales/approvals",
                    Icon = "bi-patch-check",
                    Color = "#1d4ed8",
                    Priority = CartablePriority.High,
                    Actionable = true,
                    BadgeCount = submitted
                });

            var overLimit = await _db.VisOrders.AsNoTracking()
                .Where(o => o.Status == VisOrderStatus.Submitted && o.DiscountOverLimit)
                .CountAsync(ct);
            if (overLimit > 0)
                items.Add(new CartableItemDto
                {
                    Source = Key,
                    Kind = "discount-over-limit",
                    Title = $"{Fa.Digits(overLimit)} سفارش با تخفیف بالای سقف",
                    Subtitle = "نیازمند تأیید ویژهٔ مدیر فروش",
                    Link = "/sales/approvals",
                    Icon = "bi-exclamation-triangle",
                    Color = "#be123c",
                    Priority = CartablePriority.Urgent,
                    Actionable = true,
                    BadgeCount = overLimit
                });
        }

        // ---------------- مالی ----------------
        if (canFinance)
        {
            // سفارش تأییدشدهٔ مدیر = یک قلم کارتابل مالی؛ با یک اقدام «فروش فروشگاه» ثبت می‌شود
            var approved = await _db.VisOrders.AsNoTracking()
                .Where(o => o.Status == VisOrderStatus.Approved)
                .CountAsync(ct);
            if (approved > 0)
                items.Add(new CartableItemDto
                {
                    Source = Key,
                    Kind = "finance-approved",
                    Title = $"{Fa.Digits(approved)} سفارش تأییدشده در کارتابل مالی",
                    Subtitle = "روش پرداخت (نقد/چک/اقساط) را ثبت و فروش را نهایی کنید",
                    Link = "/sales/finance-queue",
                    Icon = "bi-receipt-cutoff",
                    Color = "#0f766e",
                    Priority = CartablePriority.High,
                    Actionable = true,
                    BadgeCount = approved
                });

            var invoiced = await _db.VisOrders.AsNoTracking()
                .Where(o => o.Status == VisOrderStatus.Invoiced)
                .CountAsync(ct);
            if (invoiced > 0)
                items.Add(new CartableItemDto
                {
                    Source = Key,
                    Kind = "finance-sold",
                    Title = $"{Fa.Digits(invoiced)} سفارش به فروش فروشگاه منتقل شده",
                    Subtitle = "در فهرست «فروش» سامانه فروشگاهی قابل پیگیری است",
                    Link = "/orders/sale",
                    Icon = "bi-cart-check",
                    Color = "#1d4ed8",
                    Priority = CartablePriority.Normal,
                    Actionable = false,
                    BadgeCount = invoiced
                });
        }

        return items;
    }
}
