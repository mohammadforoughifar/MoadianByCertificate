using System.Globalization;
using Inventory.Api.Services.Invoicing;
using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services;

/// <summary>
/// پیاده‌سازی منطق اصلی برنامه: کالا، انبار، طرف حساب، خرید/فروش، کاردکس، نقطه سفارش و داشبورد.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly Db.AppDbContext _db;
    private readonly ILogger<InventoryService> _log;

    public InventoryService(Db.AppDbContext db, ILogger<InventoryService> log)
    {
        _db = db;
        _log = log;
    }

    // =============================== تنظیمات ===============================

    public async Task<AppSettings> GetSettingsAsync()
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync();
        return s is null
            ? new AppSettings()
            : new AppSettings { CostingMethod = s.CostingMethod, AllowNegativeStock = s.AllowNegativeStock, ItServerUrl = s.ItServerUrl, ItCompanyName = s.ItCompanyName, BaleBotToken = s.BaleBotToken, EitaaToken = s.EitaaToken, MessengerSenderNumber = s.MessengerSenderNumber, NotifyBannerMs = s.NotifyBannerMs, NotifyToastMs = s.NotifyToastMs };
    }

    public async Task<AppSettings> SaveSettingsAsync(AppSettings dto)
    {
        var method = (dto.CostingMethod ?? "Average").Trim();
        if (method is not ("Average" or "FIFO" or "LIFO"))
            throw new InvalidOperationException("روش قیمت‌گذاری باید یکی از مقادیر Average، FIFO یا LIFO باشد.");

        var s = await _db.AppSettings.FirstOrDefaultAsync();
        if (s is null)
        {
            s = new Db.AppSetting();
            _db.AppSettings.Add(s);
        }
        s.CostingMethod = method;
        s.AllowNegativeStock = dto.AllowNegativeStock;
        s.ItServerUrl = string.IsNullOrWhiteSpace(dto.ItServerUrl) ? null : dto.ItServerUrl.Trim().TrimEnd('/');
        s.ItCompanyName = string.IsNullOrWhiteSpace(dto.ItCompanyName) ? null : dto.ItCompanyName.Trim();
        s.BaleBotToken = string.IsNullOrWhiteSpace(dto.BaleBotToken) ? null : dto.BaleBotToken.Trim();
        s.EitaaToken = string.IsNullOrWhiteSpace(dto.EitaaToken) ? null : dto.EitaaToken.Trim();
        s.MessengerSenderNumber = string.IsNullOrWhiteSpace(dto.MessengerSenderNumber) ? "09111189771" : dto.MessengerSenderNumber.Trim();
        // مدت نمایش اعلان‌ها و پیام‌ها (سراسری): ۰ = تا بستن دستی، در غیر این‌صورت بین ۵ ثانیه تا ۱ ساعت؛ مقدار نامعتبر → پیش‌فرض
        s.NotifyBannerMs = SanitizeNotifyMs(dto.NotifyBannerMs, 120_000);
        s.NotifyToastMs = SanitizeNotifyMs(dto.NotifyToastMs, 20_000);
        await _db.SaveChangesAsync();
        return await GetSettingsAsync();
    }

    /// <summary>اعتبارسنجی مدت نمایش (میلی‌ثانیه): ۰ = تا بستن دستی؛ در غیر این‌صورت بین ۵۰۰۰ تا ۳۶۰۰۰۰۰؛ خارج از بازه → مقدار پیش‌فرض.</summary>
    private static int SanitizeNotifyMs(int ms, int fallback)
        => ms == 0 ? 0 : (ms >= 5_000 && ms <= 3_600_000 ? ms : fallback);

    // =============================== معرف (بازاریاب) ===============================

    public async Task<List<Referrer>> GetReferrersAsync(bool activeOnly = false, Paging.Request? pagination = null, string? search = null)
    {
        var q = _db.Referrers.AsQueryable();
        if (activeOnly) q = q.Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(r => r.Name.Contains(term));
        }
        var list = await q.OrderBy(r => r.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var counts = await _db.Transactions
            .Where(t => t.ReferrerId != null && list.Select(r => r.Id).Contains(t.ReferrerId.Value))
            .GroupBy(t => t.ReferrerId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        return list.Select(r => new Referrer
        {
            Id = r.Id,
            Name = r.Name,
            CompanyName = r.CompanyName,
            Phone = r.Phone,
            GoodsCommissionPercent = r.GoodsCommissionPercent,
            ServiceCommissionPercent = r.ServiceCommissionPercent,
            CardNumber = r.CardNumber,
            Iban = r.Iban,
            CanViewProducts = r.CanViewProducts,
                MaxDiscountPercent = r.MaxDiscountPercent,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt,
            OrderCount = counts.FirstOrDefault(x => x.Id == r.Id)?.Count ?? 0
        }).ToList();
    }

    public async Task<Referrer> SaveReferrerAsync(Referrer dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام معرف را وارد کنید.");
        if (dto.GoodsCommissionPercent is < 0 or > 100 || dto.ServiceCommissionPercent is < 0 or > 100)
            throw new InvalidOperationException("درصد پورسانت باید بین ۰ و ۱۰۰ باشد.");

        var card = NullIfEmpty(dto.CardNumber)?.Replace(" ", "").Replace("-", "");
        if (card is not null && (card.Length != 16 || !card.All(char.IsDigit)))
            throw new InvalidOperationException("شماره کارت باید ۱۶ رقم باشد.");
        dto.CardNumber = card;

        var iban = NullIfEmpty(dto.Iban)?.Replace(" ", "").ToUpperInvariant();
        if (iban is not null)
        {
            if (iban.StartsWith("IR")) iban = iban[2..];
            if (iban.Length != 24 || !iban.All(char.IsDigit))
                throw new InvalidOperationException("شماره شبا باید ۲۴ رقم (بدون IR) باشد.");
        }
        dto.Iban = iban;

        var name = dto.Name.Trim();
        var dup = await _db.Referrers.AnyAsync(r => r.Name == name && r.Id != dto.Id);
        if (dup) throw new InvalidOperationException("معرفی با این نام از قبل وجود دارد.");

        Db.Referrer entity;
        if (dto.Id == 0)
        {
            entity = new Db.Referrer { CreatedAt = DateTime.Now };
            _db.Referrers.Add(entity);
        }
        else
        {
            entity = await _db.Referrers.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("معرف یافت نشد.");
        }

        entity.Name = name;
        entity.CompanyName = NullIfEmpty(dto.CompanyName);
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.GoodsCommissionPercent = dto.GoodsCommissionPercent;
        entity.ServiceCommissionPercent = dto.ServiceCommissionPercent;
        entity.CardNumber = NullIfEmpty(dto.CardNumber);
        entity.Iban = NullIfEmpty(dto.Iban);
        entity.CanViewProducts = dto.CanViewProducts;
        // سقف تخفیف مجاز ویزیتور در سفارش میدانی (۰ = بدون سقف)
        entity.MaxDiscountPercent = Math.Clamp(dto.MaxDiscountPercent, 0, 100);
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteReferrerAsync(int id)
    {
        var r = await _db.Referrers.FindAsync(id);
        if (r is null) return;
        var used = await _db.Transactions.AnyAsync(t => t.ReferrerId == id);
        if (used) throw new InvalidOperationException("این معرف دارای اسناد فروش است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var hasPayments = await _db.ReferrerPayments.AnyAsync(p => p.ReferrerId == id);
        if (hasPayments) throw new InvalidOperationException("این معرف دارای سند پرداخت است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        _db.Referrers.Remove(r);
        await _db.SaveChangesAsync();
    }

    // =============================== کیف پول و پرداخت معرف ===============================

    /// <summary>جمع پورسانت یک معرف روی همه اسناد فروش او.</summary>
    private async Task<decimal> ComputeReferrerCommissionAsync(int referrerId)
    {
        var txns = await _db.Transactions.Include(t => t.Lines)
            .Where(t => t.ReferrerId == referrerId && t.Type == TransactionType.Sale)
            .ToListAsync();

        decimal total = 0;
        foreach (var t in txns)
        {
            var order = await ToOrderDtoAsync(t); // شامل محاسبه سود و پورسانت
            total += order.CommissionAmount ?? 0;
        }
        return total;
    }

    /// <summary>نگاشت سبک معرف به DTO کیف پول (بدون مبالغ).</summary>
    private static Referrer MapWallet(Db.Referrer r, int orderCount = 0) => new()
    {
        Id = r.Id,
        Name = r.Name,
        CompanyName = r.CompanyName,
        Phone = r.Phone,
        GoodsCommissionPercent = r.GoodsCommissionPercent,
        ServiceCommissionPercent = r.ServiceCommissionPercent,
        CardNumber = r.CardNumber,
        Iban = r.Iban,
        CanViewProducts = r.CanViewProducts,
                MaxDiscountPercent = r.MaxDiscountPercent,
        IsActive = r.IsActive,
        CreatedAt = r.CreatedAt,
        OrderCount = orderCount
    };

    public async Task<List<Referrer>> GetReferrerWalletsAsync(string? search, string? sortBy, bool desc,
        Paging.Request? pagination = null)
    {
        // فیلتر جست‌وجو در SQL.
        var q = _db.Referrers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.Name.Contains(s) || (r.CompanyName != null && r.CompanyName.Contains(s)));
        }

        var mode = sortBy?.ToLowerInvariant();
        if (mode != "commission" && mode != "paid" && mode != "balance") mode = "name";

        if (mode is "commission" or "balance")
        {
            // کلید این دو مرتب‌سازی (پورسانت/مانده) به محاسبهٔ پورسانت روی زنجیرهٔ بهای تمام‌شدهٔ
            // FIFO/LIFO/میانگین وابسته است و در SQL قابل بیان نیست؛ بنابراین فقط برای این دو حالت،
            // مبالغ همهٔ معرف‌ها محاسبه و در حافظه مرتب می‌شود و سپس صفحهٔ درخواستی برش می‌خورد.
            // راه‌حل بلندمدتِ Database-level: ذخیرهٔ پورسانت روی سند فروش هنگام ثبت است.
            var all = await q.OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync();
            var allIds = all.Select(r => r.Id).ToList();
            var orderCounts = allIds.Count == 0
                ? new Dictionary<int, int>()
                : await _db.Transactions
                    .Where(t => t.ReferrerId != null && allIds.Contains(t.ReferrerId.Value))
                    .GroupBy(t => t.ReferrerId!.Value)
                    .Select(g => new { Id = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Id, x => x.Count);
            var paidMap = (await _db.ReferrerPayments.AsNoTracking().ToListAsync())
                .GroupBy(p => p.ReferrerId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            var walletList = new List<Referrer>();
            foreach (var r in all)
            {
                var dto = MapWallet(r, orderCounts.TryGetValue(r.Id, out var oc) ? oc : 0);
                dto.TotalCommission = await ComputeReferrerCommissionAsync(r.Id);
                dto.TotalPaid = paidMap.TryGetValue(r.Id, out var paid) ? paid : 0;
                dto.WalletBalance = dto.TotalCommission - dto.TotalPaid;
                walletList.Add(dto);
            }

            var sorted = mode == "commission"
                ? (desc ? walletList.OrderByDescending(r => r.TotalCommission) : walletList.OrderBy(r => r.TotalCommission))
                : (desc ? walletList.OrderByDescending(r => r.WalletBalance) : walletList.OrderBy(r => r.WalletBalance));
            var list = sorted.ThenBy(r => r.Name).ThenBy(r => r.Id).ToList();
            if (pagination is { IsPaged: true })
            {
                pagination.SetTotal(list.Count);
                if (pagination.Skip > 0) list = list.Skip(pagination.Skip).ToList();
                if (pagination.Take is > 0) list = list.Take(pagination.Take.Value).ToList();
            }
            return list;
        }

        // name/paid: فیلتر → ترتیب → شمارش → صفحه در SQL؛ مبالغ فقط برای رکوردهای صفحه محاسبه می‌شود.
        // مرتب‌سازی paid روی SUM پرداختی‌ها در SQL (به‌خاطر نبود Sum دسیمال در SQLite با معادل double).
        IOrderedQueryable<Db.Referrer> ordered;
        if (mode == "paid")
        {
            ordered = _db.Database.IsSqlite()
                ? (desc
                    ? q.OrderByDescending(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (double?)p.Amount) ?? 0)
                    : q.OrderBy(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (double?)p.Amount) ?? 0))
                : (desc
                    ? q.OrderByDescending(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (decimal?)p.Amount) ?? 0m)
                    : q.OrderBy(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (decimal?)p.Amount) ?? 0m));
        }
        else
        {
            ordered = desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name);
        }

        var page = await ordered.ThenBy(r => r.Name).ThenBy(r => r.Id).ToPageListAsync(pagination);
        var pageIds = page.Select(r => r.Id).ToList();
        var pageOrderCounts = pageIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.Transactions
                .Where(t => t.ReferrerId != null && pageIds.Contains(t.ReferrerId.Value))
                .GroupBy(t => t.ReferrerId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);
        var pagePaid = pageIds.Count == 0
            ? new List<Db.ReferrerPayment>()
            : await _db.ReferrerPayments.AsNoTracking().Where(p => pageIds.Contains(p.ReferrerId)).ToListAsync();
        var pagePaidMap = pagePaid.GroupBy(p => p.ReferrerId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var result = new List<Referrer>();
        foreach (var r in page)
        {
            var dto = MapWallet(r, pageOrderCounts.TryGetValue(r.Id, out var pc) ? pc : 0);
            dto.TotalCommission = await ComputeReferrerCommissionAsync(r.Id);
            dto.TotalPaid = pagePaidMap.TryGetValue(r.Id, out var paid) ? paid : 0;
            dto.WalletBalance = dto.TotalCommission - dto.TotalPaid;
            result.Add(dto);
        }
        return result;
    }

    public async Task<List<ReferrerPayment>> GetReferrerPaymentsAsync(int? referrerId, Paging.Request? pagination = null)
    {
        var q = _db.ReferrerPayments.AsQueryable();
        if (referrerId is > 0) q = q.Where(p => p.ReferrerId == referrerId);
        var list = await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToPageListAsync(pagination);

        var names = await _db.Referrers.ToDictionaryAsync(r => r.Id, r => r.Name);
        return list.Select(p => new ReferrerPayment
        {
            Id = p.Id,
            ReferrerId = p.ReferrerId,
            ReferrerName = names.TryGetValue(p.ReferrerId, out var n) ? n : "",
            Number = p.Number,
            Amount = p.Amount,
            Date = p.Date,
            Description = p.Description,
            CreatedAt = p.CreatedAt
        }).ToList();
    }

    public async Task<ReferrerPayment> AddReferrerPaymentAsync(ReferrerPayment dto)
    {
        if (dto.ReferrerId <= 0) throw new InvalidOperationException("معرف را انتخاب کنید.");
        if (dto.Amount <= 0) throw new InvalidOperationException("مبلغ پرداخت باید بزرگ‌تر از صفر باشد.");
        _ = await _db.Referrers.FindAsync(dto.ReferrerId)
            ?? throw new InvalidOperationException("معرف یافت نشد.");

        // شماره سند پرداخت: PY-0001
        var count = await _db.ReferrerPayments.CountAsync();
        string number;
        do { number = $"PY-{++count:0000}"; }
        while (await _db.ReferrerPayments.AnyAsync(p => p.Number == number));

        var entity = new Db.ReferrerPayment
        {
            ReferrerId = dto.ReferrerId,
            Number = number,
            Amount = dto.Amount,
            Date = dto.Date == default ? DateTime.Now : dto.Date,
            Description = NullIfEmpty(dto.Description),
            CreatedAt = DateTime.Now
        };
        _db.ReferrerPayments.Add(entity);
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.Number = entity.Number;
        dto.Date = entity.Date;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteReferrerPaymentAsync(int id)
    {
        var p = await _db.ReferrerPayments.FindAsync(id);
        if (p is null) return;
        _db.ReferrerPayments.Remove(p);
        await _db.SaveChangesAsync();
    }

    // =============================== محاسبه بهای تمام‌شده (Average / FIFO / LIFO) ===============================

    /// <summary>
    /// محاسبه بهای تمام‌شده واحد برای هر سطر فروش یک کالا در یک انبار، طبق روش قیمت‌گذاری.
    /// خروجی: نگاشت شناسه سطر فروش ← بهای تمام‌شده واحد.
    /// </summary>
    private static Dictionary<int, decimal> ComputeSaleCosts(
        List<Db.Transaction> orderedTxns, int productId, string method, decimal defaultCost)
    {
        var result = new Dictionary<int, decimal>();

        // لایه‌های موجودی: (مقدار باقی‌مانده، قیمت واحد)
        var layers = new List<(decimal Qty, decimal Cost)>();
        decimal avgQty = 0, avgCost = 0;

        foreach (var t in orderedTxns)
        {
            foreach (var l in t.Lines.Where(l => l.ProductId == productId))
            {
                switch (t.Type)
                {
                    case TransactionType.Purchase:
                        layers.Add((l.Quantity, l.Price));
                        var nq = avgQty + l.Quantity;
                        if (nq != 0) avgCost = (avgQty * avgCost + l.Quantity * l.Price) / nq;
                        avgQty = nq;
                        break;

                    case TransactionType.Sale:
                        decimal unitCost;
                        if (method == "FIFO" || method == "LIFO")
                        {
                            decimal remaining = l.Quantity, costSum = 0, taken = 0;
                            while (remaining > 0 && layers.Count > 0)
                            {
                                var idx = method == "FIFO" ? 0 : layers.Count - 1;
                                var layer = layers[idx];
                                var take = Math.Min(layer.Qty, remaining);
                                costSum += take * layer.Cost;
                                taken += take;
                                remaining -= take;
                                if (layer.Qty - take <= 0) layers.RemoveAt(idx);
                                else layers[idx] = (layer.Qty - take, layer.Cost);
                            }
                            // اگر لایه کافی نبود، باقی‌مانده با آخرین قیمت معلوم/پیش‌فرض
                            if (remaining > 0)
                            {
                                var fallback = taken > 0 ? costSum / taken : (avgCost > 0 ? avgCost : defaultCost);
                                costSum += remaining * fallback;
                                taken += remaining;
                            }
                            unitCost = taken > 0 ? costSum / taken : defaultCost;
                        }
                        else // Average
                        {
                            unitCost = avgQty > 0 || avgCost > 0 ? avgCost : defaultCost;
                        }

                        avgQty -= l.Quantity;
                        result[l.Id] = unitCost;
                        break;

                    default: // Adjustment / Initial — پایه جدید موجودی
                        layers.Clear();
                        var baseCost = l.Price > 0 ? l.Price : (avgCost > 0 ? avgCost : defaultCost);
                        if (l.Quantity > 0) layers.Add((l.Quantity, baseCost));
                        avgQty = l.Quantity;
                        if (avgCost == 0) avgCost = baseCost;
                        break;
                }
            }
        }

        return result;
    }

    /// <summary>محاسبه سود سطرهای فروش یک سند + پورسانت معرف.</summary>
    private async Task EnrichSaleProfitAsync(Order order)
    {
        if (order.Type != TransactionType.Sale) return;

        var settings = await GetSettingsAsync();
        var productIds = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        // واکشی فاکتور در دیتابیس برای دسترسی به Snapshotهای ثبت‌شده روی سطرهای TransactionLine
        var dbTxn = await _db.Transactions.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == order.Id);
        var snapshotMap = dbTxn?.Lines.ToDictionary(l => l.Id, l => l.UnitCostSnapshot) 
                          ?? new Dictionary<int, decimal?>();

        foreach (var pid in productIds)
        {
            if (!products.TryGetValue(pid, out var product)) continue;
            var productLines = order.Lines.Where(x => x.ProductId == pid).ToList();

            if (product.IsService)
            {
                // خدمات / اجرت تعمیرات:
                // سود خالص = مبلغ کل سطر (پس از تخفیف) − (هزینه/دستمزد تعمیرکار × تعداد)
                foreach (var l in productLines)
                {
                    l.IsService = true;
                    // بهای تمام‌شده/دستمزد اگر روی سطر نباشد از دیتابیس (UnitCostSnapshot) خوانده می‌شود
                    var unitCost = l.UnitCost ?? (snapshotMap.TryGetValue(l.Id, out var sn) && sn.HasValue ? sn.Value : 0m);
                    l.UnitCost = decimal.Round(unitCost, 2);
                    // سود دقیق با کسر تخفیف و کسر دستمزد تعمیرکار:
                    l.Profit = decimal.Round(l.Total - (unitCost * l.Quantity), 2);
                }
                continue;
            }

            foreach (var l in productLines)
            {
                if (!l.UnitCost.HasValue && snapshotMap.TryGetValue(l.Id, out var sn) && sn.HasValue)
                {
                    l.UnitCost = sn.Value;
                }
            }

            foreach (var l in productLines.Where(x => x.UnitCost.HasValue))
            {
                // بهای ذخیره‌شده روی خود سطر (مثل بهای قطعه تعمیری) بر روش عمومی اولویت دارد.
                // سود دقیق = مبلغ کل سطر (پس از تخفیف) − (بهای تمام‌شده × تعداد)
                var unitCost = l.UnitCost!.Value;
                l.UnitCost = decimal.Round(unitCost, 2);
                l.Profit = decimal.Round(l.Total - (unitCost * l.Quantity), 2);
            }

            var linesNeedingCost = productLines.Where(x => !x.UnitCost.HasValue).ToList();
            if (linesNeedingCost.Count == 0) continue;

            var txns = await _db.Transactions.Include(t => t.Lines)
                .Where(t => t.WarehouseId == order.WarehouseId && t.Lines.Any(l => l.ProductId == pid))
                .OrderBy(t => t.Date).ThenBy(t => t.Id)
                .ToListAsync();

            var costs = ComputeSaleCosts(txns, pid, settings.CostingMethod, product.PurchasePrice);

            foreach (var l in linesNeedingCost)
            {
                var unitCost = costs.TryGetValue(l.Id, out var c) ? c : product.PurchasePrice;
                l.UnitCost = decimal.Round(unitCost, 2);
                l.Profit = decimal.Round(l.Total - (unitCost * l.Quantity), 2);
            }
        }

        order.TotalProfit = order.Lines.Sum(l => l.Profit ?? 0);

        // پورسانت معرف (به تفکیک هر سطر):
        //   کالا   → درصد کالا × «سود» سطر (سود منفی پورسانت نمی‌گیرد)
        //   خدمات → درصد خدمات × «کل مبلغ» سطر
        if (order.ReferrerId is > 0)
        {
            var referrer = await _db.Referrers.FindAsync(order.ReferrerId);
            if (referrer is not null)
            {
                foreach (var l in order.Lines)
                {
                    // پورسانت بر اساس سود خالص سطر (نه کل مبلغ ناخالص):
                    // سود پس از کسر تخفیف و کسر بهای قطعه و دستمزد
                    var profit = l.Profit ?? 0;
                    if (profit > 0)
                    {
                        var percent = l.IsService ? referrer.ServiceCommissionPercent : referrer.GoodsCommissionPercent;
                        l.Commission = decimal.Round(profit * percent / 100m, 2);
                    }
                    else
                    {
                        l.Commission = 0;
                    }
                }
                order.CommissionAmount = order.Lines.Sum(l => l.Commission ?? 0);
            }
        }

        // سود خالص = سود کل − پورسانت معرف
        order.NetProfit = order.TotalProfit - (order.CommissionAmount ?? 0);
    }

    /// <summary>بررسی منفی نشدن موجودی جفت‌های (انبار، کالا) — با توجه به تنظیمات.</summary>
    private async Task EnsureNoNegativeStockAsync(IEnumerable<(int Wh, int Pid)> pairs)
    {
        var settings = await GetSettingsAsync();
        if (settings.AllowNegativeStock) return;

        foreach (var (wh, pid) in pairs.Distinct())
        {
            var product = await _db.Products.FindAsync(pid);
            if (product is null || product.IsService) continue;
            var st = await _db.Stocks.FirstOrDefaultAsync(s => s.WarehouseId == wh && s.ProductId == pid);
            if (st is not null && st.Quantity < 0)
                throw new InvalidOperationException(
                    $"با این عملیات، موجودی «{product.Name}» منفی می‌شود ({st.Quantity:0.##})؛ عملیات انجام نشد. " +
                    "در صورت نیاز می‌توانید در تنظیمات، موجودی منفی را مجاز کنید.");
        }
    }

    // =============================== گروه کالا ===============================

    public async Task<List<ProductCategory>> GetCategoriesAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var scope = _db.ProductCategories.AsNoTracking().AsQueryable();
        if (activeOnly) scope = scope.Where(c => c.IsActive);
        IOrderedQueryable<Db.ProductCategory> Order(IQueryable<Db.ProductCategory> q) => q
            .OrderBy(c => c.Name).ThenBy(c => c.Id);

        // الگوی DatabaseTreePaging: شمارش و صفحه‌بندی ریشه‌ها در SQL و واکشی کامل
        // زیرشاخه‌های ریشه‌های منتخب؛ total تعداد ریشه‌هاست و ساختار درختی حفظ می‌شود.
        var cats = await Order(scope).ReadTreePageAsync(
            Order(scope.Where(c => c.ParentId == null || c.ParentId == 0 || !scope.Any(p => p.Id == c.ParentId))),
            ids => Order(scope.Where(c => c.ParentId != null && ids.Contains(c.ParentId.Value))),
            c => c.Id, pagination);

        // شمارش کالاهای هر گروه (بر اساس نام گروه ذخیره‌شده روی کالا) — فقط برای گره‌های صفحهٔ جاری
        var catNames = cats.Select(c => c.Name).Distinct().ToList();
        var counts = catNames.Count == 0
            ? new Dictionary<string, int>()
            : await _db.Products
                .Where(p => p.Category != null && catNames.Contains(p.Category))
                .GroupBy(p => p.Category!)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count);

        // ---------- چینش درختی: والدها اول، فرزندان زیر والد با محاسبه عمق ----------
        var byParent = cats.GroupBy(c => c.ParentId).ToDictionary(g => g.Key ?? 0, g => g.OrderBy(c => c.Name).ToList());
        var result = new List<ProductCategory>();

        void Walk(int parentId, int depth, string parentPath)
        {
            if (!byParent.TryGetValue(parentId, out var children)) return;
            foreach (var c in children)
            {
                var path = string.IsNullOrEmpty(parentPath) ? c.Name : $"{parentPath} ← {c.Name}";
                result.Add(new ProductCategory
                {
                    Id = c.Id,
                    Name = c.Name,
                    ParentId = c.ParentId,
                    Description = c.Description,
                    IsActive = c.IsActive,
                    CreatedAt = c.CreatedAt,
                    ProductCount = counts.TryGetValue(c.Name, out var pc) ? pc : 0,
                    Depth = depth,
                    FullPath = path
                });
                Walk(c.Id, depth + 1, path);
            }
        }

        Walk(0, 0, "");

        // گروه‌هایی که والدشان (به هر دلیل) در فهرست نیست، به انتها اضافه می‌شوند
        var seen = result.Select(r => r.Id).ToHashSet();
        foreach (var c in cats.Where(c => !seen.Contains(c.Id)))
            result.Add(new ProductCategory
            {
                Id = c.Id, Name = c.Name, ParentId = c.ParentId, Description = c.Description,
                IsActive = c.IsActive, CreatedAt = c.CreatedAt,
                ProductCount = counts.TryGetValue(c.Name, out var pc2) ? pc2 : 0,
                Depth = 0, FullPath = c.Name
            });

        return result;
    }

    public async Task<ProductCategory> SaveCategoryAsync(ProductCategory dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام گروه کالا را وارد کنید.");

        var name = dto.Name.Trim();
        var dup = await _db.ProductCategories.AnyAsync(c => c.Name == name && c.Id != dto.Id);
        if (dup) throw new InvalidOperationException("گروه کالایی با این نام از قبل وجود دارد.");

        // اعتبارسنجی والد: وجود داشته باشد و حلقه در درخت ایجاد نشود
        if (dto.ParentId is > 0)
        {
            if (dto.ParentId == dto.Id)
                throw new InvalidOperationException("یک گروه نمی‌تواند والد خودش باشد.");
            var parent = await _db.ProductCategories.FindAsync(dto.ParentId)
                ?? throw new InvalidOperationException("گروه والد یافت نشد.");

            // بررسی حلقه: والد انتخابی نباید از نوادگان همین گروه باشد
            var cursor = parent;
            while (cursor?.ParentId != null)
            {
                if (cursor.ParentId == dto.Id)
                    throw new InvalidOperationException("گروه والد نمی‌تواند زیرگروهِ خود این گروه باشد.");
                cursor = await _db.ProductCategories.FindAsync(cursor.ParentId);
            }
        }

        Db.ProductCategory entity;
        string? oldName = null;
        if (dto.Id == 0)
        {
            entity = new Db.ProductCategory { CreatedAt = DateTime.Now };
            _db.ProductCategories.Add(entity);
        }
        else
        {
            entity = await _db.ProductCategories.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("گروه کالا یافت نشد.");
            oldName = entity.Name;
        }

        entity.Name = name;
        entity.ParentId = dto.ParentId is > 0 ? dto.ParentId : null;
        entity.Description = NullIfEmpty(dto.Description);
        entity.IsActive = dto.IsActive;

        // در صورت تغییر نام گروه، نام روی کالاهای مرتبط هم به‌روزرسانی می‌شود
        if (oldName != null && oldName != name)
        {
            var related = await _db.Products.Where(p => p.Category == oldName).ToListAsync();
            foreach (var p in related) p.Category = name;
        }

        await _db.SaveChangesAsync();

        var count = await _db.Products.CountAsync(p => p.Category == entity.Name);
        return new ProductCategory
        {
            Id = entity.Id,
            Name = entity.Name,
            ParentId = entity.ParentId,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            ProductCount = count
        };
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var c = await _db.ProductCategories.FindAsync(id);
        if (c is null) return;
        var used = await _db.Products.AnyAsync(p => p.Category == c.Name);
        if (used) throw new InvalidOperationException("این گروه دارای کالا است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var hasChildren = await _db.ProductCategories.AnyAsync(x => x.ParentId == id);
        if (hasChildren) throw new InvalidOperationException("این گروه دارای زیرگروه است؛ ابتدا زیرگروه‌ها را حذف یا جابه‌جا کنید.");
        _db.ProductCategories.Remove(c);
        await _db.SaveChangesAsync();
    }

    // =============================== واحد شمارش ===============================

    public async Task<List<MeasureUnit>> GetUnitsAsync(bool activeOnly = false, Paging.Request? pagination = null, string? search = null)
    {
        var q = _db.MeasureUnits.AsQueryable();
        if (activeOnly) q = q.Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(u => u.Name.Contains(term));
        }
        var units = await q.OrderBy(u => u.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var counts = await _db.Products
            .GroupBy(p => p.Unit)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync();

        return units.Select(u => new MeasureUnit
        {
            Id = u.Id,
            Name = u.Name,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            ProductCount = counts.FirstOrDefault(x => x.Name == u.Name)?.Count ?? 0
        }).ToList();
    }

    public async Task<MeasureUnit> SaveUnitAsync(MeasureUnit dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام واحد شمارش را وارد کنید.");

        var name = dto.Name.Trim();
        var dup = await _db.MeasureUnits.AnyAsync(u => u.Name == name && u.Id != dto.Id);
        if (dup) throw new InvalidOperationException("واحد شمارشی با این نام از قبل وجود دارد.");

        Db.MeasureUnit entity;
        string? oldName = null;
        if (dto.Id == 0)
        {
            entity = new Db.MeasureUnit { CreatedAt = DateTime.Now };
            _db.MeasureUnits.Add(entity);
        }
        else
        {
            entity = await _db.MeasureUnits.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("واحد شمارش یافت نشد.");
            oldName = entity.Name;
        }

        entity.Name = name;
        entity.IsActive = dto.IsActive;

        // در صورت تغییر نام واحد، روی کالاهای مرتبط هم اعمال می‌شود
        if (oldName != null && oldName != name)
        {
            var related = await _db.Products.Where(p => p.Unit == oldName).ToListAsync();
            foreach (var p in related) p.Unit = name;
        }

        await _db.SaveChangesAsync();

        var count = await _db.Products.CountAsync(p => p.Unit == entity.Name);
        return new MeasureUnit
        {
            Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt, ProductCount = count
        };
    }

    public async Task DeleteUnitAsync(int id)
    {
        var u = await _db.MeasureUnits.FindAsync(id);
        if (u is null) return;
        var used = await _db.Products.AnyAsync(p => p.Unit == u.Name);
        if (used) throw new InvalidOperationException("این واحد روی کالاها استفاده شده و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        _db.MeasureUnits.Remove(u);
        await _db.SaveChangesAsync();
    }

    // =============================== ورود اکسل کالا ===============================

    /// <summary>ستون‌های فایل اکسل: کد کالا | نام کالا | گروه | واحد | قیمت خرید | قیمت فروش | نقطه سفارش | حداکثر موجودی | بارکد | توضیحات</summary>
    public async Task<ExcelImportResult> ImportProductsAsync(Stream excelStream, int? warehouseId = null)
    {
        var result = new ExcelImportResult();

        using var wb = new ClosedXML.Excel.XLWorkbook(excelStream);
        var ws = wb.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("فایل اکسل فاقد کاربرگ (Sheet) است.");

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            throw new InvalidOperationException("فایل اکسل خالی است؛ داده‌ها باید از سطر ۲ (بعد از عنوان) شروع شوند.");

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.IsEmpty()) continue;

            try
            {
                var code = row.Cell(1).GetString().Trim();
                var name = row.Cell(2).GetString().Trim();
                var category = row.Cell(3).GetString().Trim();
                var unit = row.Cell(4).GetString().Trim();
                var purchase = ReadDecimal(row.Cell(5));
                var sale = ReadDecimal(row.Cell(6));
                var reorder = ReadDecimal(row.Cell(7));
                var maxStock = ReadDecimal(row.Cell(8));
                var barcode = row.Cell(9).GetString().Trim();
                var desc = row.Cell(10).GetString().Trim();

                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("نام کالا خالی است.");
                if (purchase < 0 || sale < 0 || reorder < 0 || maxStock < 0)
                    throw new InvalidOperationException("مقادیر عددی نمی‌توانند منفی باشند.");

                await SaveProductAsync(new Product
                {
                    Code = code,
                    Name = name,
                    Category = string.IsNullOrWhiteSpace(category) ? null : category,
                    Unit = string.IsNullOrWhiteSpace(unit) ? "عدد" : unit,
                    PurchasePrice = purchase,
                    SalePrice = sale,
                    ReorderPoint = reorder,
                    MaxStock = maxStock,
                    Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode,
                    Description = string.IsNullOrWhiteSpace(desc) ? null : desc,
                    WarehouseId = warehouseId is > 0 ? warehouseId : null,
                    IsActive = true
                });

                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"سطر {r}: {ex.Message}");
            }
        }

        // واحدهای جدید فایل اکسل به جدول واحدهای شمارش اضافه می‌شوند
        var usedUnits = await _db.Products.Select(p => p.Unit).Distinct().ToListAsync();
        var known = await _db.MeasureUnits.Select(u => u.Name).ToListAsync();
        foreach (var u in usedUnits.Where(x => !string.IsNullOrWhiteSpace(x) && !known.Contains(x)))
            _db.MeasureUnits.Add(new Db.MeasureUnit { Name = u, CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return result;
    }

    private static decimal ReadDecimal(ClosedXML.Excel.IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.TryGetValue<decimal>(out var v)) return v;
        var s = Fa.ToEn(cell.GetString()).Replace(",", "").Trim();
        return decimal.TryParse(s, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new InvalidOperationException($"مقدار «{cell.GetString()}» عدد معتبری نیست.");
    }

    /// <summary>ساخت فایل اکسل نمونه برای ورود گروهی کالا (با دو سطر مثال).</summary>
    public byte[] BuildProductTemplate()
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("کالاها");
        ws.RightToLeft = true;

        string[] headers = { "کد کالا", "نام کالا", "گروه", "واحد", "قیمت خرید", "قیمت فروش", "نقطه سفارش", "حداکثر موجودی", "بارکد", "توضیحات" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#DDEBF7");
            c.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }

        // دو سطر نمونه
        ws.Cell(2, 1).Value = "";              ws.Cell(3, 1).Value = "P-9001";
        ws.Cell(2, 2).Value = "روغن ترمز DOT4"; ws.Cell(3, 2).Value = "کمک فنر جلو";
        ws.Cell(2, 3).Value = "روغن";           ws.Cell(3, 3).Value = "جلوبندی";
        ws.Cell(2, 4).Value = "لیتر";           ws.Cell(3, 4).Value = "عدد";
        ws.Cell(2, 5).Value = 450000;           ws.Cell(3, 5).Value = 3200000;
        ws.Cell(2, 6).Value = 590000;           ws.Cell(3, 6).Value = 4100000;
        ws.Cell(2, 7).Value = 10;               ws.Cell(3, 7).Value = 4;
        ws.Cell(2, 8).Value = 60;               ws.Cell(3, 8).Value = 20;
        ws.Cell(2, 9).Value = "6260001112223";  ws.Cell(3, 9).Value = "";
        ws.Cell(2, 10).Value = "سطر نمونه — قابل حذف"; ws.Cell(3, 10).Value = "سطر نمونه — قابل حذف";

        ws.Columns(1, 10).AdjustToContents();

        // راهنما در کاربرگ دوم
        var help = wb.Worksheets.Add("راهنما");
        help.RightToLeft = true;
        help.Cell(1, 1).Value = "راهنمای ورود کالا از اکسل";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(3, 1).Value = "• داده‌ها را از سطر ۲ کاربرگ «کالاها» وارد کنید (سطر ۱ عنوان است).";
        help.Cell(4, 1).Value = "• «نام کالا» اجباری است؛ بقیه ستون‌ها اختیاری‌اند.";
        help.Cell(5, 1).Value = "• «کد کالا» خالی باشد، خودکار ساخته می‌شود؛ کد تکراری خطا می‌دهد.";
        help.Cell(6, 1).Value = "• گروه یا واحد جدید باشد، خودکار در اطلاعات پایه ساخته می‌شود.";
        help.Cell(7, 1).Value = "• دو سطر نمونه در کاربرگ «کالاها» را قبل از ورود نهایی حذف کنید.";
        help.Columns(1, 1).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // =============================== کالا ===============================

    public async Task<PagedResult<Product>> GetProductsAsync(string? search, bool belowReorderOnly, int page, int pageSize, int? warehouseId = null)
    {
        var q = _db.Products.AsNoTracking().AsQueryable();

        // فیلتر انبار: کالاهای همان انبار + کالاهای بدون انبار اختصاصی + خدمات
        if (warehouseId is > 0)
            q = q.Where(p => p.IsService || p.WarehouseId == null || p.WarehouseId == warehouseId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s) || (p.Category != null && p.Category.Contains(s)) || (p.Barcode != null && p.Barcode.Contains(s)) || (p.TaxCode != null && p.TaxCode.Contains(s)));
        }

        // Filtering belongs to the same SQL query as COUNT and OFFSET/LIMIT.
        if (belowReorderOnly)
        {
            if (_db.Database.IsSqlite())
                q = q.Where(p => p.ReorderPoint > 0 &&
                    Math.Round(_db.Stocks.Where(s => s.ProductId == p.Id)
                        .Sum(s => (double?)s.Quantity) ?? 0, 3) <= (double)p.ReorderPoint);
            else
                q = q.Where(p => p.ReorderPoint > 0 &&
                    (_db.Stocks.Where(s => s.ProductId == p.Id).Sum(s => (decimal?)s.Quantity) ?? 0) <= p.ReorderPoint);
        }

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var total = await q.CountAsync();
        var products = await q.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        var ids = products.Select(p => p.Id).ToList();
        var stocks = await _db.Stocks.AsNoTracking().Where(s => ids.Contains(s.ProductId)).ToListAsync();
        var warehouseIds = products.Where(p => p.WarehouseId != null).Select(p => p.WarehouseId!.Value).ToList();
        var whNames = await _db.Warehouses.Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);
        return new PagedResult<Product>
        {
            Items = products.Select(p => ToProductDto(p, stocks, whNames)).ToList(),
            TotalCount = total, Page = page, PageSize = pageSize
        };
    }

    public Task<List<LookupItem>> GetProductLookupsAsync(Paging.Request? pagination = null)
        => _db.Products.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.Code + " — " + p.Name })
            .ToPageListAsync(pagination, defaultCap: 100000);

    private static Product ToProductDto(Db.Product p, List<Db.Stock> stocks, Dictionary<int, string>? whNames = null)
    {
        var ps = stocks.Where(s => s.ProductId == p.Id).ToList();
        var qty = ps.Sum(s => s.Quantity);
        var totalCost = ps.Sum(s => s.Quantity * s.AvgCost);
        var avg = qty > 0 ? totalCost / qty : p.PurchasePrice;
        return new Product
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Unit = p.Unit,
            Category = p.Category,
            Barcode = p.Barcode,
            TaxCode = p.TaxCode,
            SalePrice = p.SalePrice,
            PurchasePrice = p.PurchasePrice,
            ReorderPoint = p.ReorderPoint,
            MaxStock = p.MaxStock,
            Description = p.Description,
            IsActive = p.IsActive,
            IsService = p.IsService,
            WarehouseId = p.WarehouseId,
            WarehouseName = p.WarehouseId.HasValue && whNames != null && whNames.TryGetValue(p.WarehouseId.Value, out var wn) ? wn : null,
            CreatedAt = p.CreatedAt,
            TotalStock = qty,
            AvgCost = avg
        };
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p is null) return null;
        var stocks = await _db.Stocks.Where(s => s.ProductId == id).ToListAsync();
        var whNames = await _db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Name);
        return ToProductDto(p, stocks, whNames);
    }

    public async Task<Product> SaveProductAsync(Product dto)
    {
        Db.Product entity;
        if (dto.Id == 0)
        {
            entity = new Db.Product { CreatedAt = DateTime.Now };
            _db.Products.Add(entity);
        }
        else
        {
            entity = await _db.Products.FindAsync(dto.Id) ?? throw new InvalidOperationException("کالا یافت نشد.");
        }

        entity.Name = dto.Name.Trim();
        entity.IsService = dto.IsService;

        if (dto.IsService)
        {
            // خدمات: بدون گروه، بارکد، واحد، قیمت خرید، نقطه سفارش، حداکثر موجودی و انبار
            entity.Unit = "خدمت";
            entity.Category = null;
            entity.Barcode = null;
            entity.SalePrice = dto.SalePrice;
            entity.PurchasePrice = 0;
            entity.ReorderPoint = 0;
            entity.MaxStock = 0;
            entity.WarehouseId = null;
        }
        else
        {
            entity.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "عدد" : dto.Unit.Trim();
            entity.Category = NullIfEmpty(dto.Category);

            // اگر گروه واردشده در جدول گروه‌های کالا نبود، خودکار اضافه می‌شود
            if (entity.Category != null &&
                !await _db.ProductCategories.AnyAsync(c => c.Name == entity.Category))
            {
                _db.ProductCategories.Add(new Db.ProductCategory { Name = entity.Category, CreatedAt = DateTime.Now });
            }
            entity.Barcode = NullIfEmpty(dto.Barcode);
            entity.SalePrice = dto.SalePrice;
            entity.PurchasePrice = dto.PurchasePrice;
            entity.ReorderPoint = dto.ReorderPoint;
            entity.MaxStock = dto.MaxStock;
            entity.WarehouseId = dto.WarehouseId is > 0 ? dto.WarehouseId : null;
        }

        entity.Description = NullIfEmpty(dto.Description);
        var taxCode = NullIfEmpty(dto.TaxCode)?.Trim();
        if (taxCode is { Length: < 5 or > 20 })
            throw new InvalidOperationException("شناسه مالیاتی کالا باید بین ۵ تا ۲۰ نویسه باشد.");
        if (taxCode is not null && await _db.Products.AnyAsync(p => p.TaxCode == taxCode && p.Id != entity.Id))
            throw new InvalidOperationException($"شناسه مالیاتی «{taxCode}» برای کالای دیگری ثبت شده است.");
        entity.TaxCode = taxCode;
        entity.IsActive = dto.IsActive;

        if (string.IsNullOrWhiteSpace(dto.Code))
        {
            entity.Code = await GenerateProductCodeAsync(entity.Id, dto.IsService);
        }
        else
        {
            var code = dto.Code.Trim();
            var dup = await _db.Products.AnyAsync(p => p.Code == code && p.Id != entity.Id);
            if (dup) throw new InvalidOperationException(dto.IsService ? "کد خدمت تکراری است." : "کد کالا تکراری است.");
            entity.Code = code;
        }

        await _db.SaveChangesAsync();
        return await GetProductAsync(entity.Id) ?? throw new InvalidOperationException();
    }

    private async Task<string> GenerateProductCodeAsync(int id, bool isService = false)
    {
        var prefix = isService ? "S" : "P";
        string code;
        int n = id == 0 ? await _db.Products.CountAsync() + 1 : id;
        do
        {
            code = $"{prefix}-{n:0000}";
            n++;
        } while (await _db.Products.AnyAsync(p => p.Code == code));
        return code;
    }

    public async Task DeleteProductAsync(int id)
    {
        var used = await _db.TransactionLines.AnyAsync(l => l.ProductId == id);
        if (used) throw new InvalidOperationException("این کالا دارای سوابق انبار است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var p = await _db.Products.FindAsync(id);
        if (p is null) return;
        var stocks = await _db.Stocks.Where(s => s.ProductId == id).ToListAsync();
        _db.Stocks.RemoveRange(stocks);
        _db.Products.Remove(p);
        await _db.SaveChangesAsync();
    }

    // =============================== انبار ===============================

    public async Task<List<Warehouse>> GetWarehousesAsync(Paging.Request? pagination = null)
    {
        var list = await _db.Warehouses.OrderBy(w => w.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var ids = list.Select(w => w.Id).ToList();
        var stocks = await _db.Stocks.Where(s => ids.Contains(s.WarehouseId)).ToListAsync();
        return list.Select(w => new Warehouse
        {
            Id = w.Id,
            Name = w.Name,
            Address = w.Address,
            Phone = w.Phone,
            Note = w.Note,
            IsActive = w.IsActive,
            ItemCount = stocks.Count(s => s.WarehouseId == w.Id && s.Quantity > 0)
        }).ToList();
    }

    public async Task<Warehouse> SaveWarehouseAsync(Warehouse dto)
    {
        Db.Warehouse entity;
        if (dto.Id == 0) { entity = new Db.Warehouse(); _db.Warehouses.Add(entity); }
        else entity = await _db.Warehouses.FindAsync(dto.Id) ?? throw new InvalidOperationException("انبار یافت نشد.");

        entity.Name = dto.Name.Trim();
        entity.Address = NullIfEmpty(dto.Address);
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.Note = NullIfEmpty(dto.Note);
        entity.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();
        return (await GetWarehousesAsync()).First(w => w.Id == entity.Id);
    }

    public async Task DeleteWarehouseAsync(int id)
    {
        var used = await _db.Transactions.AnyAsync(t => t.WarehouseId == id);
        if (used) throw new InvalidOperationException("این انبار دارای سوابق است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var w = await _db.Warehouses.FindAsync(id);
        if (w is null) return;
        _db.Warehouses.Remove(w);
        await _db.SaveChangesAsync();
    }

    // =============================== طرف حساب ===============================

    /// <param name="referrerScope">
    /// اگر مقدار داشته باشد، فقط طرف حساب‌های همین پروندهٔ معرف برگردانده می‌شوند
    /// (ویزیتوری که مجوز دیدن کل مشتریان را ندارد).
    /// </param>
    public async Task<List<Party>> GetPartiesAsync(PartyType type, Paging.Request? pagination = null,
        int? referrerScope = null)
    {
        var q = _db.Parties.Where(p => p.Type == type);
        if (referrerScope is > 0)
            q = q.Where(p => p.ReferrerId == referrerScope || p.RegisteredByReferrerId == referrerScope);

        var list = await q.OrderBy(p => p.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var ids = list.Select(p => p.Id).ToList();
        var txns = await _db.Transactions.Include(t => t.Cheques).Include(t => t.Installments)
            .Where(t => t.PartyId != null && ids.Contains(t.PartyId.Value)).ToListAsync();
        var refNames = await _db.Referrers.ToDictionaryAsync(r => r.Id, r => r.Name);
        var today = DateTime.Today;

        return list.Select(p =>
        {
            var pTxns = txns.Where(t => t.PartyId == p.Id).ToList();
            var bounced = pTxns.SelectMany(t => t.Cheques).Where(c => c.Status == ChequeStatus.Bounced).ToList();
            var overdueInst = pTxns.SelectMany(t => t.Installments).Where(i => !i.IsPaid && i.DueDate.Date < today).ToList();

            return new Party
            {
                Id = p.Id,
                Type = p.Type,
                Name = p.Name,
                Phone = p.Phone,
                Mobile = p.Mobile,
                Address = p.Address,
                TaxId = p.TaxId,
                PostalCode = p.PostalCode,
                Note = p.Note,
                IsActive = p.IsActive,
                ReferrerId = p.ReferrerId,
                ReferrerName = p.ReferrerId.HasValue && refNames.TryGetValue(p.ReferrerId.Value, out var rn) ? rn : null,
                RegisteredByUserId = p.RegisteredByUserId,
                RegisteredByName = p.RegisteredByName,
                RegisteredByReferrerId = p.RegisteredByReferrerId,
                RegisteredByReferrerName = p.RegisteredByReferrerId.HasValue
                                           && refNames.TryGetValue(p.RegisteredByReferrerId.Value, out var rrn) ? rrn : null,
                RegisteredAt = p.RegisteredAt,
                Balance = pTxns.Sum(t => t.Type == TransactionType.Sale ? t.Amount : -t.Amount),
                BouncedChequesCount = bounced.Count,
                BouncedChequesTotal = bounced.Sum(c => c.Amount),
                OverdueInstallmentsCount = overdueInst.Count,
                OverdueInstallmentsTotal = overdueInst.Sum(i => i.Amount)
            };
        }).ToList();
    }

    public async Task<Party> SavePartyAsync(Party dto)
    {
        Db.Party entity;
        if (dto.Id == 0) { entity = new Db.Party(); _db.Parties.Add(entity); }
        else entity = await _db.Parties.FindAsync(dto.Id) ?? throw new InvalidOperationException("طرف حساب یافت نشد.");

        entity.Type = dto.Type;
        entity.Name = dto.Name.Trim();
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.Mobile = NullIfEmpty(dto.Mobile);
        entity.Address = NullIfEmpty(dto.Address);
        entity.TaxId = NullIfEmpty(dto.TaxId)?.Trim();
        entity.PostalCode = NullIfEmpty(dto.PostalCode)?.Trim();
        if (entity.TaxId is not null && (entity.TaxId.Length is not (10 or 11 or 12 or 14) || entity.TaxId.Any(c => !char.IsAsciiDigit(c))))
            throw new InvalidOperationException("شناسه مالیاتی خریدار باید ۱۰، ۱۱، ۱۲ یا ۱۴ رقم لاتین باشد.");
        if (entity.PostalCode is not null && (entity.PostalCode.Length != 10 || entity.PostalCode.Any(c => !char.IsAsciiDigit(c))))
            throw new InvalidOperationException("کد پستی خریدار باید دقیقاً ۱۰ رقم لاتین باشد.");
        entity.Note = NullIfEmpty(dto.Note);
        entity.IsActive = dto.IsActive;
        // معرف فقط برای مشتری معنا دارد؛ مقدار 0/null = بدون معرف
        entity.ReferrerId = dto.Type == PartyType.Customer && dto.ReferrerId is > 0 ? dto.ReferrerId : null;

        // «چه کسی این طرف حساب را ثبت کرد» فقط در ایجاد نوشته می‌شود؛ ویرایش آن را عوض نمی‌کند.
        if (dto.Id == 0)
        {
            entity.RegisteredByUserId = dto.RegisteredByUserId is > 0 ? dto.RegisteredByUserId : null;
            entity.RegisteredByName = NullIfEmpty(dto.RegisteredByName);
            entity.RegisteredByReferrerId = dto.RegisteredByReferrerId is > 0 ? dto.RegisteredByReferrerId : null;
            entity.RegisteredAt = dto.RegisteredAt ?? DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return (await GetPartiesAsync(dto.Type)).First(p => p.Id == entity.Id);
    }

    public async Task DeletePartyAsync(int id)
    {
        var used = await _db.Transactions.AnyAsync(t => t.PartyId == id);
        if (used) throw new InvalidOperationException("این طرف حساب دارای سوابق است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var p = await _db.Parties.FindAsync(id);
        if (p is null) return;
        _db.Parties.Remove(p);
        await _db.SaveChangesAsync();
    }

    // =============================== موجودی ===============================

    public async Task<PagedResult<StockItem>> GetStockAsync(int? warehouseId, string? search, bool belowOnly, int page, int pageSize)
    {
        var query = from stock in _db.Stocks.AsNoTracking()
                    join product in _db.Products on stock.ProductId equals product.Id
                    join warehouse in _db.Warehouses on stock.WarehouseId equals warehouse.Id into warehouses
                    from warehouse in warehouses.DefaultIfEmpty()
                    select new StockItem
                    {
                        WarehouseId = stock.WarehouseId, WarehouseName = warehouse == null ? null : warehouse.Name,
                        ProductId = product.Id, ProductName = product.Name, ProductCode = product.Code,
                        Unit = product.Unit, Category = product.Category,
                        Quantity = stock.Quantity, AvgCost = stock.AvgCost, LastSalePrice = product.SalePrice,
                        ReorderPoint = product.ReorderPoint, MaxStock = product.MaxStock
                    };
        if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.ProductName.Contains(term) || r.ProductCode.Contains(term));
        }
        if (belowOnly) query = query.Where(r => r.ReorderPoint > 0 && r.Quantity <= r.ReorderPoint);
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var total = await query.CountAsync();
        var items = await query.OrderBy(r => r.ProductName).ThenBy(r => r.WarehouseName)
            .ThenBy(r => r.ProductId).ThenBy(r => r.WarehouseId)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        return new PagedResult<StockItem> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task AdjustStockAsync(AdjustmentCommand cmd)
    {
        if (cmd.Quantity < 0) throw new InvalidOperationException("مقدار موجودی نمی‌تواند منفی باشد.");
        var p = await _db.Products.FindAsync(cmd.ProductId) ?? throw new InvalidOperationException("کالا یافت نشد.");

        var date = cmd.Date == default ? DateTime.Now : cmd.Date;

        var txn = new Db.Transaction
        {
            Number = await GenerateNumberAsync(TransactionType.Adjustment),
            Type = TransactionType.Adjustment,
            Date = date,
            Description = cmd.Description,
            WarehouseId = cmd.WarehouseId,
            CreatedAt = DateTime.Now
        };
        txn.Lines.Add(new Db.TransactionLine { ProductId = cmd.ProductId, Quantity = cmd.Quantity, Price = p.PurchasePrice, Description = "اصلاح موجودی" });

        await using var trx = await _db.Database.BeginTransactionAsync();
        _db.Transactions.Add(txn);
        await _db.SaveChangesAsync();  // ابتدا سند ذخیره می‌شود تا در بازمحاسبه دیده شود
        await RecalcStockAsync(cmd.WarehouseId, cmd.ProductId);
        await _db.SaveChangesAsync();
        await trx.CommitAsync();
    }

    // =============================== خرید و فروش ===============================

    public async Task<Order> CreateOrderAsync(OrderCommand cmd)
    {
        if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک سطر کالا وارد کنید.");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");
        if (cmd.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2)))
            throw new InvalidOperationException("مقدار هر سطر حداکثر تا دو رقم اعشار قابل ثبت است.");
        if (cmd.WarehouseId == 0) throw new InvalidOperationException("انبار را انتخاب کنید.");
        if (cmd.PartyId == 0) throw new InvalidOperationException("طرف حساب را انتخاب کنید.");

        var productIds = cmd.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var date = cmd.Date == default ? DateTime.Now : cmd.Date;
        var settings = await GetSettingsAsync();

        // خدمات فقط در فروش مجاز است
        if (cmd.Type == TransactionType.Purchase)
        {
            var svc = cmd.Lines.FirstOrDefault(l => products.TryGetValue(l.ProductId, out var p) && p.IsService);
            if (svc is not null)
            {
                var name = products.TryGetValue(svc.ProductId, out var pp) ? pp.Name : "";
                throw new InvalidOperationException($"«{name}» از نوع خدمات است و در سند خرید قابل انتخاب نیست.");
            }
        }

        // کالای دارای انبار اختصاصی فقط در همان انبار قابل خرید/فروش است
        foreach (var l in cmd.Lines)
        {
            if (products.TryGetValue(l.ProductId, out var prod) &&
                !prod.IsService && prod.WarehouseId is > 0 && prod.WarehouseId != cmd.WarehouseId)
            {
                throw new InvalidOperationException($"کالای «{prod.Name}» مخصوص انبار دیگری است و در این انبار قابل ثبت نیست.");
            }
        }

        // بررسی موجودی کافی برای فروش (خدمات موجودی ندارد — طبق تنظیمات)
        if (cmd.Type == TransactionType.Sale && !settings.AllowNegativeStock)
        {
            var stocks = await _db.Stocks.Where(s => s.WarehouseId == cmd.WarehouseId && productIds.Contains(s.ProductId)).ToListAsync();
            foreach (var l in cmd.Lines)
            {
                if (products.TryGetValue(l.ProductId, out var prod) && prod.IsService) continue;
                var stock = stocks.FirstOrDefault(s => s.ProductId == l.ProductId);
                var available = stock?.Quantity ?? 0;
                if (available < l.Quantity)
                {
                    var name = products.TryGetValue(l.ProductId, out var pp) ? pp.Name : "";
                    throw new InvalidOperationException($"موجودی «{name}» کافی نیست (موجود: {available:0.##}).");
                }
            }
        }

        // معرف سند فروش: اگر از کلاینت نیامده باشد، خودکار از مشتری خوانده می‌شود (فاز ۹)
        int? saleReferrerId = null;
        if (cmd.Type == TransactionType.Sale)
        {
            if (cmd.ReferrerId is > 0) saleReferrerId = cmd.ReferrerId;
            else if (cmd.PartyId > 0)
                saleReferrerId = await _db.Parties.Where(p => p.Id == cmd.PartyId)
                    .Select(p => p.ReferrerId).FirstOrDefaultAsync();
        }

        var txn = new Db.Transaction
        {
            Number = await GenerateNumberAsync(cmd.Type),
            Type = cmd.Type,
            Date = date,
            Description = cmd.Description,
            WarehouseId = cmd.WarehouseId,
            PartyId = cmd.PartyId,
            ReferrerId = saleReferrerId,
            CreatedAt = DateTime.Now
        };

        // پرداخت سند (خرید و فروش): نقدی / نسیه / چک / اقساط
        if (cmd.Type is TransactionType.Sale or TransactionType.Purchase)
            ApplyPayment(txn, cmd);

        foreach (var l in cmd.Lines)
        {
            var gross = l.Quantity * l.Price;
            decimal discAmount = 0;
            decimal discPercent = 0;
            if (l.DiscountIsAmount)
            {
                discAmount = Math.Clamp(Math.Round(l.DiscountAmount, 0), 0, Math.Max(0, gross));
                discPercent = gross > 0 ? Math.Round(discAmount * 100m / gross, 2) : 0;
            }
            else if (l.DiscountPercent > 0)
            {
                discPercent = Math.Clamp(l.DiscountPercent, 0, 100);
                discAmount = Math.Round(gross * discPercent / 100m, 0);
            }

            txn.Lines.Add(new Db.TransactionLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                Price = l.Price,
                DiscountPercent = discPercent,
                DiscountAmount = discAmount,
                DiscountIsAmount = l.DiscountIsAmount,
                UnitCostSnapshot = l.UnitCostSnapshot
            });
            txn.Amount += Math.Max(0, gross - discAmount);
        }

        await using var trx = await _db.Database.BeginTransactionAsync();
        _db.Transactions.Add(txn);
        await _db.SaveChangesAsync();  // ابتدا سند ذخیره می‌شود تا در بازمحاسبه دیده شود

        foreach (var pid in productIds)
            await RecalcStockAsync(cmd.WarehouseId, pid);

        await _db.SaveChangesAsync();

        // کنترل نهایی: با احتساب همه اسناد (حتی اسناد آینده) موجودی منفی نشود
        try
        {
            await EnsureNoNegativeStockAsync(productIds.Select(pid => (cmd.WarehouseId, pid)));
        }
        catch
        {
            await trx.RollbackAsync();
            throw;
        }

        await trx.CommitAsync();

        return await GetOrderAsync(txn.Id) ?? throw new InvalidOperationException();
    }

    public async Task<Order?> GetOrderAsync(int id)
    {
        var t = await _db.Transactions.Include(x => x.Lines)
            .Include(x => x.Cheques).Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return null;
        return await ToOrderDtoAsync(t);
    }

    public async Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from, DateTime? to, int? partyId, int? warehouseId, int page, int pageSize)
    {
        var q = _db.Transactions.Include(x => x.Lines)
            .Include(x => x.Cheques).Include(x => x.Installments)
            .Where(t => t.Type == type);
        if (from.HasValue) q = q.Where(t => t.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(t => t.Date < to.Value.Date.AddDays(1));
        if (partyId.HasValue) q = q.Where(t => t.PartyId == partyId.Value);
        if (warehouseId.HasValue) q = q.Where(t => t.WarehouseId == warehouseId.Value);

        var total = await q.CountAsync();
        var pageItems = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                               .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var items = new List<Order>();
        foreach (var t in pageItems) items.Add(await ToOrderDtoAsync(t));

        return new PagedResult<Order> { Items = items, TotalCount = total };
    }

    private async Task<Order> ToOrderDtoAsync(Db.Transaction t)
    {
        var products = await _db.Products.Where(p => t.Lines.Select(l => l.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        string? partyName = null;
        if (t.PartyId.HasValue)
            partyName = await _db.Parties.Where(p => p.Id == t.PartyId.Value).Select(p => p.Name).FirstOrDefaultAsync();
        string? whName = await _db.Warehouses.Where(w => w.Id == t.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync();
        string? refName = t.ReferrerId.HasValue
            ? await _db.Referrers.Where(r => r.Id == t.ReferrerId.Value).Select(r => r.Name).FirstOrDefaultAsync()
            : null;

        string? trsAccountName = null;
        if (t.TrsAccountId.HasValue)
            trsAccountName = await _db.TrsAccounts.Where(a => a.Id == t.TrsAccountId.Value).Select(a => a.Name).FirstOrDefaultAsync();

        var order = new Order
        {
            Id = t.Id,
            WarehouseId = t.WarehouseId,
            WarehouseName = whName,
            PartyId = t.PartyId ?? 0,
            PartyName = partyName,
            ReferrerId = t.ReferrerId,
            ReferrerName = refName,
            Type = t.Type,
            Number = t.Number,
            Date = t.Date,
            Description = t.Description ?? "",
            TotalAmount = t.Amount,
            PaymentMethod = t.PaymentMethod,
            CashType = t.CashType,
            CashAmount = t.CashAmount,
            TrsAccountId = t.TrsAccountId,
            TrsAccountName = trsAccountName,
            DueDate = t.DueDate,
            SettledAmount = t.SettledAmount,
            Cheques = t.Cheques.OrderBy(c => c.DueDate).Select(c => new ChequeDto
            {
                Id = c.Id, Number = c.Number, TrsAccountId = c.TrsAccountId, BankName = c.BankName, AccountInfo = c.AccountInfo,
                OwnerName = c.OwnerName, Amount = c.Amount, DueDate = c.DueDate,
                IsCleared = c.IsCleared, ClearedAt = c.ClearedAt, Note = c.Note
            }).ToList(),
            Installments = t.Installments.OrderBy(i => i.No).Select(i => new InstallmentDto
            {
                Id = i.Id, No = i.No, Amount = i.Amount, DueDate = i.DueDate, IsPaid = i.IsPaid, PaidAt = i.PaidAt
            }).ToList(),
            Lines = t.Lines.OrderBy(l => l.Id).Select(l => new OrderLine
            {
                Id = l.Id,
                ProductId = l.ProductId,
                ProductName = products.TryGetValue(l.ProductId, out var p) ? p.Name : "",
                Unit = products.TryGetValue(l.ProductId, out var p2) ? p2.Unit : "",
                IsService = products.TryGetValue(l.ProductId, out var p3) && p3.IsService,
                Quantity = l.Quantity,
                Price = l.Price,
                DiscountPercent = l.DiscountPercent,
                DiscountAmount = l.DiscountAmount,
                DiscountIsAmount = l.DiscountIsAmount,
                UnitCost = l.UnitCostSnapshot
            }).ToList()
        };

        // محاسبه سود هر سطر و پورسانت معرف (فقط برای فروش)
        await EnrichSaleProfitAsync(order);

        return order;
    }

    /// <summary>ویرایش سند خرید/فروش: جایگزینی سرصفحه و اقلام + بازمحاسبه موجودی انبارهای قدیم و جدید.</summary>
    public async Task<Order> UpdateOrderAsync(int id, OrderCommand cmd)
    {
        if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک سطر کالا وارد کنید.");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");
        if (cmd.WarehouseId == 0) throw new InvalidOperationException("انبار را انتخاب کنید.");
        if (cmd.PartyId == 0) throw new InvalidOperationException("طرف حساب را انتخاب کنید.");

        var txn = await _db.Transactions.Include(t => t.Lines)
            .Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new InvalidOperationException("سند یافت نشد.");
        if (txn.Type is not (TransactionType.Purchase or TransactionType.Sale))
            throw new InvalidOperationException("فقط اسناد خرید و فروش قابل ویرایش هستند.");

        var existingLineQuantities = txn.Lines.ToDictionary(l => l.Id, l => (l.ProductId, l.Quantity));
        var existingLineCosts = txn.Lines.ToDictionary(l => l.Id, l => (l.ProductId, l.UnitCostSnapshot));
        if (cmd.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2)
                               && (!existingLineQuantities.TryGetValue(l.Id, out var original)
                                   || original.ProductId != l.ProductId || original.Quantity != l.Quantity)))
            throw new InvalidOperationException("مقدار هر سطر حداکثر تا دو رقم اعشار قابل ثبت است؛ فقط مقدار قدیمیِ بدون تغییر قابل حفظ است.");

        if (txn.Type is TransactionType.Sale or TransactionType.Purchase)
            ApplyPayment(txn, cmd);

        var productIds = cmd.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        // کالای دارای انبار اختصاصی فقط در همان انبار قابل ثبت است
        foreach (var l in cmd.Lines)
        {
            if (products.TryGetValue(l.ProductId, out var prod) &&
                !prod.IsService && prod.WarehouseId is > 0 && prod.WarehouseId != cmd.WarehouseId)
            {
                throw new InvalidOperationException($"کالای «{prod.Name}» مخصوص انبار دیگری است و در این انبار قابل ثبت نیست.");
            }
        }

        // جفت‌های (انبار، کالا) متأثر: هم اقلام قدیم و هم جدید
        var affected = txn.Lines.Select(l => (Wh: txn.WarehouseId, Pid: l.ProductId))
            .Concat(cmd.Lines.Select(l => (Wh: cmd.WarehouseId, Pid: l.ProductId)))
            .Distinct().ToList();

        await using var trx = await _db.Database.BeginTransactionAsync();

        // جایگزینی اقلام و سرصفحه
        _db.TransactionLines.RemoveRange(txn.Lines);
        txn.Lines.Clear();
        txn.WarehouseId = cmd.WarehouseId;
        txn.PartyId = cmd.PartyId;
        // معرف سند فروش: اگر از کلاینت نیامده باشد، خودکار از مشتری خوانده می‌شود (فاز ۹)
        int? updReferrerId = null;
        if (txn.Type == TransactionType.Sale)
        {
            if (cmd.ReferrerId is > 0) updReferrerId = cmd.ReferrerId;
            else if (cmd.PartyId > 0)
                updReferrerId = await _db.Parties.Where(p => p.Id == cmd.PartyId)
                    .Select(p => p.ReferrerId).FirstOrDefaultAsync();
        }
        txn.ReferrerId = updReferrerId;
        txn.Date = cmd.Date == default ? txn.Date : cmd.Date;
        txn.Description = cmd.Description;
        txn.Amount = 0;
        foreach (var l in cmd.Lines)
        {
            var unitCostSnapshot = existingLineCosts.TryGetValue(l.Id, out var oldCost) && oldCost.ProductId == l.ProductId
                ? oldCost.UnitCostSnapshot
                : l.UnitCostSnapshot;

            var gross = l.Quantity * l.Price;
            decimal discAmount = 0;
            decimal discPercent = 0;
            if (l.DiscountIsAmount)
            {
                discAmount = Math.Clamp(Math.Round(l.DiscountAmount, 0), 0, Math.Max(0, gross));
                discPercent = gross > 0 ? Math.Round(discAmount * 100m / gross, 2) : 0;
            }
            else if (l.DiscountPercent > 0)
            {
                discPercent = Math.Clamp(l.DiscountPercent, 0, 100);
                discAmount = Math.Round(gross * discPercent / 100m, 0);
            }

            txn.Lines.Add(new Db.TransactionLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                Price = l.Price,
                DiscountPercent = discPercent,
                DiscountAmount = discAmount,
                DiscountIsAmount = l.DiscountIsAmount,
                UnitCostSnapshot = unitCostSnapshot
            });
            txn.Amount += Math.Max(0, gross - discAmount);
        }
        await _db.SaveChangesAsync();

        // بازمحاسبه موجودی همه جفت‌های متأثر
        foreach (var (wh, pid) in affected)
            await RecalcStockAsync(wh, pid);
        await _db.SaveChangesAsync();

        // اعتبارسنجی: هیچ موجودی‌ای نباید منفی شود (با احتساب اسناد بعدی — طبق تنظیمات)
        try
        {
            await EnsureNoNegativeStockAsync(affected);
        }
        catch
        {
            await trx.RollbackAsync();
            throw;
        }

        await trx.CommitAsync();
        return await GetOrderAsync(txn.Id) ?? throw new InvalidOperationException();
    }

    public async Task DeleteOrderAsync(int id)
    {
        var t = await _db.Transactions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("سند یافت نشد.");

        var productIds = t.Lines.Select(l => l.ProductId).Distinct().ToList();
        var wh = t.WarehouseId;

        await using var trx = await _db.Database.BeginTransactionAsync();
        _db.Transactions.Remove(t);
        await _db.SaveChangesAsync();  // ابتدا حذف اعمال می‌شود تا از بازمحاسبه خارج شود

        foreach (var pid in productIds)
            await RecalcStockAsync(wh, pid);

        await _db.SaveChangesAsync();

        // کنترل: حذف سند خرید نباید موجودی را منفی کند (مثلاً وقتی کالای آن قبلاً فروخته شده)
        try
        {
            await EnsureNoNegativeStockAsync(productIds.Select(pid => (wh, pid)));
        }
        catch
        {
            await trx.RollbackAsync();
            throw;
        }

        await trx.CommitAsync();
    }

    /// <summary>آخرین سند خرید شامل کالای موردنظر.</summary>
    public async Task<Order?> GetLastPurchaseAsync(int productId)
    {
        var t = await _db.Transactions.Include(x => x.Lines)
            .Where(x => x.Type == TransactionType.Purchase && x.Lines.Any(l => l.ProductId == productId))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (t is null) return null;
        return await ToOrderDtoAsync(t);
    }

    public async Task<decimal> SuggestPriceAsync(int productId, TransactionType type)
    {
        var p = await _db.Products.FindAsync(productId);
        if (p is null) return 0;
        if (type == TransactionType.Sale)
        {
            var last = await _db.Transactions.Include(x => x.Lines)
                .Where(t => t.Type == TransactionType.Sale && t.Lines.Any(l => l.ProductId == productId))
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync();
            if (last != null)
            {
                var line = last.Lines.First(l => l.ProductId == productId);
                return line.Price;
            }
            return p.SalePrice;
        }
        return p.PurchasePrice;
    }

    // =============================== کاردکس ===============================

    public async Task<List<KardexRow>> GetKardexAsync(int productId, int? warehouseId, DateTime? from, DateTime? to, Paging.Request? pagination = null)
    {
        var source = _db.Transactions.AsNoTracking()
            .Where(t => !warehouseId.HasValue || t.WarehouseId == warehouseId.Value)
            .SelectMany(t => t.Lines.Where(l => l.ProductId == productId), (t, l) => new
            {
                TransactionId = t.Id, LineId = l.Id, t.Date, t.Type, t.Number, t.PartyId, t.Description,
                l.Quantity, l.Price
            });
        var filtered = source;
        if (from.HasValue) { var start = from.Value.Date; filtered = filtered.Where(r => r.Date >= start); }
        if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
        {
            var end = to.Value.Date.AddDays(1);
            filtered = filtered.Where(r => r.Date < end);
        }
        var selected = await filtered.OrderBy(r => r.Date).ThenBy(r => r.TransactionId).ThenBy(r => r.LineId)
            .ToPageListAsync(pagination);
        if (selected.Count == 0) return new();

        // The first row's opening balance includes history before both the date
        // filter and offset. Adjustment/Initial replace (rather than add to) stock.
        var first = selected[0];
        var preceding = source.Where(r => r.Date < first.Date || (r.Date == first.Date &&
            (r.TransactionId < first.TransactionId || (r.TransactionId == first.TransactionId && r.LineId < first.LineId))));
        var reset = await preceding.Where(r => r.Type != TransactionType.Sale && r.Type != TransactionType.Purchase)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.TransactionId).ThenByDescending(r => r.LineId)
            .FirstOrDefaultAsync();
        decimal balance = reset?.Quantity ?? 0;
        if (reset != null)
            preceding = preceding.Where(r => r.Date > reset.Date || (r.Date == reset.Date &&
                (r.TransactionId > reset.TransactionId || (r.TransactionId == reset.TransactionId && r.LineId > reset.LineId))));
        var deltas = preceding.Where(r => r.Type == TransactionType.Sale || r.Type == TransactionType.Purchase)
            .Select(r => r.Type == TransactionType.Sale ? -r.Quantity : r.Quantity);
        balance += _db.Database.IsSqlite()
            ? (decimal)Math.Round(await deltas.SumAsync(q => (double?)q) ?? 0, 3)
            : await deltas.SumAsync(q => (decimal?)q) ?? 0m;

        var partyIds = selected.Where(r => r.PartyId.HasValue).Select(r => r.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var rows = new List<KardexRow>();
        foreach (var r in selected)
        {
            decimal inQty = 0, outQty = 0;
            if (r.Type == TransactionType.Sale) { outQty = r.Quantity; balance -= r.Quantity; }
            else if (r.Type == TransactionType.Purchase) { inQty = r.Quantity; balance += r.Quantity; }
            else
            {
                var delta = r.Quantity - balance;
                if (delta >= 0) inQty = delta; else outQty = -delta;
                balance = r.Quantity;
            }
            rows.Add(new KardexRow
            {
                Date = r.Date, Type = r.Type, DocumentNo = r.Number,
                PartyName = r.PartyId.HasValue && parties.TryGetValue(r.PartyId.Value, out var name) ? name : "",
                Description = r.Description ?? "", InQty = inQty, OutQty = outQty,
                Balance = balance, UnitPrice = r.Price
            });
        }
        return rows;
    }

    // =============================== نقطه سفارش ===============================

    public async Task<List<ReorderItem>> GetReorderAsync(int? warehouseId, Paging.Request? pagination = null)
    {
        var products = _db.Products.AsNoTracking().Where(p => p.IsActive && p.ReorderPoint > 0);
        var stocks = _db.Stocks.AsNoTracking().Where(s => !warehouseId.HasValue || s.WarehouseId == warehouseId.Value);
        if (_db.Database.IsSqlite())
        {
            // SQLite has no decimal SUM. Quantities are stored at three decimal places.
            var quantities = products.Select(p => new
            {
                Product = p,
                Quantity = Math.Round(stocks.Where(s => s.ProductId == p.Id).Sum(s => (double?)s.Quantity) ?? 0, 3)
            });
            return await quantities.Where(x => x.Quantity <= (double)x.Product.ReorderPoint)
                .OrderByDescending(x => (double)x.Product.ReorderPoint - x.Quantity).ThenBy(x => x.Product.Id)
                .Select(x => new ReorderItem
                {
                    ProductId = x.Product.Id, ProductCode = x.Product.Code, ProductName = x.Product.Name,
                    Unit = x.Product.Unit, Category = x.Product.Category, ReorderPoint = x.Product.ReorderPoint,
                    MaxStock = x.Product.MaxStock, TotalStock = (decimal)x.Quantity
                }).ToPageListAsync(pagination);
        }
        var query = products.Select(p => new
        {
            Product = p,
            Quantity = stocks.Where(s => s.ProductId == p.Id).Sum(s => (decimal?)s.Quantity) ?? 0m
        });
        return await query.Where(x => x.Quantity <= x.Product.ReorderPoint)
            .OrderByDescending(x => x.Product.ReorderPoint - x.Quantity).ThenBy(x => x.Product.Id)
            .Select(x => new ReorderItem
            {
                ProductId = x.Product.Id, ProductCode = x.Product.Code, ProductName = x.Product.Name,
                Unit = x.Product.Unit, Category = x.Product.Category, ReorderPoint = x.Product.ReorderPoint,
                MaxStock = x.Product.MaxStock, TotalStock = x.Quantity
            }).ToPageListAsync(pagination);
    }

    // =============================== داشبورد ===============================

    public async Task<DashboardSummary> GetDashboardAsync()
    {
        var now = DateTime.Now;
        var products = await _db.Products.CountAsync();
        var warehouses = await _db.Warehouses.CountAsync();
        var customers = await _db.Parties.CountAsync(p => p.Type == PartyType.Customer);
        var suppliers = await _db.Parties.CountAsync(p => p.Type == PartyType.Supplier);

        var stocks = await _db.Stocks.ToListAsync();
        var productList = await _db.Products.Where(p => p.IsActive).ToListAsync();
        int below = 0;
        foreach (var p in productList.Where(p => p.ReorderPoint > 0))
        {
            var qty = stocks.Where(s => s.ProductId == p.Id).Sum(s => s.Quantity);
            if (qty <= p.ReorderPoint) below++;
        }

        var invValue = stocks.Sum(s => s.Quantity * s.AvgCost);

        var txns = await _db.Transactions.ToListAsync();
        var todaySales = txns.Where(t => t.Type == TransactionType.Sale && t.Date.Date == now.Date).Sum(t => t.Amount);
        var todayPurchases = txns.Where(t => t.Type == TransactionType.Purchase && t.Date.Date == now.Date).Sum(t => t.Amount);

        var (jy, jm, _) = PersianDate.FromGregorian(now);
        var monthStart = PersianDate.ToGregorian(jy, jm, 1);
        var monthEnd = PersianDate.ToGregorian(jy, jm == 12 ? 1 : jm + 1, 1).AddDays(-1);

        var monthSales = txns.Where(t => t.Type == TransactionType.Sale && t.Date >= monthStart && t.Date <= monthEnd).Sum(t => t.Amount);
        var monthPurchases = txns.Where(t => t.Type == TransactionType.Purchase && t.Date >= monthStart && t.Date <= monthEnd).Sum(t => t.Amount);

        return new DashboardSummary
        {
            ProductCount = products,
            WarehouseCount = warehouses,
            CustomerCount = customers,
            SupplierCount = suppliers,
            BelowReorderCount = below,
            InventoryValue = invValue,
            TodaySales = todaySales,
            TodayPurchases = todayPurchases,
            MonthSales = monthSales,
            MonthPurchases = monthPurchases,
            LowStockThreshold = 0
        };
    }

    public async Task<List<RecentActivity>> GetRecentAsync(int count)
    {
        var txns = await _db.Transactions.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(count).ToListAsync();
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id);

        return txns.Select(t => new RecentActivity
        {
            Date = t.Date,
            Type = t.Type.ToString(),
            Number = t.Number,
            PartyName = t.PartyId.HasValue && parties.TryGetValue(t.PartyId.Value, out var p) ? p.Name : "",
            Amount = t.Amount
        }).ToList();
    }

    // =============================== ابزار ===============================

    /// <summary>
    /// اعتبارسنجی و اعمال اطلاعات پرداخت روی سند فروش (نقدی/نسیه/چک/اقساط).
    /// پرداخت ترکیبی: در نسیه/چک/اقساط می‌توان «پیش‌دریافت نقدی» هم داشت؛
    /// در این حالت جمع چک‌ها/اقساط باید برابر «مبلغ فاکتور منهای نقد» باشد.
    /// </summary>
    /// <summary>
    /// ثبت پرداخت سند (خرید/فروش) به‌صورت «ترکیبی»:
    /// هر سند می‌تواند هم‌زمان چند جزء داشته باشد — نقد (پیش‌دریافت) + چند چک + چند قسط.
    /// اگر جمع اجزا کمتر از مبلغ سند باشد، مابقی «نسیه» با تاریخ سررسید ثبت می‌شود؛
    /// اگر بیشتر باشد، مازاد همان سود مدت‌دار (چک/اقساط) است و پذیرفته می‌شود.
    /// روش پرداخت مؤثر خودکار تعیین می‌شود: تک‌جزء ⇒ همان جزء، چندجزء ⇒ Mixed.
    /// </summary>
    private static void ApplyPayment(Db.Transaction txn, OrderCommand cmd)
    {
        var total = cmd.Lines.Sum(l => l.Quantity * l.Price);

        txn.TrsAccountId = cmd.TrsAccountId > 0 ? cmd.TrsAccountId : null;
        txn.CashType = null;
        txn.CashAmount = 0;
        txn.DueDate = null;
        txn.SettledAmount = 0;
        txn.Cheques.Clear();
        txn.Installments.Clear();

        // ---------- ۱) جزء نقدی ----------
        // انتخاب صریح «نقدی» یعنی تمام مبلغ نقد است؛ در بقیهٔ روش‌ها نقد یک جزء اختیاری است.
        var cash = cmd.PaymentMethod == PaymentMethod.Cash ? total : Math.Max(0, cmd.CashAmount);
        if (cmd.PaymentMethod != PaymentMethod.Cash && cash > 0 && cash >= total && total > 0)
            throw new InvalidOperationException("پیش‌دریافت نقدی نمی‌تواند برابر یا بیشتر از مبلغ فاکتور باشد؛ در این صورت روش پرداخت را «نقدی» انتخاب کنید.");

        // ---------- ۲) جزء چک ----------
        var chequeSum = 0m;
        foreach (var c in cmd.Cheques)
        {
            if (string.IsNullOrWhiteSpace(c.Number))
                throw new InvalidOperationException("شماره چک را وارد کنید.");
            if (c.Amount <= 0)
                throw new InvalidOperationException("مبلغ چک باید بزرگ‌تر از صفر باشد.");
            if (c.DueDate == default)
                throw new InvalidOperationException("تاریخ سررسید چک را وارد کنید.");
            chequeSum += c.Amount;
            txn.Cheques.Add(new Db.Cheque
            {
                Number = c.Number.Trim(),
                TrsAccountId = c.TrsAccountId > 0 ? c.TrsAccountId : null,
                BankName = string.IsNullOrWhiteSpace(c.BankName) ? null : c.BankName.Trim(),
                AccountInfo = string.IsNullOrWhiteSpace(c.AccountInfo) ? null : c.AccountInfo.Trim(),
                OwnerName = string.IsNullOrWhiteSpace(c.OwnerName) ? null : c.OwnerName.Trim(),
                Amount = c.Amount,
                DueDate = c.DueDate.Date,
                IsCleared = c.IsCleared,
                ClearedAt = c.IsCleared ? (c.ClearedAt ?? DateTime.Now) : null,
                Note = string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim(),
                CreatedAt = DateTime.Now
            });
        }
        if (cmd.PaymentMethod == PaymentMethod.Cheque && txn.Cheques.Count == 0)
            throw new InvalidOperationException("حداقل یک چک ثبت کنید.");

        // ---------- ۳) جزء اقساط ----------
        var instSum = 0m;
        var no = 1;
        foreach (var i in cmd.Installments.OrderBy(x => x.DueDate))
        {
            if (i.Amount <= 0)
                throw new InvalidOperationException("مبلغ هر قسط باید بزرگ‌تر از صفر باشد.");
            if (i.DueDate == default)
                throw new InvalidOperationException("تاریخ سررسید هر قسط را وارد کنید.");
            instSum += i.Amount;
            txn.Installments.Add(new Db.InstallmentLine
            {
                No = no++,
                Amount = i.Amount,
                DueDate = i.DueDate.Date,
                IsPaid = i.IsPaid,
                PaidAt = i.IsPaid ? (i.PaidAt ?? DateTime.Now) : null,
                CreatedAt = DateTime.Now
            });
        }
        if (cmd.PaymentMethod == PaymentMethod.Installment && txn.Installments.Count == 0)
            throw new InvalidOperationException("حداقل یک قسط تعریف کنید.");

        // ---------- ۴) باقی‌مانده = نسیه ----------
        var paid = cash + chequeSum + instSum;
        var remainder = total - paid;
        var creditRemainder = remainder > PaymentMix.Tolerance ? remainder : 0;

        if (creditRemainder > 0)
        {
            if (!cmd.DueDate.HasValue)
                throw new InvalidOperationException(
                    $"جمع پرداخت‌ها ({paid:#,0}) کمتر از مبلغ سند ({total:#,0}) است؛ " +
                    "برای ثبت مابقی به‌صورت نسیه، تاریخ سررسید نسیه را وارد کنید.");
            txn.DueDate = cmd.DueDate.Value.Date;
        }

        // ---------- ۵) ثبت اجزای نقدی و روش مؤثر ----------
        if (cash > 0)
        {
            txn.CashAmount = cash;
            txn.CashType = cmd.CashType ?? Inventory.Shared.CashType.Cash;
            txn.SettledAmount = cash; // بخش نقدی همان ابتدا تسویه شده است
        }
        else if (cmd.PaymentMethod == PaymentMethod.Cash)
        {
            txn.CashType = cmd.CashType ?? Inventory.Shared.CashType.Cash;
        }

        txn.PaymentMethod = PaymentMix.Derive(cash, chequeSum, instSum, creditRemainder);
    }

    public async Task<AdminDashboard> GetAdminDashboardAsync()
    {
        var now = DateTime.Now;
        var today = now.Date;

        // بازه‌های شمسی: هفته از شنبه، ماه از ۱ ماه شمسی، فصل از ماه ۱/۴/۷/۱۰
        var dow = (int)today.DayOfWeek; // Saturday=6
        var weekStart = today.AddDays(-((dow + 1) % 7));
        var (jy, jm, _) = PersianDate.FromGregorian(today);
        var monthStart = PersianDate.ToGregorian(jy, jm, 1);
        var quarterFirstMonth = jm <= 3 ? 1 : jm <= 6 ? 4 : jm <= 9 ? 7 : 10;
        var quarterStart = PersianDate.ToGregorian(jy, quarterFirstMonth, 1);

        var sales = await _db.Transactions
            .Include(t => t.Cheques)
            .Include(t => t.Installments)
            .Where(t => t.Type == TransactionType.Sale && t.Date >= quarterStart)
            .ToListAsync();

        var dash = new AdminDashboard
        {
            SalesToday = sales.Where(t => t.Date.Date == today).Sum(t => t.Amount),
            SalesWeek = sales.Where(t => t.Date >= weekStart).Sum(t => t.Amount),
            SalesMonth = sales.Where(t => t.Date >= monthStart).Sum(t => t.Amount),
            SalesQuarter = sales.Sum(t => t.Amount)
        };

        // سود دوره‌ای بعد از محاسبه profitById (پایین‌تر) انجام می‌شود

        // ---------- داده نمودارها ----------
        // روند ۳۰ روز اخیر (فروش + سود) — سود از GetOrderAsync قبلاً برای فصل محاسبه شده؛
        // برای ۳۰ روز، دوباره از همان لیست فصل استفاده می‌کنیم (۳۰ روز داخل فصل است؛
        // اگر ابتدای فصل باشد روزهای قبل صفر می‌مانند که برای نمودار قابل قبول است).
        var profitById = new Dictionary<int, decimal>();
        foreach (var t in sales)
        {
            var o = await GetOrderAsync(t.Id);
            profitById[t.Id] = o?.TotalProfit ?? 0;
        }

        // سود دوره‌ای (با روش قیمت‌گذاری تنظیمات)
        foreach (var t in sales)
        {
            var profit = profitById.TryGetValue(t.Id, out var pv0) ? pv0 : 0;
            if (t.Date.Date == today) dash.ProfitToday += profit;
            if (t.Date >= weekStart) dash.ProfitWeek += profit;
            if (t.Date >= monthStart) dash.ProfitMonth += profit;
            dash.ProfitQuarter += profit;
        }

        for (var d = 29; d >= 0; d--)
        {
            var day = today.AddDays(-d);
            var dayTxns = sales.Where(t => t.Date.Date == day).ToList();
            var (djy, djm, djd) = PersianDate.FromGregorian(day);
            dash.DailyTrend.Add(new TrendPoint
            {
                Label = $"{djm}/{djd}",
                Sales = dayTxns.Sum(t => t.Amount),
                Profit = dayTxns.Sum(t => profitById.TryGetValue(t.Id, out var pv) ? pv : 0)
            });
        }

        // ۶ ماه شمسی اخیر — نیاز به همه اسناد ۶ ماه (فراتر از فصل)
        var (cy, cm, _) = PersianDate.FromGregorian(today);
        var m6y = cy; var m6m = cm;
        for (var i = 0; i < 5; i++) { m6m--; if (m6m == 0) { m6m = 12; m6y--; } }
        var sixMonthStart = PersianDate.ToGregorian(m6y, m6m, 1);
        var sales6m = await _db.Transactions
            .Where(t => t.Type == TransactionType.Sale && t.Date >= sixMonthStart)
            .ToListAsync();
        var profit6m = new Dictionary<int, decimal>(profitById);
        foreach (var t in sales6m.Where(t => !profit6m.ContainsKey(t.Id)))
        {
            var o = await GetOrderAsync(t.Id);
            profit6m[t.Id] = o?.TotalProfit ?? 0;
        }
        var my = m6y; var mm = m6m;
        for (var i = 0; i < 6; i++)
        {
            var mStart = PersianDate.ToGregorian(my, mm, 1);
            var (ny, nm) = PersianDate.AddMonths(my, mm, 1);
            var mEnd = PersianDate.ToGregorian(ny, nm, 1);
            var mTxns = sales6m.Where(t => t.Date >= mStart && t.Date < mEnd).ToList();
            dash.MonthlyTrend.Add(new TrendPoint
            {
                Label = PersianDate.MonthName(mm),
                Sales = mTxns.Sum(t => t.Amount),
                Profit = mTxns.Sum(t => profit6m.TryGetValue(t.Id, out var pv) ? pv : 0)
            });
            my = ny; mm = nm;
        }

        // تفکیک روش پرداخت (فصل جاری) — بر اساس «اجزای واقعی» پرداخت تا اسناد ترکیبی هم درست شمرده شوند
        static decimal CashOf(Db.Transaction t) => t.PaymentMethod == PaymentMethod.Cash ? t.Amount : t.CashAmount;
        static decimal ChequeOf(Db.Transaction t) => t.Cheques.Sum(c => c.Amount);
        static decimal InstOf(Db.Transaction t) => t.Installments.Sum(i => i.Amount);
        static decimal CreditOf(Db.Transaction t) => Math.Max(0, t.Amount - CashOf(t) - ChequeOf(t) - InstOf(t));

        dash.PayCash = sales.Sum(CashOf);
        dash.PayCheque = sales.Sum(ChequeOf);
        dash.PayInstallment = sales.Sum(InstOf);
        dash.PayCredit = sales.Sum(CreditOf);

        var partyNames = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p);

        // بدهکاران سررسیدشده: نسیه‌های پرداخت‌نشده + اقساط سررسیدشده
        // نسیهٔ سررسیدشده — شامل ماندهٔ نسیهٔ اسناد ترکیبی (بعد از کسر نقد/چک/اقساط)
        var creditTxns = await _db.Transactions
            .Include(t => t.Cheques)
            .Include(t => t.Installments)
            .Where(t => t.Type == TransactionType.Sale &&
                        (t.PaymentMethod == PaymentMethod.Credit || t.PaymentMethod == PaymentMethod.Mixed) &&
                        t.DueDate != null && t.DueDate <= today && t.SettledAmount < t.Amount)
            .ToListAsync();
        foreach (var t in creditTxns)
        {
            var party = t.PartyId.HasValue && partyNames.TryGetValue(t.PartyId.Value, out var pp) ? pp : null;
            dash.OverdueDebtors.Add(new DebtorItem
            {
                PartyId = t.PartyId ?? 0,
                PartyName = party?.Name ?? "—",
                Mobile = party?.Mobile,
                Number = t.Number,
                TransactionId = t.Id,
                Kind = "نسیه",
                Amount = Math.Max(0, t.Amount - CashOf(t) - ChequeOf(t) - InstOf(t) - t.SettledAmount),
                DueDate = t.DueDate!.Value,
                DaysOverdue = (int)(today - t.DueDate.Value.Date).TotalDays
            });
        }

        var dueInstallments = await _db.Installments
            .Where(i => !i.IsPaid && i.DueDate <= today)
            .ToListAsync();
        if (dueInstallments.Count > 0)
        {
            var instTxnIds = dueInstallments.Select(i => i.TransactionId).Distinct().ToList();
            var instTxns = await _db.Transactions.Where(t => instTxnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
            foreach (var i in dueInstallments)
            {
                if (!instTxns.TryGetValue(i.TransactionId, out var t)) continue;
                var party = t.PartyId.HasValue && partyNames.TryGetValue(t.PartyId.Value, out var pp) ? pp : null;
                dash.OverdueDebtors.Add(new DebtorItem
                {
                    PartyId = t.PartyId ?? 0,
                    PartyName = party?.Name ?? "—",
                    Mobile = party?.Mobile,
                    Number = t.Number,
                    TransactionId = t.Id,
                    Kind = $"قسط {i.No}",
                    Amount = i.Amount,
                    DueDate = i.DueDate,
                    DaysOverdue = (int)(today - i.DueDate.Date).TotalDays
                });
            }
        }
        dash.OverdueDebtors = dash.OverdueDebtors.OrderByDescending(d => d.DaysOverdue).ThenByDescending(d => d.Amount).ToList();

        // چک‌های دریافتی از مشتری و چک‌های پرداختی ما (شامل سررسید امروز/گذشته یا برگشتی‌ها)
        var uncleanedCheques = await _db.Cheques
            .Where(c => (!c.IsCleared && c.DueDate <= today) || c.Status == ChequeStatus.Bounced)
            .OrderBy(c => c.DueDate)
            .ToListAsync();

        if (uncleanedCheques.Count > 0)
        {
            var chTxnIds = uncleanedCheques.Select(c => c.TransactionId).Distinct().ToList();
            var chTxns = await _db.Transactions.Where(t => chTxnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
            foreach (var c in uncleanedCheques)
            {
                chTxns.TryGetValue(c.TransactionId, out var t);
                var party = t?.PartyId != null && partyNames.TryGetValue(t.PartyId.Value, out var pp) ? pp : null;
                var alertItem = new ChequeAlertItem
                {
                    ChequeId = c.Id,
                    Number = c.Number,
                    BankName = c.BankName,
                    OwnerName = c.OwnerName,
                    PartyName = party?.Name ?? "—",
                    OrderNumber = t?.Number ?? "",
                    Amount = c.Amount,
                    DueDate = c.DueDate,
                    Status = c.Status,
                    BounceReason = c.BounceReason
                };

                if (t != null && t.Type == TransactionType.Purchase)
                {
                    // چک پرداختی ما به فروشنده/تأمین‌کننده
                    dash.IssuedCheques.Add(alertItem);
                }
                else
                {
                    // چک دریافتی از مشتری (فروش)
                    dash.TodayCheques.Add(alertItem);
                }
            }
        }

        return dash;
    }

    public async Task<List<ReferrerProductItem>> GetReferrerProductsAsync(int referrerId, string? search, bool bypassFlag = false, Paging.Request? pagination = null)
    {
        var referrer = await _db.Referrers.FindAsync(referrerId)
            ?? throw new InvalidOperationException("معرف یافت نشد.");
        // bypassFlag: کاربر از طریق نقش‌های RBAC مجوز «محصولات من» را دارد
        if (!bypassFlag && !referrer.CanViewProducts)
            throw new InvalidOperationException("دسترسی مشاهده کالاها برای شما فعال نشده است.");

        var q = _db.Products.AsNoTracking().Where(p => p.IsActive && !p.IsService);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s) || (p.Category != null && p.Category.Contains(s)));
        }
        if (_db.Database.IsSqlite())
            q = q.Where(p => Math.Round(_db.Stocks.Where(s => s.ProductId == p.Id)
                .Sum(s => (double?)s.Quantity) ?? 0, 3) > 0);
        else
            q = q.Where(p => (_db.Stocks.Where(s => s.ProductId == p.Id)
                .Sum(s => (decimal?)s.Quantity) ?? 0) > 0);
        var products = await q.OrderBy(p => p.Name).ThenBy(p => p.Id).ToPageListAsync(pagination);
        return products
            .Select(p => new ReferrerProductItem
            {
                Name = p.Name,
                Code = p.Code,
                Unit = p.Unit,
                Category = p.Category,
                // قیمت فروش عمداً ارسال نمی‌شود — معرف فقط موجود بودن کالا را می‌بیند
                InStock = true
            }).ToList();
    }

    /// <summary>پاس کردن چک.</summary>
    public async Task ClearChequeAsync(int chequeId)
    {
        var c = await _db.Cheques.FindAsync(chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        c.IsCleared = true;
        c.ClearedAt = DateTime.Now;
        c.Status = ChequeStatus.Cleared;
        c.BouncedAt = null;
        c.BounceReason = null;
        await _db.SaveChangesAsync();
    }

    /// <summary>برگشت زدن چک.</summary>
    public async Task BounceChequeAsync(int chequeId, string? reason)
    {
        var c = await _db.Cheques.FindAsync(chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        c.IsCleared = false;
        c.ClearedAt = null;
        c.Status = ChequeStatus.Bounced;
        c.BouncedAt = DateTime.Now;
        c.BounceReason = string.IsNullOrWhiteSpace(reason) ? "عدم موجودی کافی" : reason.Trim();
        await _db.SaveChangesAsync();
    }

    /// <summary>بازگردانی وضعیت چک به در جریان (InHand).</summary>
    public async Task ResetChequeAsync(int chequeId)
    {
        var c = await _db.Cheques.FindAsync(chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        c.IsCleared = false;
        c.ClearedAt = null;
        c.Status = ChequeStatus.InHand;
        c.BouncedAt = null;
        c.BounceReason = null;
        await _db.SaveChangesAsync();
    }

    /// <summary>ثبت پرداخت قسط.</summary>
    public async Task PayInstallmentAsync(int installmentId)
    {
        var i = await _db.Installments.FindAsync(installmentId)
            ?? throw new InvalidOperationException("قسط یافت نشد.");
        if (!i.IsPaid)
        {
            i.IsPaid = true;
            i.PaidAt = DateTime.Now;
            // مبلغ تسویه‌شده سند
            var t = await _db.Transactions.FindAsync(i.TransactionId);
            if (t is not null) t.SettledAmount += i.Amount;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>لغو پرداخت قسط (برگشت به حالت پرداخت‌نشده / بدهکار).</summary>
    public async Task UnpayInstallmentAsync(int installmentId)
    {
        var i = await _db.Installments.FindAsync(installmentId)
            ?? throw new InvalidOperationException("قسط یافت نشد.");
        if (i.IsPaid)
        {
            i.IsPaid = false;
            i.PaidAt = null;
            var t = await _db.Transactions.FindAsync(i.TransactionId);
            if (t is not null) t.SettledAmount = Math.Max(0, t.SettledAmount - i.Amount);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>تسویه سند نسیه.</summary>
    public async Task SettleCreditAsync(int transactionId, decimal amount)
    {
        var t = await _db.Transactions.FindAsync(transactionId)
            ?? throw new InvalidOperationException("سند یافت نشد.");
        if (amount <= 0) throw new InvalidOperationException("مبلغ تسویه باید بزرگ‌تر از صفر باشد.");
        t.SettledAmount = Math.Min(t.Amount, t.SettledAmount + amount);
        await _db.SaveChangesAsync();
    }

    private async Task<string> GenerateNumberAsync(TransactionType type)
    {
        var prefix = type switch
        {
            TransactionType.Purchase => "PR",
            TransactionType.Sale => "SL",
            _ => "ADJ"
        };
        var count = await _db.Transactions.CountAsync(t => t.Type == type);
        return $"{prefix}-{count + 1:0000}";
    }

    /// <summary>بازسازی موجودی و قیمت میانگین یک کالا در یک انبار بر اساس کل اسناد (فقط در حافظه؛ ذخیره توسط فراخواننده).</summary>
    private async Task RecalcStockAsync(int warehouseId, int productId)
    {
        var txns = await _db.Transactions.Include(t => t.Lines)
            .Where(t => t.WarehouseId == warehouseId && t.Lines.Any(l => l.ProductId == productId))
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        decimal qty = 0, cost = 0;
        var product = await _db.Products.FindAsync(productId);
        var defaultCost = product?.PurchasePrice ?? 0;

        foreach (var t in txns)
        {
            foreach (var l in t.Lines.Where(l => l.ProductId == productId))
            {
                switch (t.Type)
                {
                    case TransactionType.Purchase:
                        var newQty = qty + l.Quantity;
                        if (newQty != 0)
                            cost = (qty * cost + l.Quantity * l.Price) / newQty;
                        qty = newQty;
                        break;
                    case TransactionType.Sale:
                        qty -= l.Quantity;
                        break;
                    default: // Adjustment / Initial
                        qty = l.Quantity;
                        if (cost == 0 && qty > 0) cost = l.Price > 0 ? l.Price : defaultCost;
                        break;
                }
            }
        }

        // ---------- ماژول انبارداری ----------
        // گردش اسناد رسید/حواله انبار هم به مانده اضافه می‌شود تا مانده «یکپارچه» بماند.
        var invStock = await _db.InvStocks.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);
        if (invStock is not null)
        {
            qty += invStock.Quantity;
            if (invStock.AvgCost > 0) cost = invStock.AvgCost;
        }

        var stock = await _db.Stocks.FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);
        if (qty == 0 && stock is null) return;

        if (stock is null)
        {
            stock = new Db.Stock { WarehouseId = warehouseId, ProductId = productId };
            _db.Stocks.Add(stock);
        }
        stock.Quantity = qty;
        stock.AvgCost = qty > 0 ? cost : defaultCost;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ================== مدیریت اقساط و چک‌های سامانه فروشگاهی ==================

    public async Task<ShopCreditSummary> GetShopCreditSummaryAsync()
    {
        var today = DateTime.Today;
        var cheques = await _db.Cheques.AsNoTracking().ToListAsync();
        var installments = await _db.Installments.AsNoTracking().ToListAsync();

        var inHand = cheques.Where(c => !c.IsCleared && c.Status != ChequeStatus.Bounced).ToList();
        var cleared = cheques.Where(c => c.IsCleared || c.Status == ChequeStatus.Cleared).ToList();
        var bounced = cheques.Where(c => c.Status == ChequeStatus.Bounced).ToList();

        var pendingInst = installments.Where(i => !i.IsPaid).ToList();
        var paidInst = installments.Where(i => i.IsPaid).ToList();
        var overdueInst = installments.Where(i => !i.IsPaid && i.DueDate.Date < today).ToList();

        return new ShopCreditSummary
        {
            InHandChequesCount = inHand.Count,
            InHandChequesTotal = inHand.Sum(x => x.Amount),
            ClearedChequesCount = cleared.Count,
            ClearedChequesTotal = cleared.Sum(x => x.Amount),
            BouncedChequesCount = bounced.Count,
            BouncedChequesTotal = bounced.Sum(x => x.Amount),

            PendingInstallmentsCount = pendingInst.Count,
            PendingInstallmentsTotal = pendingInst.Sum(x => x.Amount),
            PaidInstallmentsCount = paidInst.Count,
            PaidInstallmentsTotal = paidInst.Sum(x => x.Amount),
            OverdueInstallmentsCount = overdueInst.Count,
            OverdueInstallmentsTotal = overdueInst.Sum(x => x.Amount)
        };
    }

    public async Task<List<ShopChequeItem>> GetShopChequesAsync(ChequeStatus? status, TransactionType? txnType, string? search)
    {
        var today = DateTime.Today;
        var q = _db.Cheques.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            if (status.Value == ChequeStatus.Cleared)
                q = q.Where(c => c.IsCleared || c.Status == ChequeStatus.Cleared);
            else if (status.Value == ChequeStatus.Bounced)
                q = q.Where(c => c.Status == ChequeStatus.Bounced);
            else
                q = q.Where(c => !c.IsCleared && c.Status != ChequeStatus.Bounced);
        }

        var list = await q.OrderBy(c => c.DueDate).ToListAsync();
        if (list.Count == 0) return new List<ShopChequeItem>();

        var txnIds = list.Select(c => c.TransactionId).Distinct().ToList();
        var txns = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id);

        var partyIds = txns.Values.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var result = new List<ShopChequeItem>();
        foreach (var c in list)
        {
            txns.TryGetValue(c.TransactionId, out var t);
            if (txnType.HasValue && t != null && t.Type != txnType.Value) continue;

            Db.Party? party = null;
            if (t?.PartyId.HasValue == true) parties.TryGetValue(t.PartyId.Value, out party);

            var item = new ShopChequeItem
            {
                Id = c.Id,
                TransactionId = c.TransactionId,
                OrderNumber = t?.Number ?? "—",
                TransactionType = t?.Type ?? TransactionType.Sale,
                PartyId = t?.PartyId ?? 0,
                PartyName = party?.Name ?? (c.OwnerName ?? "—"),
                PartyMobile = party?.Mobile,
                Number = c.Number,
                BankName = c.BankName,
                AccountInfo = c.AccountInfo,
                OwnerName = c.OwnerName,
                Amount = c.Amount,
                DueDate = c.DueDate,
                IsCleared = c.IsCleared || c.Status == ChequeStatus.Cleared,
                ClearedAt = c.ClearedAt,
                Status = c.Status,
                BouncedAt = c.BouncedAt,
                BounceReason = c.BounceReason,
                Note = c.Note,
                DaysOverdue = c.DueDate.Date < today ? (int)(today - c.DueDate.Date).TotalDays : 0
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                bool match = item.Number.ToLower().Contains(s) ||
                             item.PartyName.ToLower().Contains(s) ||
                             (item.BankName != null && item.BankName.ToLower().Contains(s)) ||
                             item.OrderNumber.ToLower().Contains(s);
                if (!match) continue;
            }

            result.Add(item);
        }

        return result;
    }

    public async Task<List<ShopInstallmentItem>> GetShopInstallmentsAsync(bool? isPaid, string? search)
    {
        var today = DateTime.Today;
        var q = _db.Installments.AsNoTracking().AsQueryable();

        if (isPaid.HasValue)
            q = q.Where(i => i.IsPaid == isPaid.Value);

        var list = await q.OrderBy(i => i.DueDate).ToListAsync();
        if (list.Count == 0) return new List<ShopInstallmentItem>();

        var txnIds = list.Select(i => i.TransactionId).Distinct().ToList();
        var txns = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id);

        var partyIds = txns.Values.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var result = new List<ShopInstallmentItem>();
        foreach (var i in list)
        {
            txns.TryGetValue(i.TransactionId, out var t);
            Db.Party? party = null;
            if (t?.PartyId.HasValue == true) parties.TryGetValue(t.PartyId.Value, out party);

            var item = new ShopInstallmentItem
            {
                Id = i.Id,
                TransactionId = i.TransactionId,
                OrderNumber = t?.Number ?? "—",
                PartyId = t?.PartyId ?? 0,
                PartyName = party?.Name ?? "—",
                PartyMobile = party?.Mobile,
                No = i.No,
                Amount = i.Amount,
                DueDate = i.DueDate,
                IsPaid = i.IsPaid,
                PaidAt = i.PaidAt,
                DaysOverdue = !i.IsPaid && i.DueDate.Date < today ? (int)(today - i.DueDate.Date).TotalDays : 0
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                bool match = item.PartyName.ToLower().Contains(s) ||
                             item.OrderNumber.ToLower().Contains(s) ||
                             (item.PartyMobile != null && item.PartyMobile.Contains(s));
                if (!match) continue;
            }

            result.Add(item);
        }

        return result;
    }

}
