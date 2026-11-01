using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

// این نام‌ها هم در DTOهای مشترک و هم در انتیتی‌های API وجود دارند. قرارداد سرویس
// فاکتور (IInvoicingService) نسخهٔ DTO را می‌گیرد و برمی‌گرداند؛ پس همین‌ها را صریح می‌کنیم.
using FacInvoice = Inventory.Shared.Dtos.FacInvoice;
using FacInvoiceLine = Inventory.Shared.Dtos.FacInvoiceLine;
using PartyEntity = Inventory.Api.Data.Party;

namespace Inventory.Api.Services.SalesVisit;

/// <summary>
/// هویت و توان‌های کاربر جاری در ماژول ویزیتوری.
/// فقط در سرور ساخته می‌شود؛ کلاینت هیچ‌گاه آن را تعیین نمی‌کند.
/// </summary>
public sealed record VisActor(
    int UserId,
    string UserName,
    int? ReferrerId,
    string ReferrerName,
    bool IsAdmin,
    bool CanCreate,
    bool CanApprove,
    bool CanFinance,
    decimal MaxDiscountPercent)
{
    /// <summary>
    /// شرکت مؤثر کاربر (کمپانی). همهٔ فهرست‌های فروش و ویزیتوری — مشتری، انبار، کالا —
    /// بر اساس همین شرکت دیده می‌شوند. null یعنی زمینهٔ شرکتی تعیین نشده (نصب تک‌شرکتی).
    /// </summary>
    public int? CompanyId { get; init; }

    /// <summary>نام شرکت مؤثر — برای نمایش در فرم سفارش.</summary>
    public string CompanyName { get; init; } = "";

    /// <summary>دیدن همهٔ سفارش‌های شرکت (مدیر فروش/مالی) در مقابل فقط سفارش‌های خودش (ویزیتور).</summary>
    public bool CanSeeAll => IsAdmin || CanApprove || CanFinance;
}

/// <summary>سرویس سفارش ویزیتور — پیش‌سند فروش با چرخهٔ تأیید.</summary>
public interface IVisOrderService
{
    /// <summary>ساخت هویت ماژول برای کاربر جاری (از توکن + پایگاه‌داده).</summary>
    Task<VisActor> ResolveActorAsync(ClaimsPrincipal user);

    Task<List<VisOrderDto>> GetListAsync(VisActor actor, VisOrderStatus? status, int? partyId,
        DateTime? from, DateTime? to, string? search, int take = 200);

    Task<VisOrderDto?> GetAsync(VisActor actor, int id);

    Task<VisOrderDto> SaveAsync(VisActor actor, VisOrderCommand cmd);

    Task<VisOrderDto> SubmitAsync(VisActor actor, int id);

    /// <summary>تأیید سفارش + تعیین تاریخ نهایی تحویل (خالی = پذیرش تاریخ ویزیتور).</summary>
    Task<VisOrderDto> ApproveAsync(VisActor actor, int id, string? comment,
        bool createInvoiceDraft = true, DateTime? finalDeliveryDate = null);

    Task<VisOrderDto> ReturnAsync(VisActor actor, int id, string comment);

    Task<VisOrderDto> RejectAsync(VisActor actor, int id, string comment);

    Task<VisOrderDto> CancelAsync(VisActor actor, int id, string? comment);

    /// <summary>
    /// تأیید مالی: ثبت «فروش فروشگاه» از سفارش تأییدشده + سند خودکار + چک/اقساط.
    /// </summary>
    Task<VisOrderDto> FinanceApproveAsync(VisActor actor, int id, VisFinanceCommand cmd);

    /// <summary>ساخت (یا بازگردانی) پیش‌نویس فاکتور فروش از سفارش تأییدشده — گام مالی.</summary>
    Task<VisOrderDto> CreateInvoiceDraftAsync(VisActor actor, int id);

    /// <summary>همگام‌سازی وضعیت با فاکتور: قطعی → Invoiced، ابطال → Canceled.</summary>
    Task<VisOrderDto> SyncInvoiceStateAsync(VisActor actor, int id);

    /// <summary>صف تأیید مدیر فروش — با هشدار کسری موجودی و اعتبار مشتری.</summary>
    Task<List<VisApprovalItem>> ApprovalsQueueAsync(VisActor actor, int take = 100);

    /// <summary>صف مالی: سفارش‌های تأییدشده / فاکتورشده.</summary>
    Task<List<VisOrderDto>> FinanceQueueAsync(VisActor actor, int take = 200);

    Task<VisQueueStats> StatsAsync(VisActor actor);

    Task<List<VisCustomerLite>> CustomersAsync(VisActor actor, string? search, int take = 50);

    Task<VisCustomerLite> CreateCustomerAsync(VisActor actor, string name, string? phone, string? mobile, string? address);

    Task<List<VisProductLite>> ProductsAsync(VisActor actor, int? warehouseId, string? search, int take = 50);

    /// <summary>انبارهای فعالِ شرکتِ کاربر.</summary>
    Task<List<VisWarehouseLite>> WarehousesAsync(VisActor actor);

    /// <summary>پاک‌سازی سفارش‌های ارسال‌شدهٔ منقضی (کار زمان‌بندی‌شده/فراخوانی دستی).</summary>
    Task<int> ExpireStaleAsync(int expireAfterDays = 7);
}

public class VisOrderService : IVisOrderService
{
    private const string ModuleKey = "SalesVisit";
    private const string FormName = "سفارش ویزیتور";

    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _permissions;
    private readonly IInvoicingService _invoicing;
    private readonly IInventoryService _inventory;
    private readonly INotifyService _notify;
    private readonly ILogger<VisOrderService> _log;

    public VisOrderService(AppDbContext db, IEffectivePermissions permissions,
        IInvoicingService invoicing, IInventoryService inventory,
        INotifyService notify, ILogger<VisOrderService> log)
    {
        _db = db;
        _permissions = permissions;
        _invoicing = invoicing;
        _inventory = inventory;
        _notify = notify;
        _log = log;
    }

    // ==================================================================
    //  هویت کاربر
    // ==================================================================

    public async Task<VisActor> ResolveActorAsync(ClaimsPrincipal user)
    {
        var uid = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        var perms = uid > 0 ? await _permissions.GetAsync(user) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isAdmin = uid > 0 && await _permissions.IsLegacyAdminAsync(user);

        bool Has(string action) => isAdmin || perms.Contains($"{ModuleKey}.{action}");

        var dbUser = uid > 0
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid)
            : null;

        int? referrerId = dbUser?.ReferrerId;
        if (referrerId is not > 0 && int.TryParse(user.FindFirstValue("referrerId"), out var rid) && rid > 0)
            referrerId = rid;

        var referrer = referrerId is > 0
            ? await _db.Referrers.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == referrerId)
            : null;

        var name = string.IsNullOrWhiteSpace($"{dbUser?.FirstName} {dbUser?.LastName}".Trim())
            ? (dbUser?.Username ?? user.Identity?.Name ?? "")
            : $"{dbUser?.FirstName} {dbUser?.LastName}".Trim();

        var (companyId, companyName) = await ResolveCompanyAsync(uid, referrer?.Id, isAdmin);

        return new VisActor(
            UserId: uid,
            UserName: name,
            ReferrerId: referrer?.Id,
            ReferrerName: referrer?.Name ?? "",
            IsAdmin: isAdmin,
            CanCreate: Has("Create"),
            CanApprove: Has("Approve"),
            CanFinance: Has("FinanceView"),
            MaxDiscountPercent: referrer?.MaxDiscountPercent ?? 0m)
        {
            CompanyId = companyId,
            CompanyName = companyName
        };
    }

    // ==================================================================
    //  شرکت (کمپانی) مؤثر — پایهٔ دیدن مشتری/انبار/کالا
    // ==================================================================

    /// <summary>
    /// شرکت مؤثر کاربر، به ترتیب اولویت:
    /// <list type="number">
    /// <item>شرکت فعالِ درخواست (کمبوی بالای برنامه / هدر <c>X-Company-Id</c>) — انتخاب صریح کاربر.</item>
    /// <item>شرکت پیش‌فرض ثبت‌شده برای کاربر در «دسترسی شرکت‌های کاربر».</item>
    /// <item>شرکت روی خود کاربر (دادهٔ نسخهٔ قدیمی).</item>
    /// <item>شرکت معرفِ ویزیتور — ویزیتوری که در یک کمپانی قرار گرفته، مشتری/انبار/کالای همان کمپانی را می‌بیند.</item>
    /// <item>نصب تک‌شرکتی (فقط یک شرکت فعال) — همان شرکت.</item>
    /// <item>در غیر این صورت null = زمینه تعیین نشده → هیچ فیلتری اعمال نمی‌شود (سازگاری با دادهٔ قدیمی).</item>
    /// </list>
    /// نکتهٔ مهم: اگر شرکت فعال ۰ باشد (کاربر به هیچ شرکتی دسترسی ندارد) دیگر مثل قبل همه‌چیز
    /// «خالی» نمی‌شود؛ شرکتِ معرف/کاربر جایگزین می‌شود تا ویزیتور کالا و مشتری خودش را ببیند.
    /// </summary>
    private async Task<(int? Id, string Name)> ResolveCompanyAsync(int userId, int? referrerId, bool isAdmin)
    {
        async Task<(int?, string)> NameOf(int id)
        {
            var name = await _db.SystemCompanies.AsNoTracking()
                .Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync() ?? "";
            return ((int?)id, name);
        }

        // ۱) شرکت فعالِ درخواست
        var active = _db.ActiveCompanyId;
        if (active is > 0) return await NameOf(active.Value);

        // ۲) دسترسی‌های ثبت‌شدهٔ کاربر (پیش‌فرض، وگرنه اولین شرکت مجاز)
        var byAccess = await _db.UserCompanyAccesses.AsNoTracking()
            .Where(x => x.UserId == userId && x.Company.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Company.Name).ThenBy(x => x.CompanyId)
            .Select(x => (int?)x.CompanyId).FirstOrDefaultAsync();
        if (byAccess is > 0) return await NameOf(byAccess.Value);

        // ۳) شرکت روی خود کاربر (نسخهٔ قدیمی) — در جدول کاربران سیستم
        var username = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(username))
        {
            var legacy = await _db.SystemUsers.AsNoTracking()
                .Where(u => u.Username == username && u.CompanyId != null)
                .Select(u => u.CompanyId).FirstOrDefaultAsync();
            if (legacy is > 0) return await NameOf(legacy.Value);
        }

        // ۴) شرکت معرفِ ویزیتور
        if (referrerId is > 0)
        {
            var refCompany = await _db.Referrers.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.Id == referrerId && r.CompanyId != null)
                .Select(r => r.CompanyId).FirstOrDefaultAsync();
            if (refCompany is > 0) return await NameOf(refCompany.Value);
        }

        // ۵) نصب تک‌شرکتی
        if (isAdmin) return (null, "");
        var companies = await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => c.Id).Take(2).ToListAsync();
        if (companies.Count == 1) return await NameOf(companies[0]);

        return (null, "");
    }

    /// <summary>
    /// قاعدهٔ دیدن دادهٔ شرکت: رکوردهای همان شرکت + رکوردهای بدون شرکت (میراثی/مشترک).
    /// بدون این قاعده، دادهٔ قدیمیِ قبل از فعال‌شدن چندشرکتی برای همه نامرئی می‌شد
    /// (دلیل «ویزیتور هیچ کالایی نمی‌بیند»).
    /// </summary>
    private static bool InCompany(int? recordCompanyId, int? companyId)
        => companyId is not > 0 || recordCompanyId is null || recordCompanyId == companyId;

    // ==================================================================
    //  خواندن
    // ==================================================================

    public async Task<List<VisOrderDto>> GetListAsync(VisActor actor, VisOrderStatus? status, int? partyId,
        DateTime? from, DateTime? to, string? search, int take = 200)
    {
        var q = ScopedQuery(actor);

        if (status is not null) q = q.Where(o => o.Status == status);
        if (partyId is > 0) q = q.Where(o => o.PartyId == partyId);
        if (from is not null) q = q.Where(o => o.Date >= from.Value.Date);
        if (to is not null) q = q.Where(o => o.Date <= to.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => o.Number.Contains(s) || o.PartyName.Contains(s) || o.ReferrerName.Contains(s)
                             || (o.Note != null && o.Note.Contains(s)));
        }

        var list = await q.Include(o => o.Lines).AsNoTracking()
            .OrderByDescending(o => o.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        var dtos = list.Select(ToDto).ToList();
        await FillSaleNumbersAsync(dtos);
        return dtos;
    }

    public async Task<VisOrderDto?> GetAsync(VisActor actor, int id)
    {
        var order = await ScopedQuery(actor)
            .Include(o => o.Lines)
            .Include(o => o.History)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return null;
        var dto = ToDto(order);
        dto.History = order.History.OrderByDescending(h => h.At).Select(ToHistoryDto).ToList();
        await FillSaleNumbersAsync(new List<VisOrderDto> { dto });
        return dto;
    }

    /// <summary>محدودهٔ داده: ویزیتور فقط سفارش‌های خودش، مدیر/مالی همهٔ شرکت.</summary>
    private IQueryable<VisOrder> ScopedQuery(VisActor actor)
    {
        var q = _db.VisOrders.AsQueryable();
        if (actor.CanSeeAll) return q;
        var myRef = actor.ReferrerId ?? 0;
        return q.Where(o => o.ReferrerId == myRef);
    }

    // ==================================================================
    //  ثبت / ویرایش پیش‌نویس
    // ==================================================================

    public async Task<VisOrderDto> SaveAsync(VisActor actor, VisOrderCommand cmd)
    {
        if (!actor.CanCreate && !actor.CanApprove)
            throw new InvalidOperationException("شما مجوز ثبت سفارش ویزیتوری را ندارید.");
        if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک سطر کالا وارد کنید.");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");
        if (cmd.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2)))
            throw new InvalidOperationException("مقدار هر سطر حداکثر تا دو رقم اعشار قابل ثبت است.");
        if (cmd.Lines.Any(l => l.UnitPrice < 0)) throw new InvalidOperationException("قیمت واحد نمی‌تواند منفی باشد.");
        if (cmd.Lines.Any(l => l.DiscountPercent is < 0 or > 100))
            throw new InvalidOperationException("درصد تخفیف باید بین ۰ تا ۱۰۰ باشد.");
        if (cmd.WarehouseId <= 0) throw new InvalidOperationException("انبار را انتخاب کنید.");
        if (cmd.PartyId <= 0) throw new InvalidOperationException("مشتری را انتخاب کنید.");

        var party = await _db.Parties.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == cmd.PartyId)
                    ?? throw new InvalidOperationException("مشتری انتخاب‌شده یافت نشد؛ از فهرست مشتری‌ها دوباره انتخاب کنید.");
        var warehouse = await _db.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == cmd.WarehouseId)
                        ?? throw new InvalidOperationException("انبار انتخاب‌شده یافت نشد.");

        // محدودهٔ شرکت: مشتری و انبار باید از شرکت همین کاربر باشند
        if (!InCompany(party.CompanyId, actor.CompanyId))
            throw new InvalidOperationException("این مشتری مربوط به شرکت دیگری است و در سفارش این شرکت قابل انتخاب نیست.");
        if (!InCompany(warehouse.CompanyId, actor.CompanyId))
            throw new InvalidOperationException("این انبار مربوط به شرکت دیگری است؛ انبار شرکت خودتان را انتخاب کنید.");

        // محدودهٔ مشتری همان محدودهٔ شرکت است (هم‌راستا با فهرستی که کاربر می‌بیند):
        // هر مشتری فعالِ شرکت قابل انتخاب است؛ مشتری شرکت دیگر یا غیرفعال رد می‌شود.
        if (party.Type != PartyType.Customer)
            throw new InvalidOperationException("طرف‌حساب انتخاب‌شده مشتری نیست؛ از فهرست مشتری‌ها انتخاب کنید.");
        if (!party.IsActive)
            throw new InvalidOperationException("این مشتری غیرفعال است؛ از فهرست مشتری‌های فعال انتخاب کنید.");

        var productIds = cmd.Lines.Select(l => l.ProductId).Where(id => id > 0).Distinct().ToList();
        var products = await _db.Products.IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id) && (actor.CompanyId == null
                        || p.CompanyId == null || p.CompanyId == actor.CompanyId))
            .ToDictionaryAsync(p => p.Id);
        if (products.Count != productIds.Count)
            throw new InvalidOperationException("یکی از کالاهای انتخاب‌شده یافت نشد؛ فهرست کالاها را دوباره بارگذاری کنید.");

        VisOrder entity;
        var isNew = cmd.Id == 0;
        // ویرایش سفارشِ ارسال‌شده (پیش از تأیید مدیر) باید «ارسال‌شده» بماند و مدیر بداند تغییر کرده است
        var wasSubmitted = false;
        if (isNew)
        {
            var referrerId = actor.ReferrerId ?? party.ReferrerId ?? 0;
            var referrerName = await _db.Referrers.IgnoreQueryFilters().Where(r => r.Id == referrerId)
                .Select(r => r.Name).FirstOrDefaultAsync() ?? actor.ReferrerName;

            entity = new VisOrder
            {
                Number = await GenerateNumberAsync(),
                CreatedAt = DateTime.Now,
                CreatedByUserId = actor.UserId,
                CreatedByName = actor.UserName,
                ReferrerId = referrerId,
                ReferrerName = referrerName,
                Status = VisOrderStatus.Draft
            };
            _db.VisOrders.Add(entity);
        }
        else
        {
            entity = await _db.VisOrders.Include(o => o.Lines).Include(o => o.History)
                .FirstOrDefaultAsync(o => o.Id == cmd.Id)
                ?? throw new InvalidOperationException("سفارش یافت نشد.");

            if (!actor.CanSeeAll && entity.ReferrerId != (actor.ReferrerId ?? 0))
                throw new InvalidOperationException("این سفارش متعلق به شما نیست.");

            // پنجرهٔ ویرایش: ویزیتور تا قبل از تأیید مدیر (پیش‌نویس، ارسال‌شده، برگشتی) حق ویرایش دارد؛
            // مدیر (Approve/Admin) در کارتابل و پنل خود در وضعیت تأییدشده هم می‌تواند تخفیف یا اقلام را اصلاح کند (تا قبل از فاکتورشدن نهایی).
            var allowedStatus = (actor.CanApprove || actor.IsAdmin)
                ? (entity.Status is VisOrderStatus.Draft or VisOrderStatus.Submitted or VisOrderStatus.Returned or VisOrderStatus.Approved)
                : (entity.Status is VisOrderStatus.Draft or VisOrderStatus.Submitted or VisOrderStatus.Returned);

            if (!allowedStatus)
                throw new InvalidOperationException(entity.Status switch
                {
                    VisOrderStatus.Approved => "این سفارش تأیید شده و فقط توسط مدیر قابل ویرایش است.",
                    VisOrderStatus.Invoiced => "این سفارش فاکتور و ثبت فروش شده و قابل ویرایش نیست.",
                    VisOrderStatus.Rejected => "این سفارش رد شده است؛ برای ثبت مجدد، سفارش جدید بسازید.",
                    VisOrderStatus.Canceled => "این سفارش ابطال شده و قابل ویرایش نیست.",
                    _ => "این سفارش در وضعیتی نیست که قابل ویرایش باشد."
                });

            wasSubmitted = entity.Status == VisOrderStatus.Submitted;

            _db.VisOrderLines.RemoveRange(entity.Lines);
            entity.Lines.Clear();
        }

        entity.Date = cmd.Date == default ? DateTime.Now : cmd.Date;

        // تاریخ دریافت محصول: پیش‌فرض امروز. اگر مدیر قبلاً تاریخ نهایی گذاشته و ویزیتور
        // دوباره ویرایش می‌کند، تاریخ نهایی دست نمی‌خورد (بازنویسی نمی‌شود).
        entity.DeliveryDate = (cmd.DeliveryDate ?? entity.DeliveryDate)?.Date ?? DateTime.Today;
        entity.PartyId = party.Id;
        entity.PartyName = party.Name;
        entity.WarehouseId = warehouse.Id;
        entity.WarehouseName = warehouse.Name;
        entity.PaymentMethod = cmd.PaymentMethod;
        entity.Note = string.IsNullOrWhiteSpace(cmd.Note) ? null : cmd.Note.Trim();

        var row = 1;
        foreach (var l in cmd.Lines)
        {
            var p = products[l.ProductId];
            if (p.IsService && l.Quantity <= 0)
                throw new InvalidOperationException($"مقدار خدمت «{p.Name}» باید بزرگ‌تر از صفر باشد.");

            var gross = l.Quantity * l.UnitPrice;

            // تخفیف دو حالته: ورودی تا ۹۹ درصد است و از ۱۰۰ به بالا مبلغ ریالی.
            // (قاعدهٔ فرم؛ کلاینت همان را اعمال می‌کند و اینجا هم مستقل بازمحاسبه می‌شود
            //  تا فراخوانی مستقیم API نتواند مبلغی بیش از سطر تخفیف بزند.)
            var isAmount = l.DiscountIsAmount;
            var pct = Math.Clamp(l.DiscountPercent, 0, 99);
            decimal discount;
            if (isAmount)
            {
                discount = Math.Clamp(Math.Round(l.DiscountAmount, 0), 0, Math.Round(gross, 0));
                pct = gross > 0 ? Math.Round(discount * 100m / gross, 2) : 0;
            }
            else
            {
                discount = Math.Round(gross * pct / 100m, 0);
            }

            entity.Lines.Add(new VisOrderLine
            {
                RowNo = row++,
                ProductId = p.Id,
                ProductCode = p.Code,
                ProductName = p.Name,
                Unit = p.Unit,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountPercent = pct,
                DiscountAmount = discount,
                DiscountIsAmount = isAmount,
                LineTotal = gross - discount,
                Note = string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim()
            });
        }

        Recalculate(entity);

        // کنترل سقف تخفیف ویزیتور: ویزیتور نمی‌تواند بیش از سقف تعیین‌شده توسط مدیر تخفیف دهد، ولی مدیر/ادمین محدودیت ندارد
        var cap = await _db.Referrers.IgnoreQueryFilters().Where(r => r.Id == entity.ReferrerId)
            .Select(r => r.MaxDiscountPercent).FirstOrDefaultAsync();
        entity.DiscountOverLimit = cap > 0 && entity.Lines.Any(l => l.DiscountPercent > cap);

        if (!actor.CanApprove && !actor.IsAdmin && entity.DiscountOverLimit)
        {
            throw new InvalidOperationException($"درصد تخفیف واردشده بیش از سقف مجاز ویزیتور ({cap}٪) است.");
        }

        await _db.SaveChangesAsync();

        // پروندهٔ مشتری: آخرین سفارش
        await TouchCustomerLinkAsync(entity);

        if (cmd.SubmitAfterSave)
        {
            // ثبت/ویرایش + ارسال برای تأیید (اعلان ارسال داخل SubmitAsync انجام می‌شود)
            if (!isNew)
            {
                AddHistory(entity, entity.Status, entity.Status, actor,
                    wasSubmitted ? "ویرایش سفارش در انتظار تأیید و ارسال دوباره" : "ویرایش و ارسال برای تأیید");
                await _db.SaveChangesAsync();
            }
            return await SubmitAsync(actor, entity.Id);
        }

        // ذخیرهٔ پیش‌نویس (بدون ارسال): مدیر فروش هم در جریان قرار می‌گیرد
        if (isNew)
        {
            AddHistory(entity, VisOrderStatus.Draft, VisOrderStatus.Draft, actor, "پیش‌نویس سفارش ثبت شد");
            await _db.SaveChangesAsync();
            await NotifyOrderEventAsync(entity, VisOrderEvent.DraftCreated, actor);
        }
        else
        {
            AddHistory(entity, entity.Status, entity.Status, actor,
                wasSubmitted ? "ویرایش سفارش ارسال‌شده (پیش از تأیید مدیر)" : "ویرایش پیش‌نویس");
            await _db.SaveChangesAsync();
            await NotifyOrderEventAsync(entity, wasSubmitted ? VisOrderEvent.ModifiedAfterSubmit : VisOrderEvent.DraftUpdated, actor);
        }
        await SafeBroadcastAsync(entity);

        return (await GetAsync(actor, entity.Id))!;
    }

    // ==================================================================
    //  چرخهٔ وضعیت
    // ==================================================================

    public async Task<VisOrderDto> SubmitAsync(VisActor actor, int id)
    {
        var order = await LoadOwnedAsync(actor, id);
        if (order.Status is not (VisOrderStatus.Draft or VisOrderStatus.Returned))
            throw new InvalidOperationException("این سفارش قبلاً ارسال شده است.");
        if (order.Lines.Count == 0) throw new InvalidOperationException("سفارش بدون سطر کالا قابل ارسال نیست.");

        var from = order.Status;
        order.Status = VisOrderStatus.Submitted;
        order.SubmittedAt = DateTime.Now;
        order.DecidedAt = null;
        order.DecidedBy = null;
        order.DecisionNote = null;
        order.ExpiresAt = DateTime.Now.AddDays(7);

        AddHistory(order, from, VisOrderStatus.Submitted, actor, "ارسال برای تأیید مدیر فروش");
        await _db.SaveChangesAsync();

        await NotifyOrderEventAsync(order, VisOrderEvent.Submitted, actor);
        await SafeBroadcastAsync(order);

        return (await GetAsync(actor, id))!;
    }

    public async Task<VisOrderDto> ApproveAsync(VisActor actor, int id, string? comment,
        bool createInvoiceDraft = true, DateTime? finalDeliveryDate = null)
    {
        if (!actor.CanApprove) throw new InvalidOperationException("شما مجوز تأیید سفارش ویزیتور را ندارید.");

        var order = await _db.VisOrders.Include(o => o.Lines).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new InvalidOperationException("سفارش یافت نشد.");
        if (order.Status != VisOrderStatus.Submitted)
            throw new InvalidOperationException("فقط سفارش ارسال‌شده قابل تأیید است.");

        var from = order.Status;
        order.Status = VisOrderStatus.Approved;
        order.DecidedAt = DateTime.Now;
        order.DecidedBy = actor.UserName;
        order.DecisionNote = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        // ---------- تاریخ نهایی تحویل ----------
        // مدیر می‌تواند تاریخ ویزیتور را بپذیرد یا اصلاح کند. تاریخ ویزیتور در
        // DeliveryDate دست‌نخورده می‌ماند و تصمیم مدیر در FinalDeliveryDate ثبت می‌شود
        // تا هر دو برای پیگیری در سابقه باشد.
        var visitorDate = order.DeliveryDate ?? order.Date.Date;
        var chosen = (finalDeliveryDate ?? visitorDate).Date;
        order.FinalDeliveryDate = chosen;
        order.FinalDateSetAt = DateTime.Now;
        order.FinalDateSetBy = actor.UserName;

        var dateComment = chosen == visitorDate.Date
            ? $"تاریخ تحویل نهایی: {PersianDate.ToShortFa(chosen)} (مطابق تاریخ ویزیتور)"
            : $"تاریخ تحویل نهایی: {PersianDate.ToShortFa(chosen)} — اصلاح‌شده از تاریخ ویزیتور {PersianDate.ToShortFa(visitorDate.Date)}";
        var historyComment = string.IsNullOrWhiteSpace(order.DecisionNote)
            ? dateComment
            : $"{order.DecisionNote} — {dateComment}";

        AddHistory(order, from, VisOrderStatus.Approved, actor, historyComment);
        await _db.SaveChangesAsync();

        // گام مالی: ساخت پیش‌نویس فاکتور. اگر پیکربندی فاکتور کامل نباشد، تأیید باطل نمی‌شود
        // و مالی می‌تواند از صف مالی دوباره تلاش کند (خطا در گردش ثبت می‌شود).
        if (createInvoiceDraft)
        {
            try
            {
                await BuildInvoiceDraftAsync(actor, order);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "ساخت پیش‌نویس فاکتور برای سفارش {Number} ناموفق بود", order.Number);
                order.History.Add(new VisOrderHistory
                {
                    OrderId = order.Id,
                    FromStatus = VisOrderStatus.Approved,
                    ToStatus = VisOrderStatus.Approved,
                    UserId = actor.UserId,
                    UserName = actor.UserName,
                    At = DateTime.Now,
                    Comment = $"پیش‌نویس فاکتور ساخته نشد: {ex.Message}"
                });
                await _db.SaveChangesAsync();
            }
        }

        await NotifyOrderEventAsync(order, VisOrderEvent.Approved, actor);
        await SafeBroadcastAsync(order);
        return (await GetAsync(actor, id))!;
    }

    public async Task<VisOrderDto> ReturnAsync(VisActor actor, int id, string comment)
    {
        if (!actor.CanApprove) throw new InvalidOperationException("شما مجوز برگشت سفارش را ندارید.");
        if (string.IsNullOrWhiteSpace(comment)) throw new InvalidOperationException("دلیل برگشت الزامی است.");

        var order = await _db.VisOrders.Include(o => o.History).FirstOrDefaultAsync(o => o.Id == id)
                    ?? throw new InvalidOperationException("سفارش یافت نشد.");
        if (order.Status != VisOrderStatus.Submitted)
            throw new InvalidOperationException("فقط سفارش ارسال‌شده قابل برگشت است.");

        var from = order.Status;
        order.Status = VisOrderStatus.Returned;
        order.ReturnCount += 1;
        order.DecidedAt = DateTime.Now;
        order.DecidedBy = actor.UserName;
        order.DecisionNote = comment.Trim();

        AddHistory(order, from, VisOrderStatus.Returned, actor, order.DecisionNote);
        await _db.SaveChangesAsync();

        await NotifyOrderEventAsync(order, VisOrderEvent.Returned, actor);
        await SafeBroadcastAsync(order);

        return (await GetAsync(actor, id))!;
    }

    public async Task<VisOrderDto> RejectAsync(VisActor actor, int id, string comment)
    {
        if (!actor.CanApprove) throw new InvalidOperationException("شما مجوز رد سفارش را ندارید.");
        if (string.IsNullOrWhiteSpace(comment)) throw new InvalidOperationException("دلیل رد الزامی است.");

        var order = await _db.VisOrders.Include(o => o.History).FirstOrDefaultAsync(o => o.Id == id)
                    ?? throw new InvalidOperationException("سفارش یافت نشد.");
        if (order.Status is not (VisOrderStatus.Submitted or VisOrderStatus.Returned))
            throw new InvalidOperationException("این سفارش در وضعیتی نیست که قابل رد باشد.");

        var from = order.Status;
        order.Status = VisOrderStatus.Rejected;
        order.DecidedAt = DateTime.Now;
        order.DecidedBy = actor.UserName;
        order.DecisionNote = comment.Trim();

        AddHistory(order, from, VisOrderStatus.Rejected, actor, order.DecisionNote);
        await _db.SaveChangesAsync();

        await NotifyOrderEventAsync(order, VisOrderEvent.Rejected, actor);
        await SafeBroadcastAsync(order);

        return (await GetAsync(actor, id))!;
    }

    public async Task<VisOrderDto> CancelAsync(VisActor actor, int id, string? comment)
    {
        var order = await _db.VisOrders.Include(o => o.History).FirstOrDefaultAsync(o => o.Id == id)
                    ?? throw new InvalidOperationException("سفارش یافت نشد.");

        var isOwner = order.ReferrerId == (actor.ReferrerId ?? 0) || order.CreatedByUserId == actor.UserId;
        if (!isOwner && !actor.CanApprove && !actor.IsAdmin)
            throw new InvalidOperationException("این سفارش متعلق به شما نیست.");
        if (order.Status is VisOrderStatus.Invoiced or VisOrderStatus.Closed or VisOrderStatus.Canceled)
            throw new InvalidOperationException("سفارش فاکتورشده یا بسته‌شده قابل ابطال نیست.");

        // ویزیتور فقط پیش از تأیید مدیر می‌تواند سفارش را ابطال کند؛ از «تأییدشده» به بعد
        // سفارش در اختیار مدیر/مالی است (برای جلوگیری از ابطال سفارشی که در صف تسویه است).
        if (!actor.CanApprove && !actor.IsAdmin && order.Status is not
            (VisOrderStatus.Draft or VisOrderStatus.Submitted or VisOrderStatus.Returned))
            throw new InvalidOperationException("سفارش تأییدشده قابل ابطال توسط ویزیتور نیست؛ با مدیر فروش تماس بگیرید.");

        var from = order.Status;
        order.Status = VisOrderStatus.Canceled;
        order.DecidedAt = DateTime.Now;
        order.DecidedBy = actor.UserName;
        order.DecisionNote = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        AddHistory(order, from, VisOrderStatus.Canceled, actor, order.DecisionNote);
        await _db.SaveChangesAsync();
        await NotifyOrderEventAsync(order, VisOrderEvent.Canceled, actor);
        await SafeBroadcastAsync(order);

        return (await GetAsync(actor, id))!;
    }

    // ==================================================================
    //  گام مالی
    // ==================================================================

    /// <summary>
    /// گام مالی نهایی: سفارش تأییدشدهٔ مدیر به **سند فروش فروشگاه** تبدیل می‌شود.
    /// <para>
    /// ۱) سند فروش با اقلام سفارش (قیمت خالص پس از تخفیف سطر) و روش پرداختِ انتخاب مالی ثبت می‌شود؛
    /// چک‌ها و دفترچهٔ اقساط مثل ماژول فروش ساخته می‌شوند و موجودی انبار کم می‌شود.<br/>
    /// ۲) سند حسابداری خودکار زده می‌شود: نقدی → صندوق/بانک، چک و اقساط → حساب دریافتنی.<br/>
    /// ۳) سفارش «فاکتورشده» می‌شود و شمارهٔ فروش روی آن ثبت می‌گردد — بدون هیچ دکمهٔ همگام‌سازی.
    /// </para>
    /// </summary>
    public async Task<VisOrderDto> FinanceApproveAsync(VisActor actor, int id, VisFinanceCommand cmd)
    {
        if (!actor.CanFinance && !actor.CanApprove)
            throw new InvalidOperationException("شما مجوز تأیید مالی سفارش را ندارید.");

        var order = await _db.VisOrders.Include(o => o.Lines).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new InvalidOperationException("سفارش یافت نشد.");

        if (order.Status == VisOrderStatus.Invoiced)
            throw new InvalidOperationException("برای این سفارش قبلاً فروش ثبت شده است.");
        if (order.Status != VisOrderStatus.Approved)
            throw new InvalidOperationException("فقط سفارش تأییدشدهٔ مدیر قابل تأیید مالی است.");
        if (order.Lines.Count == 0)
            throw new InvalidOperationException("سفارش سطر کالا ندارد.");

        // قیمت واحد پس از تخفیف سطر — سند فروش ستون تخفیف ندارد، پس تخفیف در فی واحد نشسته می‌شود
        var lines = order.Lines
            .OrderBy(l => l.RowNo)
            .Select(l => new OrderLineInput
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                Price = l.Quantity > 0
                    ? Math.Round((l.Quantity * l.UnitPrice - l.DiscountAmount) / l.Quantity, 4)
                    : l.UnitPrice
            })
            .ToList();

        var saleCmd = new OrderCommand
        {
            Type = TransactionType.Sale,
            Date = order.Date,
            WarehouseId = order.WarehouseId,
            PartyId = order.PartyId,
            ReferrerId = order.ReferrerId > 0 ? order.ReferrerId : null,
            Description = $"سفارش ویزیتور {order.Number}" +
                          (string.IsNullOrWhiteSpace(cmd.Comment) ? "" : $" — {cmd.Comment!.Trim()}"),
            PaymentMethod = cmd.PaymentMethod,
            CashType = cmd.CashType,
            TrsAccountId = cmd.TrsAccountId,
            CashAmount = cmd.PaymentMethod == PaymentMethod.Cash ? 0 : cmd.CashAmount,
            DueDate = cmd.DueDate,
            Cheques = cmd.Cheques ?? new(),
            Installments = cmd.Installments ?? new(),
            Lines = lines
        };

        // ---------- ۱) سند فروش فروشگاه (موجودی، چک‌ها، اقساط) ----------
        var sale = await _inventory.CreateOrderAsync(saleCmd);

        // ---------- ۲) سند حسابداری خودکار ----------
        int? voucherId = null;
        try
        {
            voucherId = await _invoicing.PostStoreSaleVoucherAsync(sale.Id, actor.UserName);
        }
        catch (Exception ex)
        {
            // تراکنش نباید نیمه‌کاره بماند: سند فروش برمی‌گردد و خطا برای مالی نمایش داده می‌شود
            try { await _inventory.DeleteOrderAsync(sale.Id); } catch { /* ignore */ }
            _log.LogError(ex, "صدور سند حسابداری فروش سفارش {Number} ناموفق بود", order.Number);
            throw new InvalidOperationException($"ثبت فروش انجام نشد: {ex.Message}");
        }

        // ---------- ۳) اتصال سفارش به فروش ----------
        var from = order.Status;
        order.InvoiceId = sale.Id;              // شناسهٔ سند فروش فروشگاه
        order.InvoiceNumber = sale.Number;      // شمارهٔ فروش
        order.Status = VisOrderStatus.Invoiced;
        order.DecidedAt ??= DateTime.Now;

        // روش پرداخت مؤثر را از خود سند فروش می‌خوانیم (ممکن است ترکیبی شده باشد)
        var effChequeSum = (sale.Cheques ?? new()).Sum(c => c.Amount);
        var effInstSum = (sale.Installments ?? new()).Sum(i => i.Amount);
        var effCash = sale.PaymentMethod == PaymentMethod.Cash ? sale.TotalAmount : sale.CashAmount;
        var effCredit = Math.Max(0, sale.TotalAmount - effCash - effChequeSum - effInstSum);
        var payText = PaymentMix.Describe(sale.PaymentMethod, effCash, effChequeSum, effInstSum, effCredit);
        order.PaymentMethod = sale.PaymentMethod;

        var comment = $"تأیید مالی — فروش {sale.Number} ثبت شد ({payText})" +
                      (voucherId is null ? " — سند حسابداری خودکار صادر نشد" : " — سند حسابداری صادر شد") +
                      (string.IsNullOrWhiteSpace(cmd.Comment) ? "" : $" — {cmd.Comment!.Trim()}");
        AddHistory(order, from, VisOrderStatus.Invoiced, actor, comment);
        await _db.SaveChangesAsync();
        await SafeBroadcastAsync(order);

        await NotifyOrderEventAsync(order, VisOrderEvent.Sold, actor, $"فروش {sale.Number}");

        return (await GetAsync(actor, id))!;
    }

    private static string PayTitle(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Installment => "اقساطی",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "—"
    };

    public async Task<VisOrderDto> CreateInvoiceDraftAsync(VisActor actor, int id)
    {
        if (!actor.CanFinance && !actor.CanApprove)
            throw new InvalidOperationException("شما مجوز اقدام مالی روی سفارش را ندارید.");

        var order = await _db.VisOrders.Include(o => o.Lines).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new InvalidOperationException("سفارش یافت نشد.");
        if (order.Status is not (VisOrderStatus.Approved or VisOrderStatus.Invoiced))
            throw new InvalidOperationException("فقط سفارش تأییدشده قابل تبدیل به فاکتور است.");

        if (order.InvoiceId is > 0)
            return (await GetAsync(actor, id))!;   // پیش‌نویس قبلاً ساخته شده است

        await BuildInvoiceDraftAsync(actor, order);
        await _db.SaveChangesAsync();
        await NotifyOrderEventAsync(order, VisOrderEvent.InvoiceDraftCreated, actor);
        await SafeBroadcastAsync(order);

        return (await GetAsync(actor, id))!;
    }

    /// <summary>ساخت پیش‌نویس فاکتور فروش از سفارش — بدون قطعی‌سازی (اثر انبار/حسابداری ندارد).</summary>
    private async Task BuildInvoiceDraftAsync(VisActor actor, VisOrder order)
    {
        if (order.Lines.Count == 0) throw new InvalidOperationException("سفارش سطر کالا ندارد.");

        var lines = new List<FacInvoiceLine>();
        foreach (var l in order.Lines)
        {
            FacInvoiceLine line;
            try
            {
                // قیمت، نرخ مالیات و کد مالیاتی پیش‌فرض از قواعد ماژول فاکتور
                line = await _invoicing.BuildLineAsync(l.ProductId, InvoiceKind.Sale, order.WarehouseId);
            }
            catch
            {
                line = new FacInvoiceLine { ProductId = l.ProductId, VatRate = 0 };
            }

            line.ProductId = l.ProductId;
            line.Quantity = l.Quantity;
            line.UnitPrice = l.UnitPrice;          // قیمت توافق‌شدهٔ ویزیتور ملاک است
            line.DiscountPercent = l.DiscountPercent;
            line.Discount = 0;                      // از درصد محاسبه می‌شود
            line.Description = l.Note;
            lines.Add(line);
        }

        var invoice = new FacInvoice
        {
            Kind = InvoiceKind.Sale,
            WarehouseId = order.WarehouseId,
            PartyId = order.PartyId,
            Date = DateTime.Now.Date,
            Settlement = order.PaymentMethod == PaymentMethod.Cash ? SettlementType.Cash : SettlementType.Credit,
            Description = $"سفارش ویزیتور {order.Number} — {order.PartyName}",
            Lines = lines
        };

        var saved = await _invoicing.SaveInvoiceAsync(invoice, actor.UserName);

        order.InvoiceId = saved.Id;
        order.InvoiceNumber = saved.Number.ToString();
        order.History.Add(new VisOrderHistory
        {
            OrderId = order.Id,
            FromStatus = order.Status,
            ToStatus = order.Status,
            UserId = actor.UserId,
            UserName = actor.UserName,
            At = DateTime.Now,
            Comment = $"پیش‌نویس فاکتور شمارهٔ {saved.Number} ساخته شد"
        });
        await _db.SaveChangesAsync();
    }

    public async Task<VisOrderDto> SyncInvoiceStateAsync(VisActor actor, int id)
    {
        var order = await _db.VisOrders.Include(o => o.History).FirstOrDefaultAsync(o => o.Id == id)
                    ?? throw new InvalidOperationException("سفارش یافت نشد.");
        if (order.InvoiceId is not > 0) return (await GetAsync(actor, id))!;

        var invoice = await _db.FacInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.InvoiceId);
        if (invoice is null) return (await GetAsync(actor, id))!;

        var from = order.Status;
        if (invoice.Status == InvoiceStatus.Confirmed && order.Status != VisOrderStatus.Invoiced)
        {
            order.Status = VisOrderStatus.Invoiced;
            AddHistory(order, from, VisOrderStatus.Invoiced, actor,
                $"فاکتور {invoice.Number} قطعی شد — سند انبار و حسابداری صادر شد");
            await _db.SaveChangesAsync();
            await NotifyOrderEventAsync(order, VisOrderEvent.InvoiceStateChanged, actor,
                $"فاکتور {invoice.Number} قطعی شد");
            await SafeBroadcastAsync(order);
        }
        else if (invoice.Status == InvoiceStatus.Cancelled && order.Status != VisOrderStatus.Canceled)
        {
            order.Status = VisOrderStatus.Canceled;
            AddHistory(order, from, VisOrderStatus.Canceled, actor, $"فاکتور {invoice.Number} ابطال شد");
            await _db.SaveChangesAsync();
            await NotifyOrderEventAsync(order, VisOrderEvent.InvoiceStateChanged, actor,
                $"فاکتور {invoice.Number} ابطال شد");
            await SafeBroadcastAsync(order);
        }

        return (await GetAsync(actor, id))!;
    }

    // ==================================================================
    //  صف‌ها و آمار
    // ==================================================================

    public async Task<List<VisApprovalItem>> ApprovalsQueueAsync(VisActor actor, int take = 100)
    {
        if (!actor.CanApprove) return new List<VisApprovalItem>();

        var orders = await _db.VisOrders.Include(o => o.Lines).AsNoTracking()
            .Where(o => o.Status == VisOrderStatus.Submitted)
            .OrderBy(o => o.SubmittedAt ?? o.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync();

        var productIds = orders.SelectMany(o => o.Lines).Select(l => l.ProductId).Distinct().ToList();
        var warehouseIds = orders.Select(o => o.WarehouseId).Distinct().ToList();
        var stocks = await _db.Stocks.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && warehouseIds.Contains(s.WarehouseId))
            .Select(s => new { s.WarehouseId, s.ProductId, s.Quantity })
            .ToListAsync();

        var links = await _db.VisCustomerLinks.AsNoTracking()
            .Where(l => l.ReferrerId == (actor.ReferrerId ?? 0) || l.CreditLimit != null)
            .Select(l => new { l.PartyId, l.CreditLimit })
            .ToListAsync();

        var result = new List<VisApprovalItem>();
        foreach (var o in orders)
        {
            var item = new VisApprovalItem { Order = ToDto(o) };

            foreach (var l in o.Lines)
            {
                var available = stocks
                    .Where(s => s.WarehouseId == o.WarehouseId && s.ProductId == l.ProductId)
                    .Select(s => s.Quantity).FirstOrDefault();
                if (available < l.Quantity)
                    item.StockWarnings.Add(new VisStockWarning
                    {
                        ProductId = l.ProductId,
                        ProductName = l.ProductName,
                        Required = l.Quantity,
                        Available = available
                    });
            }

            var limit = links.Where(x => x.PartyId == o.PartyId).Select(x => x.CreditLimit).FirstOrDefault();
            if (limit is > 0)
            {
                item.CreditLimit = limit;
                item.CustomerBalance = 0;   // مانده واقعی در صفحهٔ طرف حساب؛ اینجا فقط سقف نمایش داده می‌شود
            }

            result.Add(item);
        }

        return result;
    }

    public async Task<List<VisOrderDto>> FinanceQueueAsync(VisActor actor, int take = 200)
    {
        if (!actor.CanFinance && !actor.CanApprove) return new List<VisOrderDto>();

        var list = await _db.VisOrders.Include(o => o.Lines).AsNoTracking()
            .Where(o => o.Status == VisOrderStatus.Approved || o.Status == VisOrderStatus.Invoiced)
            .OrderByDescending(o => o.DecidedAt ?? o.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        var dtos = list.Select(ToDto).ToList();

        // کسری موجودی برای سفارش‌های تأییدشده — مالی پیش از ثبت فروش می‌بیند که چه چیزی کم است.
        var pending = dtos.Where(d => d.Status == VisOrderStatus.Approved).ToList();
        if (pending.Count > 0)
        {
            var allProducts = pending.SelectMany(d => d.Lines).Select(l => l.ProductId).Distinct().ToList();
            var stocks = await _db.Stocks.AsNoTracking()
                .Where(s => allProducts.Contains(s.ProductId))
                .Select(s => new { s.WarehouseId, s.ProductId, s.Quantity })
                .ToListAsync();
            var serviceIds = await _db.Products.AsNoTracking()
                .Where(p => allProducts.Contains(p.Id) && p.IsService)
                .Select(p => p.Id).ToListAsync();

            foreach (var d in pending)
            {
                foreach (var l in d.Lines)
                {
                    if (serviceIds.Contains(l.ProductId)) continue;   // خدمات موجودی ندارند
                    var available = stocks.Where(s => s.WarehouseId == d.WarehouseId && s.ProductId == l.ProductId)
                                          .Select(s => s.Quantity).FirstOrDefault();
                    if (available < l.Quantity)
                        d.StockWarnings.Add(new VisStockWarning
                        {
                            ProductId = l.ProductId,
                            ProductName = l.ProductName,
                            Required = l.Quantity,
                            Available = available
                        });
                }
            }
        }

        await FillSaleNumbersAsync(dtos);
        return dtos;
    }

    /// <summary>
    /// شمارهٔ سند فروش فروشگاه را روی سفارش‌های «فروش‌شده» می‌نشاند.
    /// فقط وقتی معتبر است که شناسه به یک «سند فروش» واقعی اشاره کند و شماره هم همان باشد؛
    /// در غیر این صورت مقادیر جاماندهٔ گردش قدیمی (فاکتور ماژول فاکتور) پاک می‌شوند.
    /// </summary>
    private async Task FillSaleNumbersAsync(List<VisOrderDto> dtos)
    {
        var targets = dtos.Where(d => d.Status == VisOrderStatus.Invoiced && d.InvoiceId is > 0).ToList();
        if (targets.Count == 0) return;

        var ids = targets.Select(d => d.InvoiceId!.Value).Distinct().ToList();
        var sales = await _db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.Type == TransactionType.Sale)
            .Select(t => new { t.Id, t.Number })
            .ToListAsync();

        foreach (var d in targets)
        {
            var sale = sales.FirstOrDefault(t => t.Id == d.InvoiceId);
            var matches = sale is not null &&
                          (string.IsNullOrWhiteSpace(d.InvoiceNumber) || d.InvoiceNumber == sale.Number);
            if (!matches)
            {
                d.InvoiceId = null;
                d.InvoiceNumber = null;
                d.InvoiceStatus = null;
                continue;
            }
            d.InvoiceNumber = sale!.Number;
            d.InvoiceStatus = null;
        }
    }

    public async Task<VisQueueStats> StatsAsync(VisActor actor)
    {
        var q = ScopedQuery(actor).AsNoTracking();

        var stats = new VisQueueStats
        {
            Draft = await q.CountAsync(o => o.Status == VisOrderStatus.Draft),
            Submitted = await q.CountAsync(o => o.Status == VisOrderStatus.Submitted),
            Approved = await q.CountAsync(o => o.Status == VisOrderStatus.Approved),
            Returned = await q.CountAsync(o => o.Status == VisOrderStatus.Returned),
            Invoiced = await q.CountAsync(o => o.Status == VisOrderStatus.Invoiced),
            Rejected = await q.CountAsync(o => o.Status == VisOrderStatus.Rejected),
            Canceled = await q.CountAsync(o => o.Status == VisOrderStatus.Canceled),
            // توجه: SQLite عملگر Sum روی decimal را پشتیبانی نمی‌کند؛ جمع در حافظه انجام می‌شود
            // (تعداد ردیف‌های این جمع کوچک است: سفارش‌های تأییدشده/فاکتورشدهٔ شرکت).
            ApprovedAmount = (await q.Where(o => o.Status == VisOrderStatus.Approved
                                                 || o.Status == VisOrderStatus.Invoiced)
                .Select(o => o.TotalNet).ToListAsync()).Sum(),
            InvoicedAmount = (await q.Where(o => o.Status == VisOrderStatus.Invoiced)
                .Select(o => o.TotalNet).ToListAsync()).Sum()
        };

        var pct = actor.MaxDiscountPercent;   // فقط برای نمایش تخمینی — محاسبهٔ نهایی در کیف پول معرف
        if (pct > 0) stats.CommissionEstimate = Math.Round(stats.InvoicedAmount * pct / 100m, 0);
        else if (actor.ReferrerId is > 0 && actor.IsAdmin)
        {
            var p = await _db.Referrers.IgnoreQueryFilters().Where(r => r.Id == actor.ReferrerId)
                .Select(r => r.GoodsCommissionPercent).FirstOrDefaultAsync();
            if (p > 0) stats.CommissionEstimate = Math.Round(stats.InvoicedAmount * p / 100m, 0);
        }

        return stats;
    }

    // ==================================================================
    //  دادهٔ کمکی فرم (بدون وابستگی به کنترلرهای «فقط اپراتور»)
    // ==================================================================

    public async Task<List<VisCustomerLite>> CustomersAsync(VisActor actor, string? search, int take = 50)
    {
        // فقط «مشتری» — تأمین‌کنندگان هرگز نباید در فهرست مشتری ویزیتور/مدیر فروش ظاهر شوند.
        // فیلتر شرکت صریح اعمال می‌شود (و رکوردهای بدون شرکت هم دیده می‌شوند) تا فیلتر سراسری
        // نتواند کل فهرست را خالی کند.
        var q = _db.Parties.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IsActive && p.Type == PartyType.Customer);

        if (actor.CompanyId is > 0)
        {
            var cid = actor.CompanyId.Value;
            q = q.Where(p => p.CompanyId == null || p.CompanyId == cid);
        }

        // محدودهٔ مشتری = محدودهٔ **شرکت** کاربر.
        // قبلاً فقط «پروندهٔ خودِ ویزیتور» دیده می‌شد؛ نتیجه: ویزیتوری که پرونده‌اش خالی بود
        // (یا مشتری‌ها به ویزیتور دیگری/به هیچ‌کس تخصیص داشتند) پیام «مشتری‌ای در پروندهٔ شما نیست»
        // می‌گرفت و نمی‌توانست سفارش ثبت کند. حالا همهٔ مشتریان فعال شرکت دیده می‌شوند و
        // مشتریان پروندهٔ خودِ ویزیتور اول فهرست می‌آیند (IsMine) تا تفکیک پورسانت روشن بماند.

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || (p.Mobile != null && p.Mobile.Contains(s))
                                              || (p.Phone != null && p.Phone.Contains(s)));
        }

        var myRef = actor.ReferrerId ?? 0;
        var myLinked = myRef > 0
            ? await _db.VisCustomerLinks.AsNoTracking()
                .Where(l => l.ReferrerId == myRef && l.IsActive)
                .Select(l => l.PartyId).ToListAsync()
            : new List<int>();

        var rows = await q
            .OrderBy(p => p.Name)
            .Take(Math.Clamp(take, 1, 200))
            .Select(p => new VisCustomerLite
            {
                Id = p.Id,
                Name = p.Name,
                Phone = p.Phone,
                Mobile = p.Mobile,
                Address = p.Address,
                ReferrerName = null,
                OwnedByMe = myRef > 0 && p.ReferrerId == myRef,
                OwnerRefId = p.ReferrerId
            })
            .ToListAsync();

        // نام ویزیتورِ مسئول (برای نمایش «ویزیتور: …» زیر نام مشتری در کمبو)
        var refIds = rows.Where(r => r.OwnerRefId is > 0).Select(r => r.OwnerRefId!.Value).Distinct().ToList();
        var refNames = refIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Referrers.IgnoreQueryFilters().AsNoTracking()
                .Where(r => refIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name);
        foreach (var r in rows)
        {
            if (!r.OwnedByMe && myLinked.Contains(r.Id)) r.OwnedByMe = true;
            if (r.OwnerRefId is > 0 && refNames.TryGetValue(r.OwnerRefId.Value, out var nm)) r.ReferrerName = nm;
        }

        return rows
            .OrderByDescending(r => r.OwnedByMe)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Take(Math.Clamp(take, 1, 200))
            .ToList();
    }

    public async Task<VisCustomerLite> CreateCustomerAsync(VisActor actor, string name, string? phone,
        string? mobile, string? address)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            throw new InvalidOperationException("نام مشتری را وارد کنید.");
        if (actor.ReferrerId is not > 0 && !actor.CanApprove)
            throw new InvalidOperationException("حساب شما به ویزیتور متصل نیست؛ برای ثبت مشتری جدید با مدیر تماس بگیرید.");

        var party = new PartyEntity
        {
            Type = PartyType.Customer,
            Name = name.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Mobile = string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            ReferrerId = actor.ReferrerId,
            IsActive = true,
            // «چه ویزیتوری این مشتری را ثبت کرد» — همان رکورد تعریف مشتری، با سابقهٔ ثبت‌کننده
            RegisteredByUserId = actor.UserId > 0 ? actor.UserId : null,
            RegisteredByName = string.IsNullOrWhiteSpace(actor.UserName) ? null : actor.UserName.Trim(),
            RegisteredByReferrerId = actor.ReferrerId,
            RegisteredAt = DateTime.Now,
            // مشتری جدید در شرکتِ همان ویزیتور ثبت می‌شود تا در فهرست خودش بماند
            CompanyId = actor.CompanyId
        };
        _db.Parties.Add(party);
        await _db.SaveChangesAsync();

        if (actor.ReferrerId is > 0)
        {
            _db.VisCustomerLinks.Add(new VisCustomerLink
            {
                ReferrerId = actor.ReferrerId.Value,
                PartyId = party.Id,
                IsActive = true
            });
            await _db.SaveChangesAsync();

            // اطلاع به مدیر فروش (تخصیص مشتری جدید ثبت‌شده توسط ویزیتور)
            var managers = await UsersWithPermissionAsync("Approve");
            await SafeNotifyManyAsync(managers, null,
                "مشتری جدید توسط ویزیتور ثبت شد",
                $"{actor.ReferrerName} مشتری «{party.Name}» را به پروندهٔ خود افزود.", "#6d28d9", "/parties");
        }

        return new VisCustomerLite
        {
            Id = party.Id,
            Name = party.Name,
            Phone = party.Phone,
            Mobile = party.Mobile,
            Address = party.Address
        };
    }

    public async Task<List<VisProductLite>> ProductsAsync(VisActor actor, int? warehouseId, string? search, int take = 50)
    {
        // کالاهای شرکتِ همان ویزیتور/مدیر + کالاهای بدون شرکت (میراثی).
        var q = _db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.IsActive);

        if (actor.CompanyId is > 0)
        {
            var cid = actor.CompanyId.Value;
            q = q.Where(p => p.CompanyId == null || p.CompanyId == cid);
        }

        // کالای دارای «انبار اختصاصی» فقط در همان انبار قابل ثبت است (هم‌قاعده با ماژول فروش)؛
        // پس در فهرست کالاهای این انبار هم نباید پیشنهاد شود تا سفارشِ غیرقابل‌فروش ساخته نشود.
        if (warehouseId is > 0)
            q = q.Where(p => p.WarehouseId == null || p.WarehouseId == warehouseId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s)
                             || (p.Barcode != null && p.Barcode.Contains(s)));
        }

        var list = await q.OrderBy(p => p.Name).Take(Math.Clamp(take, 1, 100))
            .Select(p => new VisProductLite
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Unit = p.Unit,
                SalePrice = p.SalePrice,
                IsService = p.IsService
            })
            .ToListAsync();

        if (warehouseId is > 0 && list.Count > 0)
        {
            var ids = list.Select(p => p.Id).ToList();
            var stocks = await _db.Stocks.AsNoTracking()
                .Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductId))
                .Select(s => new { s.ProductId, s.Quantity })
                .ToListAsync();
            foreach (var p in list)
                p.Stock = stocks.Where(s => s.ProductId == p.Id).Select(s => s.Quantity).FirstOrDefault();
        }

        return list;
    }

    /// <summary>انبارهای فعالِ شرکتِ کاربر — ویزیتور فقط انبار کمپانی خودش را می‌بیند.</summary>
    public async Task<List<VisWarehouseLite>> WarehousesAsync(VisActor actor)
    {
        var q = _db.Warehouses.IgnoreQueryFilters().AsNoTracking().Where(w => w.IsActive);

        if (actor.CompanyId is > 0)
        {
            var cid = actor.CompanyId.Value;
            q = q.Where(w => w.CompanyId == null || w.CompanyId == cid);
        }

        // انبار پیش‌فرض شرکت اول می‌آید تا فرم ویزیتور با انباری باز شود که کالا دارد
        return await q.OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .Select(w => new VisWarehouseLite { Id = w.Id, Name = w.Name })
            .ToListAsync();
    }

    // ==================================================================
    //  انقضای سفارش‌های بدون اقدام
    // ==================================================================

    public async Task<int> ExpireStaleAsync(int expireAfterDays = 7)
    {
        var threshold = DateTime.Now.AddDays(-Math.Clamp(expireAfterDays, 1, 90));
        var stale = await _db.VisOrders.Include(o => o.History)
            .Where(o => o.Status == VisOrderStatus.Submitted
                        && (o.ExpiresAt != null ? o.ExpiresAt < DateTime.Now : o.SubmittedAt < threshold))
            .Take(500)
            .ToListAsync();

        foreach (var o in stale)
        {
            o.History.Add(new VisOrderHistory
            {
                OrderId = o.Id,
                FromStatus = o.Status,
                ToStatus = VisOrderStatus.Canceled,
                UserId = 0,
                UserName = "سیستم",
                At = DateTime.Now,
                Comment = "انقضای خودکار — سفارش در بازهٔ مجاز بررسی نشد"
            });
            o.Status = VisOrderStatus.Canceled;
        }

        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync();
            // اعلان انقضای خودکار به ویزیتور و مدیر فروش
            foreach (var o in stale)
                await NotifyOrderEventAsync(o, VisOrderEvent.Expired, null);
        }
        return stale.Count;
    }

    // ==================================================================
    //  کمکی‌ها
    // ==================================================================

    private async Task<VisOrder> LoadOwnedAsync(VisActor actor, int id)
    {
        var order = await _db.VisOrders.Include(o => o.Lines).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new InvalidOperationException("سفارش یافت نشد.");
        if (!actor.CanSeeAll && order.ReferrerId != (actor.ReferrerId ?? 0) && order.CreatedByUserId != actor.UserId)
            throw new InvalidOperationException("این سفارش متعلق به شما نیست.");
        return order;
    }


    private async Task TouchCustomerLinkAsync(VisOrder order)
    {
        if (order.ReferrerId <= 0) return;
        var link = await _db.VisCustomerLinks
            .FirstOrDefaultAsync(l => l.ReferrerId == order.ReferrerId && l.PartyId == order.PartyId);
        if (link is null)
        {
            _db.VisCustomerLinks.Add(new VisCustomerLink
            {
                ReferrerId = order.ReferrerId,
                PartyId = order.PartyId,
                LastOrderAt = DateTime.Now
            });
        }
        else
        {
            link.LastOrderAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    private static void Recalculate(VisOrder order)
    {
        order.TotalGross = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
        order.TotalDiscount = order.Lines.Sum(l => l.DiscountAmount);
        order.TotalNet = order.TotalGross - order.TotalDiscount;
    }

    private static void AddHistory(VisOrder order, VisOrderStatus? from, VisOrderStatus to, VisActor actor, string? comment)
    {
        order.History.Add(new VisOrderHistory
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            UserId = actor.UserId,
            UserName = actor.UserName,
            At = DateTime.Now,
            Comment = comment
        });
    }

    private async Task<string> GenerateNumberAsync()
    {
        var (jy, _, _) = PersianDate.FromGregorian(DateTime.Now);
        var prefix = $"VS-{jy}-";
        var count = await _db.VisOrders.CountAsync(o => o.Number.StartsWith(prefix)) + 1;
        return $"{prefix}{count:0000}";
    }

    /// <summary>کاربران دارای یک اکشن مشخص از ماژول (برای اعلان مدیر/مالی).</summary>
    private async Task<List<int>> UsersWithPermissionAsync(string action)
    {
        var roleIds = await _db.RolePermissions
            .Where(rp => _db.Permissions.Any(p => p.Id == rp.PermissionId && p.Module == ModuleKey && p.Action == action))
            .Select(rp => rp.RoleId).Distinct().ToListAsync();
        if (roleIds.Count == 0) return new List<int>();

        return await _db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId).Distinct().ToListAsync();
    }

    /// <summary>رخدادهای ماژول ویزیتوری که هرکدام یک اعلان مشخص دارند.</summary>
    private enum VisOrderEvent
    {
        DraftCreated,          // پیش‌نویس جدید
        DraftUpdated,          // ویرایش پیش‌نویس
        ModifiedAfterSubmit,   // ویرایش سفارش ارسال‌شده (پیش از تأیید)
        Submitted,             // ارسال برای تأیید
        Approved,              // تأیید مدیر
        Returned,              // برگشت برای اصلاح
        Rejected,              // رد
        Canceled,              // ابطال
        Sold,                  // تأیید مالی و ثبت فروش
        InvoiceDraftCreated,   // ساخت پیش‌نویس فاکتور
        InvoiceStateChanged,   // قطعی/ابطال فاکتور
        Expired                // انقضای خودکار
    }

    /// <summary>
    /// موتور اعلان ماژول ویزیتوری — **هر رخداد** یک اعلان با متن، رنگ، مقصد و لینک مشخص دارد.
    /// <para>قاعده: عمل‌کننده (actor) خودش اعلان نمی‌گیرد؛ ذی‌نفع‌های دیگر مطلع می‌شوند.</para>
    /// <list type="bullet">
    /// <item>مدیر فروش (مجوز Approve) — پیش‌نویس‌ها، ارسال‌ها، ویرایش‌ها، غیرفعال‌شدن‌ها</item>
    /// <item>مالی (مجوز FinanceView) — تأیید مدیر و فروش نهایی</item>
    /// <item>ویزیتور ثبت‌کننده — تأیید/برگشت/رد/فروش/انقضا</item>
    /// </list>
    /// </summary>
    private async Task NotifyOrderEventAsync(VisOrder order, VisOrderEvent kind, VisActor? actor,
        string? extra = null)
    {
        try
        {
            var approvers = await UsersWithPermissionAsync("Approve");
            var finance = await UsersWithPermissionAsync("FinanceView");
            var creator = order.CreatedByUserId;
            var actorId = actor?.UserId ?? 0;
            var amount = order.TotalNet.ToString("N0");
            var who = string.IsNullOrWhiteSpace(actor?.UserName) ? order.ReferrerName : actor!.UserName;
            var link = "/sales/visits/" + order.Id;
            var summary = $"{order.Number} — {order.PartyName} — {amount} ریال";

            // فهرست‌های گیرنده: عمل‌کننده هرگز به خودش اعلان نمی‌گیرد؛
            // ویزیتور ثبت‌کننده اعلان اختصاصی خودش را جدا می‌گیرد، پس از فهرست‌های گروهی حذف می‌شود.
            var approversOnly = approvers.Where(id => id != creator).ToList();
            var financeOnly = finance.Where(id => id != creator).ToList();
            var watchers = approversOnly.Union(financeOnly).ToList();

            async Task Send(List<int> targets, string title, string body, string color, string to)
                => await SafeNotifyManyAsync(targets.Where(id => id != actorId).Distinct().ToList(),
                    order, title, body, color, to);

            switch (kind)
            {
                case VisOrderEvent.DraftCreated:
                    await Send(approversOnly, "پیش‌نویس سفارش جدید ثبت شد",
                        $"{who} پیش‌نویس {summary} را ذخیره کرد (هنوز ارسال نشده).", "#64748b", "/sales/requests");
                    break;

                case VisOrderEvent.DraftUpdated:
                    await Send(approversOnly, "پیش‌نویس سفارش ویرایش شد",
                        $"{who} پیش‌نویس {summary} را ویرایش کرد.", "#64748b", "/sales/requests");
                    break;

                case VisOrderEvent.ModifiedAfterSubmit:
                    await Send(approversOnly, "سفارش ارسال‌شده ویرایش شد — بازبینی لازم است",
                        $"{who} سفارش {summary} را پیش از تأیید ویرایش کرد؛ مقدار و اقلام را دوباره بررسی کنید.",
                        "#b45309", "/sales/approvals");
                    if (creator != actorId)
                        await NotifyAsync(creator, order, "سفارش شما ویرایش شد",
                            $"سفارش {order.Number} توسط {who} ویرایش شد.", "#b45309", link);
                    break;

                case VisOrderEvent.Submitted:
                {
                    var shortages = await ShortageSummaryAsync(order);
                    await Send(approversOnly, shortages is null
                            ? "سفارش جدید ویزیتور در انتظار تأیید"
                            : "سفارش جدید با کسری موجودی — در انتظار تأیید",
                        $"{who} سفارش {summary} را برای تأیید ارسال کرد." +
                        (shortages is null ? "" : $" کسری موجودی: {shortages}"),
                        shortages is null ? "#1d4ed8" : "#b45309", "/sales/approvals");
                    // رسید عملیات برای ویزیتور — همیشه (حتی وقتی خودش ارسال کرده است)
                    await NotifyAsync(creator, order, "سفارش شما ارسال شد",
                        $"سفارش {order.Number} برای تأیید مدیر فروش ارسال شد.", "#1d4ed8", link);
                    break;
                }

                case VisOrderEvent.Approved:
                    await NotifyAsync(creator, order, "سفارش شما تأیید شد",
                        $"سفارش {order.Number} تأیید شد و به صف مالی رفت.", "#0f766e", link);
                    await Send(watchers, "سفارش تأییدشده آمادهٔ اقدام مالی",
                        $"سفارش {summary} تأیید شد؛ صف مالی را بررسی کنید.", "#1d4ed8", "/sales/finance-queue");
                    break;

                case VisOrderEvent.Returned:
                    await NotifyAsync(creator, order, "سفارش برای اصلاح برگشت خورد",
                        $"سفارش {order.Number}: {order.DecisionNote}", "#b45309", link);
                    await Send(approversOnly, "سفارش برای اصلاح برگشت داده شد",
                        $"{who} سفارش {summary} را برگشت داد: {order.DecisionNote}", "#b45309", "/sales/requests");
                    break;

                case VisOrderEvent.Rejected:
                    await NotifyAsync(creator, order, "سفارش رد شد",
                        $"سفارش {order.Number}: {order.DecisionNote}", "#be123c", link);
                    await Send(approversOnly, "سفارش رد شد",
                        $"{who} سفارش {summary} را رد کرد: {order.DecisionNote}", "#be123c", "/sales/requests");
                    break;

                case VisOrderEvent.Canceled:
                    await NotifyAsync(creator, order, "سفارش ابطال شد",
                        $"سفارش {order.Number} توسط {who} ابطال شد." +
                        (string.IsNullOrWhiteSpace(order.DecisionNote) ? "" : $" دلیل: {order.DecisionNote}"),
                        "#be123c", link);
                    await Send(approversOnly, "سفارش ابطال شد",
                        $"{who} سفارش {summary} را ابطال کرد." +
                        (string.IsNullOrWhiteSpace(order.DecisionNote) ? "" : $" دلیل: {order.DecisionNote}"),
                        "#be123c", "/sales/requests");
                    await Send(financeOnly, "سفارش از صف مالی خارج شد",
                        $"سفارش {summary} ابطال شد و نباید تسویه شود.", "#be123c", "/sales/finance-queue");
                    break;

                case VisOrderEvent.Sold:
                    await NotifyAsync(creator, order, "سفارش شما فروش شد",
                        $"سفارش {order.Number} در مالی تأیید و به فروش فروشگاه ({extra}) منتقل شد.",
                        "#0f766e", link);
                    await Send(watchers, "سفارش در مالی به فروش تبدیل شد",
                        $"سفارش {summary} تسویه و به فروش {extra} منتقل شد.", "#0f766e", "/sales/requests");
                    await Send(financeOnly, "فروش سفارش ویزیتوری ثبت شد",
                        $"سفارش {summary} → فروش {extra}.", "#0f766e", "/sales/finance-queue");
                    break;

                case VisOrderEvent.InvoiceDraftCreated:
                    await NotifyAsync(creator, order, "پیش‌نویس فاکتور ساخته شد",
                        $"برای سفارش {order.Number} پیش‌نویس فاکتور ساخته شد.", "#1d4ed8", link);
                    break;

                case VisOrderEvent.InvoiceStateChanged:
                    await NotifyAsync(creator, order, "وضعیت فاکتور سفارش تغییر کرد",
                        $"سفارش {order.Number}: {extra}", "#1d4ed8", link);
                    await Send(watchers, "وضعیت فاکتور سفارش تغییر کرد",
                        $"{order.Number} — {order.PartyName}: {extra}", "#1d4ed8", "/sales/requests");
                    break;

                case VisOrderEvent.Expired:
                    await NotifyAsync(creator, order, "سفارش به‌دلیل انقضا بسته شد",
                        $"سفارش {order.Number} در بازهٔ مجاز بررسی نشد و به‌صورت خودکار بسته شد.",
                        "#be123c", link);
                    await Send(approversOnly, "سفارش منقضی و بسته شد",
                        $"سفارش {summary} به‌دلیل بررسی‌نشدن در بازهٔ مجاز بسته شد.", "#be123c", "/sales/requests");
                    break;
            }
        }
        catch (Exception ex)
        {
            // اعلان هرگز نباید عملیات اصلی را بشکند
            _log.LogWarning(ex, "ارسال اعلان رخداد {Kind} سفارش {Number} ناموفق بود", kind, order.Number);
        }
    }

    /// <summary>خلاصهٔ کسری موجودی سفارش برای متن اعلان (null = موجودی تأمین است).</summary>
    private async Task<string?> ShortageSummaryAsync(VisOrder order)
    {
        var lines = order.Lines?.ToList() ?? new List<VisOrderLine>();
        if (lines.Count == 0) return null;

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var stocks = await _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseId == order.WarehouseId && productIds.Contains(s.ProductId))
            .Select(s => new { s.ProductId, s.Quantity })
            .ToListAsync();

        var parts = new List<string>();
        foreach (var l in lines)
        {
            var available = stocks.Where(s => s.ProductId == l.ProductId).Select(s => s.Quantity).FirstOrDefault();
            if (available < l.Quantity)
                parts.Add($"{l.ProductName} (موجود {available:N0} از {l.Quantity:N0})");
        }
        return parts.Count == 0 ? null : string.Join("، ", parts.Take(5));
    }

    private async Task NotifyAsync(int userId, VisOrder order, string title, string body, string color, string link)
    {
        if (userId <= 0) return;
        try
        {
            await _notify.SendAsync(userId, title, body, "سامانه ویزیتوری", FormName, link);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال اعلان سفارش {Number} ناموفق بود", order.Number);
        }
    }

    private async Task SafeNotifyManyAsync(List<int> userIds, VisOrder? order, string title, string body,
        string color, string link)
    {
        if (userIds.Count == 0) return;
        try
        {
            await _notify.SendManyAsync(userIds, title, body, "سامانه ویزیتوری", FormName, link);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال اعلان گروهی سفارش {Number} ناموفق بود", order?.Number ?? "-");
        }
    }

    private async Task SafeBroadcastAsync(VisOrder order)
    {
        try { await _notify.BroadcastChangedAsync("cartable"); }
        catch (Exception ex) { _log.LogDebug(ex, "به‌روزرسانی بلادرنگ کارتابل ناموفق بود"); }
    }

    private static VisOrderDto ToDto(VisOrder o) => new()
    {
        Id = o.Id,
        Number = o.Number,
        Date = o.Date,
        DeliveryDate = o.DeliveryDate,
        FinalDeliveryDate = o.FinalDeliveryDate,
        FinalDateSetAt = o.FinalDateSetAt,
        FinalDateSetBy = o.FinalDateSetBy,
        ReferrerId = o.ReferrerId,
        ReferrerName = o.ReferrerName,
        PartyId = o.PartyId,
        PartyName = o.PartyName,
        WarehouseId = o.WarehouseId,
        WarehouseName = o.WarehouseName,
        Status = o.Status,
        PaymentMethod = o.PaymentMethod,
        TotalGross = o.TotalGross,
        TotalDiscount = o.TotalDiscount,
        TotalNet = o.TotalNet,
        Note = o.Note,
        CreatedAt = o.CreatedAt,
        CreatedByUserId = o.CreatedByUserId,
        CreatedByName = o.CreatedByName,
        SubmittedAt = o.SubmittedAt,
        DecidedAt = o.DecidedAt,
        DecidedBy = o.DecidedBy,
        DecisionNote = o.DecisionNote,
        ReturnCount = o.ReturnCount,
        DiscountOverLimit = o.DiscountOverLimit,
        // شناسه/شمارهٔ فروش فقط برای سفارش «فروش‌شده» معنا دارد؛ مقادیر جاماندهٔ گردش قدیمی
        // (پیش‌نویس فاکتور) نباید به‌عنوان شمارهٔ فروش دیده شوند.
        InvoiceId = o.Status == VisOrderStatus.Invoiced ? o.InvoiceId : null,
        InvoiceNumber = o.Status == VisOrderStatus.Invoiced ? o.InvoiceNumber : null,
        ExpiresAt = o.ExpiresAt,
        Lines = o.Lines.OrderBy(l => l.RowNo).Select(l => new VisOrderLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductCode = l.ProductCode,
            ProductName = l.ProductName,
            Unit = l.Unit,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            DiscountPercent = l.DiscountPercent,
            DiscountAmount = l.DiscountAmount,
            DiscountIsAmount = l.DiscountIsAmount,
            LineTotal = l.LineTotal,
            Note = l.Note
        }).ToList()
    };

    private static VisOrderHistoryDto ToHistoryDto(VisOrderHistory h) => new()
    {
        Id = h.Id,
        FromStatus = h.FromStatus,
        ToStatus = h.ToStatus,
        UserId = h.UserId,
        UserName = h.UserName,
        At = h.At,
        Comment = h.Comment
    };
}
