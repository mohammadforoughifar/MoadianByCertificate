using Inventory.Api.Services;
using Inventory.Api.Services.Accounting;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Stocktaking;

/// <summary>
/// پیاده‌سازی ماژول انبارگردانی و بارکد.
///
/// چرخه‌ی یک دوره انبارگردانی:
///   ۱) پیش‌نویس   — انبار، دامنه و عنوان مشخص می‌شود
///   ۲) شمارش      — موجودی سیستم «قفل» (اسنپ‌شات) و لیست شمارش ساخته می‌شود
///   ۳) بررسی      — مغایرت‌ها مرور می‌شوند
///   ۴) اعمال      — رسید اضافی و حواله کسری صادر و قطعی می‌شوند
///                    (سند حسابداری آن‌ها از طریق AccInvRule خودکار صادر می‌شود)
/// </summary>
public class StocktakingService : IStocktakingService
{
    private readonly Db.AppDbContext _db;
    private readonly IWarehousingService _wh;
    private readonly IAccountingService _acc;

    /// <summary>کد نوع سند رسید اصلاح موجودی (اضافی انبار)</summary>
    private const string SurplusDocCode = "RC-ADJ";

    /// <summary>کد نوع سند حواله اصلاح موجودی (کسری انبار)</summary>
    private const string ShortageDocCode = "IS-ADJ";

    public StocktakingService(Db.AppDbContext db, IWarehousingService wh, IAccountingService acc)
    {
        _db = db;
        _wh = wh;
        _acc = acc;
    }

    // =====================================================================
    // ۱) بارکد
    // =====================================================================

    public async Task<List<BcdBarcode>> GetBarcodesAsync(int? productId = null, string? search = null, Paging.Request? pagination = null)
    {
        var q = _db.BcdBarcodes.AsNoTracking().Include(b => b.Product).AsQueryable();

        if (productId is > 0) q = q.Where(b => b.ProductId == productId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(b => b.Code.Contains(s)
                             || (b.Product != null && (b.Product.Name.Contains(s) || b.Product.Code.Contains(s))));
        }

        var rows = await q
            .OrderBy(b => b.ProductId).ThenByDescending(b => b.IsPrimary).ThenBy(b => b.Code).ThenBy(x => x.Id)
            .ToPageListAsync(pagination, defaultCap: 500);

        return rows.Select(MapBarcode).ToList();
    }

    public async Task<BcdBarcode> SaveBarcodeAsync(BcdBarcode dto)
    {
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException("محتوای بارکد الزامی است.");
        if (dto.ProductId <= 0)
            throw new InvalidOperationException("کالا را انتخاب کنید.");

        if (!Barcode.IsValid(code, dto.Type))
            throw new InvalidOperationException($"این مقدار برای استاندارد {TypeTitle(dto.Type)} معتبر نیست.");

        // EAN بدون رقم کنترلی را کامل می‌کنیم
        if (dto.Type == BarcodeType.Ean13 && code.Length == 12) code += Barcode.CheckDigit(code);
        if (dto.Type == BarcodeType.Ean8 && code.Length == 7) code += Barcode.CheckDigit(code);

        if (await _db.BcdBarcodes.AnyAsync(b => b.Code == code && b.Id != dto.Id))
            throw new InvalidOperationException($"بارکد «{code}» قبلاً برای کالای دیگری ثبت شده است.");

        if (dto.PackQty <= 0)
            throw new InvalidOperationException("ضریب بسته باید بزرگ‌تر از صفر باشد.");

        var e = dto.Id > 0
            ? await _db.BcdBarcodes.FirstOrDefaultAsync(b => b.Id == dto.Id)
              ?? throw new InvalidOperationException("بارکد یافت نشد.")
            : new Db.BcdBarcode { CreatedAt = DateTime.Now };

        e.ProductId = dto.ProductId;
        e.Code = code;
        e.Type = dto.Type;
        e.Unit = Trim(dto.Unit);
        e.PackQty = dto.PackQty;
        e.IsActive = dto.IsActive;
        e.Description = Trim(dto.Description);

        if (dto.Id == 0) _db.BcdBarcodes.Add(e);
        await _db.SaveChangesAsync();

        // فقط یک بارکد اصلی برای هر کالا
        if (dto.IsPrimary)
        {
            var others = await _db.BcdBarcodes
                .Where(b => b.ProductId == e.ProductId && b.Id != e.Id && b.IsPrimary).ToListAsync();
            foreach (var o in others) o.IsPrimary = false;
            e.IsPrimary = true;

            // بارکد اصلی روی خود کالا هم نوشته می‌شود (سازگاری با فرم کالا)
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == e.ProductId);
            if (product is not null) product.Barcode = e.Code;

            await _db.SaveChangesAsync();
        }
        else if (e.IsPrimary)
        {
            e.IsPrimary = false;
            await _db.SaveChangesAsync();
        }

        var saved = await _db.BcdBarcodes.AsNoTracking().Include(b => b.Product)
            .FirstAsync(b => b.Id == e.Id);
        return MapBarcode(saved);
    }

    public async Task DeleteBarcodeAsync(int id)
    {
        var e = await _db.BcdBarcodes.FirstOrDefaultAsync(b => b.Id == id)
                ?? throw new InvalidOperationException("بارکد یافت نشد.");

        _db.BcdBarcodes.Remove(e);
        await _db.SaveChangesAsync();
    }

    public async Task<int> GenerateBarcodesAsync(List<int> productIds, string prefix)
    {
        if (productIds is null || productIds.Count == 0)
            throw new InvalidOperationException("هیچ کالایی انتخاب نشده است.");

        prefix = string.IsNullOrWhiteSpace(prefix) ? "200" : prefix.Trim();
        if (!prefix.All(char.IsDigit) || prefix.Length is < 2 or > 4)
            throw new InvalidOperationException("پیشوند باید ۲ تا ۴ رقم باشد.");

        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

        // کالاهایی که همین حالا بارکد اصلی دارند رد می‌شوند
        var already = await _db.BcdBarcodes
            .Where(b => productIds.Contains(b.ProductId) && b.IsPrimary)
            .Select(b => b.ProductId).ToListAsync();

        var created = 0;
        foreach (var p in products)
        {
            if (already.Contains(p.Id)) continue;

            var code = Barcode.BuildInternalEan13(p.Id, prefix);

            // در حالت بسیار نادرِ تصادم، تا ۱۰ بار با افزودن آفست تلاش می‌کنیم
            var attempt = 0;
            while (await _db.BcdBarcodes.AnyAsync(b => b.Code == code) && attempt < 10)
            {
                attempt++;
                code = Barcode.BuildInternalEan13(p.Id + attempt * 1_000_000, prefix);
            }
            if (await _db.BcdBarcodes.AnyAsync(b => b.Code == code)) continue;

            _db.BcdBarcodes.Add(new Db.BcdBarcode
            {
                ProductId = p.Id,
                Code = code,
                Type = BarcodeType.Ean13,
                Unit = p.Unit,
                PackQty = 1,
                IsPrimary = true,
                IsActive = true,
                Description = "تولید خودکار",
                CreatedAt = DateTime.Now
            });

            p.Barcode = code;
            created++;
        }

        await _db.SaveChangesAsync();
        return created;
    }

    public async Task<BcdScanResult> ScanAsync(string code, int warehouseId)
    {
        var value = (code ?? "").Trim();
        if (string.IsNullOrEmpty(value))
            return new BcdScanResult { Found = false, Code = value, Message = "بارکد خالی است." };

        // ۱) جدول بارکدها
        var bc = await _db.BcdBarcodes.AsNoTracking().Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Code == value && b.IsActive);

        Db.Product? product = bc?.Product;
        decimal pack = bc?.PackQty ?? 1;
        var unit = bc?.Unit;

        // ۲) فیلد بارکد قدیمی روی کالا
        product ??= await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Barcode == value);

        // ۳) کد کالا — تا اپراتور بتواند کد را هم تایپ کند
        product ??= await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Code == value);

        if (product is null)
            return new BcdScanResult
            {
                Found = false,
                Code = value,
                Message = $"کالایی با بارکد «{value}» یافت نشد."
            };

        var stock = warehouseId > 0
            ? await _db.InvStocks.AsNoTracking()
                .Where(s => s.ProductId == product.Id && s.WarehouseId == warehouseId)
                .Select(s => (decimal?)s.Quantity).FirstOrDefaultAsync() ?? 0
            : 0;

        return new BcdScanResult
        {
            Found = true,
            Code = value,
            ProductId = product.Id,
            ProductCode = product.Code,
            ProductName = product.Name,
            Unit = unit ?? product.Unit,
            PackQty = pack <= 0 ? 1 : pack,
            CurrentStock = stock,
            SalePrice = product.SalePrice,
            PurchasePrice = product.PurchasePrice
        };
    }

    public async Task<List<BcdLabel>> GetLabelsAsync(List<int> productIds)
    {
        if (productIds is null || productIds.Count == 0) return new List<BcdLabel>();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync();

        var barcodes = await _db.BcdBarcodes.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId) && b.IsActive)
            .ToListAsync();

        var list = new List<BcdLabel>();
        foreach (var id in productIds)
        {
            var p = products.FirstOrDefault(x => x.Id == id);
            if (p is null) continue;

            var bc = barcodes.Where(b => b.ProductId == id)
                .OrderByDescending(b => b.IsPrimary).FirstOrDefault();

            list.Add(new BcdLabel
            {
                ProductId = p.Id,
                ProductCode = p.Code,
                ProductName = p.Name,
                Barcode = bc?.Code ?? p.Barcode,
                Type = bc?.Type ?? (IsEan13(p.Barcode) ? BarcodeType.Ean13 : BarcodeType.Code128),
                Unit = bc?.Unit ?? p.Unit,
                SalePrice = p.SalePrice,
                Copies = 1
            });
        }
        return list;
    }

    private static bool IsEan13(string? code)
        => !string.IsNullOrWhiteSpace(code) && code.Length == 13 && code.All(char.IsDigit);

    private static BcdBarcode MapBarcode(Db.BcdBarcode e) => new()
    {
        Id = e.Id,
        ProductId = e.ProductId,
        ProductCode = e.Product?.Code,
        ProductName = e.Product?.Name,
        Code = e.Code,
        Type = e.Type,
        Unit = e.Unit,
        PackQty = e.PackQty,
        IsPrimary = e.IsPrimary,
        IsActive = e.IsActive,
        Description = e.Description,
        CreatedAt = e.CreatedAt
    };

    private static string TypeTitle(BarcodeType t) => t switch
    {
        BarcodeType.Code128 => "Code 128",
        BarcodeType.Ean13 => "EAN-13",
        BarcodeType.Ean8 => "EAN-8",
        _ => "کد داخلی"
    };

    // =====================================================================
    // ۲) دوره انبارگردانی — خواندن
    // =====================================================================

    public async Task<PagedResult<StkSession>> GetSessionsAsync(int? warehouseId, StocktakeStatus? status,
        string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.StkSessions.AsNoTracking().AsQueryable();

        if (warehouseId is > 0) q = q.Where(s => s.WarehouseId == warehouseId);
        if (status is not null) q = q.Where(s => s.Status == status);
        if (from is not null) q = q.Where(s => s.Date >= from.Value.Date);
        if (to is not null) q = q.Where(s => s.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = q.Where(s => s.Title.Contains(t) || (s.Description ?? "").Contains(t)
                             || s.Number.ToString().Contains(t));
        }

        var total = await q.CountAsync();

        var rows = await q
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new StkSession
            {
                Id = s.Id,
                Number = s.Number,
                Title = s.Title,
                WarehouseId = s.WarehouseId,
                WarehouseName = s.Warehouse != null ? s.Warehouse.Name : null,
                Date = s.Date,
                Scope = s.Scope,
                CategoryId = s.CategoryId,
                Status = s.Status,
                TreatUncountedAsZero = s.TreatUncountedAsZero,
                Description = s.Description,
                SurplusDocId = s.SurplusDocId,
                ShortageDocId = s.ShortageDocId,
                CreatedBy = s.CreatedBy,
                CreatedAt = s.CreatedAt,
                AppliedBy = s.AppliedBy,
                AppliedAt = s.AppliedAt,

                TotalLines = s.Lines.Count,
                CountedLines = s.Lines.Count(l => l.IsCounted),
                DiffLines = s.Lines.Count(l => l.IsCounted && l.CountedQty != l.SystemQty),
                SurplusQty = s.Lines.Where(l => l.IsCounted && l.CountedQty > l.SystemQty)
                    .Sum(l => (decimal?)(l.CountedQty - l.SystemQty)) ?? 0,
                ShortageQty = s.Lines.Where(l => l.IsCounted && l.CountedQty < l.SystemQty)
                    .Sum(l => (decimal?)(l.SystemQty - l.CountedQty)) ?? 0,
                SurplusValue = s.Lines.Where(l => l.IsCounted && l.CountedQty > l.SystemQty)
                    .Sum(l => (decimal?)((l.CountedQty - l.SystemQty) * l.UnitCost)) ?? 0,
                ShortageValue = s.Lines.Where(l => l.IsCounted && l.CountedQty < l.SystemQty)
                    .Sum(l => (decimal?)((l.SystemQty - l.CountedQty) * l.UnitCost)) ?? 0
            })
            .ToListAsync();

        foreach (var r in rows) r.NetValue = r.SurplusValue - r.ShortageValue;
        await FillDocNumbersAsync(rows);

        return new PagedResult<StkSession> { Items = rows, TotalCount = total };
    }

    public async Task<StkSession?> GetSessionAsync(int id, bool withLines = true)
    {
        var query = _db.StkSessions.AsNoTracking().Include(s => s.Warehouse).AsQueryable();
        if (withLines) query = query.Include(s => s.Lines).ThenInclude(l => l.Product);

        var e = await query.FirstOrDefaultAsync(s => s.Id == id);
        if (e is null) return null;

        var dto = new StkSession
        {
            Id = e.Id,
            Number = e.Number,
            Title = e.Title,
            WarehouseId = e.WarehouseId,
            WarehouseName = e.Warehouse?.Name,
            Date = e.Date,
            Scope = e.Scope,
            CategoryId = e.CategoryId,
            Status = e.Status,
            TreatUncountedAsZero = e.TreatUncountedAsZero,
            Description = e.Description,
            SurplusDocId = e.SurplusDocId,
            ShortageDocId = e.ShortageDocId,
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt,
            AppliedBy = e.AppliedBy,
            AppliedAt = e.AppliedAt
        };

        if (e.CategoryId is > 0)
            dto.CategoryName = await _db.ProductCategories.Where(c => c.Id == e.CategoryId)
                .Select(c => c.Name).FirstOrDefaultAsync();

        if (withLines)
        {
            var barcodes = await _db.BcdBarcodes.AsNoTracking()
                .Where(b => b.IsPrimary && b.IsActive)
                .ToDictionaryAsync(b => b.ProductId, b => b.Code);

            dto.Lines = e.Lines.OrderBy(l => l.RowNo).Select(l => new StkLine
            {
                Id = l.Id,
                RowNo = l.RowNo,
                ProductId = l.ProductId,
                ProductCode = l.Product?.Code ?? "",
                ProductName = l.Product?.Name ?? "",
                Unit = l.Product?.Unit ?? "",
                Barcode = barcodes.TryGetValue(l.ProductId, out var bc) ? bc : l.Product?.Barcode,
                ShelfCode = l.Product?.ShelfCode,
                SystemQty = l.SystemQty,
                CountedQty = l.CountedQty,
                IsCounted = l.IsCounted,
                UnitCost = l.UnitCost,
                Note = l.Note,
                CountedBy = l.CountedBy,
                CountedAt = l.CountedAt,
                ScanCount = l.ScanCount
            }).ToList();
        }

        FillStats(dto, e.Lines);
        await FillDocNumbersAsync(new List<StkSession> { dto });
        return dto;
    }

    /// <summary>آمار دوره را از روی اقلام محاسبه می‌کند.</summary>
    private static void FillStats(StkSession dto, ICollection<Db.StkLine> lines)
    {
        dto.TotalLines = lines.Count;
        dto.CountedLines = lines.Count(l => l.IsCounted);
        dto.DiffLines = lines.Count(l => l.IsCounted && l.CountedQty != l.SystemQty);

        var surplus = lines.Where(l => l.IsCounted && l.CountedQty > l.SystemQty).ToList();
        var shortage = lines.Where(l => l.IsCounted && l.CountedQty < l.SystemQty).ToList();

        dto.SurplusQty = surplus.Sum(l => l.CountedQty - l.SystemQty);
        dto.ShortageQty = shortage.Sum(l => l.SystemQty - l.CountedQty);
        dto.SurplusValue = surplus.Sum(l => (l.CountedQty - l.SystemQty) * l.UnitCost);
        dto.ShortageValue = shortage.Sum(l => (l.SystemQty - l.CountedQty) * l.UnitCost);
        dto.NetValue = dto.SurplusValue - dto.ShortageValue;
    }

    private async Task FillDocNumbersAsync(List<StkSession> rows)
    {
        var ids = rows.SelectMany(r => new[] { r.SurplusDocId, r.ShortageDocId })
            .Where(i => i is > 0).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var map = await _db.InvDocs.AsNoTracking().Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Number);

        foreach (var r in rows)
        {
            if (r.SurplusDocId is > 0 && map.TryGetValue(r.SurplusDocId.Value, out var s))
                r.SurplusDocNumber = s;
            if (r.ShortageDocId is > 0 && map.TryGetValue(r.ShortageDocId.Value, out var h))
                r.ShortageDocNumber = h;
        }
    }

    // =====================================================================
    // ۳) دوره — ثبت و تغییر وضعیت
    // =====================================================================

    public async Task<StkSession> NewSessionAsync(int warehouseId)
    {
        var max = await _db.StkSessions.Select(s => (int?)s.Number).MaxAsync() ?? 0;

        var wh = warehouseId > 0
            ? await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId)
            : await _db.Warehouses.AsNoTracking()
                .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Id).FirstOrDefaultAsync();

        var today = PersianDate.FromGregorian(DateTime.Now);

        return new StkSession
        {
            Number = max + 1,
            Title = $"انبارگردانی {wh?.Name ?? ""} — {today.Year}/{today.Month:00}".Trim(),
            WarehouseId = wh?.Id ?? 0,
            WarehouseName = wh?.Name,
            Date = DateTime.Now.Date,
            Scope = StocktakeScope.InStock,
            Status = StocktakeStatus.Draft
        };
    }

    public async Task<StkSession> SaveSessionAsync(StkSession dto, string? user)
    {
        if (dto.WarehouseId <= 0)
            throw new InvalidOperationException("انبار را انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان دوره الزامی است.");
        if (dto.Scope == StocktakeScope.ByCategory && dto.CategoryId is not > 0)
            throw new InvalidOperationException("برای دامنه «یک گروه کالا»، گروه را انتخاب کنید.");

        var e = dto.Id > 0
            ? await _db.StkSessions.FirstOrDefaultAsync(s => s.Id == dto.Id)
              ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.")
            : new Db.StkSession { CreatedBy = user, CreatedAt = DateTime.Now };

        if (dto.Id > 0 && e.Status is StocktakeStatus.Applied or StocktakeStatus.Cancelled)
            throw new InvalidOperationException("این دوره قابل ویرایش نیست.");

        // انبار و دامنه بعد از شروع شمارش قفل می‌شوند
        if (e.Status == StocktakeStatus.Draft)
        {
            e.WarehouseId = dto.WarehouseId;
            e.Scope = dto.Scope;
            e.CategoryId = dto.Scope == StocktakeScope.ByCategory ? dto.CategoryId : null;
            e.Date = dto.Date.Date;
        }

        e.Title = dto.Title.Trim();
        e.TreatUncountedAsZero = dto.TreatUncountedAsZero;
        e.Description = Trim(dto.Description);

        if (dto.Id == 0)
        {
            var max = await _db.StkSessions.Select(s => (int?)s.Number).MaxAsync() ?? 0;
            e.Number = max + 1;
            e.Status = StocktakeStatus.Draft;
            _db.StkSessions.Add(e);
        }

        await _db.SaveChangesAsync();
        return (await GetSessionAsync(e.Id))!;
    }

    /// <summary>
    /// قفل کردن موجودی: لیست شمارش از روی دامنه ساخته می‌شود و
    /// مقدار و بهای واحد هر کالا در همان لحظه اسنپ‌شات می‌گیرد.
    /// </summary>
    public async Task<StkSession> StartCountingAsync(int id, string? user)
    {
        var e = await _db.StkSessions.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Draft)
            throw new InvalidOperationException("فقط دوره‌ی پیش‌نویس را می‌توان شروع کرد.");

        // دوره‌ی باز دیگری روی همین انبار نباشد
        var open = await _db.StkSessions.AnyAsync(s => s.Id != e.Id
            && s.WarehouseId == e.WarehouseId
            && (s.Status == StocktakeStatus.Counting || s.Status == StocktakeStatus.Review));
        if (open)
            throw new InvalidOperationException("برای این انبار یک دوره انبارگردانی باز وجود دارد؛ ابتدا آن را ببندید.");

        // موجودی فعلی انبار
        var stocks = await _db.InvStocks.AsNoTracking()
            .Where(s => s.WarehouseId == e.WarehouseId)
            .ToDictionaryAsync(s => s.ProductId, s => new { s.Quantity, s.AvgCost });

        // انتخاب کالاها بر اساس دامنه
        List<Db.Product> products;
        switch (e.Scope)
        {
            case StocktakeScope.AllProducts:
                products = await _db.Products.AsNoTracking()
                    .Where(p => p.IsActive && !p.IsService).ToListAsync();
                break;

            case StocktakeScope.ByCategory:
                products = await _db.Products.AsNoTracking()
                    .Where(p => p.IsActive && !p.IsService && p.CategoryId == e.CategoryId).ToListAsync();
                break;

            case StocktakeScope.ScanOnly:
                products = new List<Db.Product>();
                break;

            default: // InStock
                var ids = stocks.Where(s => s.Value.Quantity != 0).Select(s => s.Key).ToList();
                products = await _db.Products.AsNoTracking()
                    .Where(p => ids.Contains(p.Id)).ToListAsync();
                break;
        }

        _db.StkLines.RemoveRange(e.Lines);
        e.Lines.Clear();

        var row = 1;
        foreach (var p in products.OrderBy(p => p.Code))
        {
            stocks.TryGetValue(p.Id, out var st);
            e.Lines.Add(new Db.StkLine
            {
                RowNo = row++,
                ProductId = p.Id,
                SystemQty = st?.Quantity ?? 0,
                UnitCost = st?.AvgCost ?? p.PurchasePrice,
                CountedQty = 0,
                IsCounted = false
            });
        }

        e.Status = StocktakeStatus.Counting;
        await _db.SaveChangesAsync();

        return (await GetSessionAsync(e.Id))!;
    }

    public async Task<StkSession> FinishCountingAsync(int id, string? user)
    {
        var e = await _db.StkSessions.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Counting)
            throw new InvalidOperationException("این دوره در مرحله شمارش نیست.");
        if (e.Lines.Count == 0)
            throw new InvalidOperationException("لیست شمارش خالی است.");
        if (!e.Lines.Any(l => l.IsCounted))
            throw new InvalidOperationException("هیچ قلمی شمارش نشده است.");

        e.Status = StocktakeStatus.Review;
        await _db.SaveChangesAsync();
        return (await GetSessionAsync(e.Id))!;
    }

    public async Task<StkSession> ReopenCountingAsync(int id, string? user)
    {
        var e = await _db.StkSessions.FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Review)
            throw new InvalidOperationException("فقط از مرحله بررسی می‌توان به شمارش برگشت.");

        e.Status = StocktakeStatus.Counting;
        await _db.SaveChangesAsync();
        return (await GetSessionAsync(e.Id))!;
    }

    /// <summary>
    /// صدور اسناد اصلاح: یک رسید برای اضافی‌ها و یک حواله برای کسری‌ها.
    /// هر دو قطعی می‌شوند و سند حسابداری‌شان از طریق قواعد AccInvRule صادر می‌شود.
    /// </summary>
    public async Task<StkSession> ApplyAsync(int id, string? user)
    {
        var e = await _db.StkSessions.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Review)
            throw new InvalidOperationException("فقط دوره‌ای که در مرحله «بررسی مغایرت» است اعمال می‌شود.");
        if (e.SurplusDocId is > 0 || e.ShortageDocId is > 0)
            throw new InvalidOperationException("اسناد اصلاح این دوره قبلاً صادر شده‌اند.");

        // اقلام مؤثر: شمارش‌شده‌ها + (در صورت انتخاب) شمارش‌نشده‌ها با مقدار صفر
        var effective = e.Lines
            .Where(l => l.IsCounted || (e.TreatUncountedAsZero && l.SystemQty != 0))
            .Select(l => new
            {
                l.ProductId,
                l.UnitCost,
                Diff = (l.IsCounted ? l.CountedQty : 0) - l.SystemQty
            })
            .Where(x => x.Diff != 0)
            .ToList();

        if (effective.Count == 0)
            throw new InvalidOperationException("مغایرتی برای اعمال وجود ندارد.");

        var surplus = effective.Where(x => x.Diff > 0).ToList();
        var shortage = effective.Where(x => x.Diff < 0).ToList();

        var title = $"اصلاح موجودی — انبارگردانی شماره {e.Number}";

        if (surplus.Count > 0)
        {
            var docType = await DocTypeAsync(SurplusDocCode, StockNature.Increase);
            var doc = new InvDoc
            {
                DocTypeId = docType,
                WarehouseId = e.WarehouseId,
                Date = e.Date,
                RefNumber = e.Number.ToString(),
                Description = $"{title} (اضافی انبار)",
                Lines = surplus.Select((x, i) => new InvDocLine
                {
                    RowNo = i + 1,
                    ProductId = x.ProductId,
                    Quantity = x.Diff,
                    UnitPrice = x.UnitCost,
                    Description = "اضافی انبارگردانی"
                }).ToList()
            };

            var saved = await _wh.SaveDocAsync(doc, user);
            await _wh.ConfirmDocAsync(saved.Id, user);
            await _acc.PostInventoryDocAsync(saved.Id, user);
            e.SurplusDocId = saved.Id;
        }

        if (shortage.Count > 0)
        {
            var docType = await DocTypeAsync(ShortageDocCode, StockNature.Decrease);
            var doc = new InvDoc
            {
                DocTypeId = docType,
                WarehouseId = e.WarehouseId,
                Date = e.Date,
                RefNumber = e.Number.ToString(),
                Description = $"{title} (کسری انبار)",
                Lines = shortage.Select((x, i) => new InvDocLine
                {
                    RowNo = i + 1,
                    ProductId = x.ProductId,
                    Quantity = Math.Abs(x.Diff),
                    UnitPrice = x.UnitCost,
                    Description = "کسری انبارگردانی"
                }).ToList()
            };

            var saved = await _wh.SaveDocAsync(doc, user);
            await _wh.ConfirmDocAsync(saved.Id, user);
            await _acc.PostInventoryDocAsync(saved.Id, user);
            e.ShortageDocId = saved.Id;
        }

        e.Status = StocktakeStatus.Applied;
        e.AppliedBy = user;
        e.AppliedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return (await GetSessionAsync(e.Id))!;
    }

    public async Task<StkSession> UnapplyAsync(int id, string? user)
    {
        var e = await _db.StkSessions.FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Applied)
            throw new InvalidOperationException("این دوره اعمال نشده است.");

        foreach (var docId in new[] { e.SurplusDocId, e.ShortageDocId })
        {
            if (docId is not > 0) continue;

            await _acc.UnpostInventoryDocAsync(docId.Value);

            var doc = await _db.InvDocs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId.Value);
            if (doc is null) continue;

            if (doc.Status == InvDocStatus.Confirmed) await _wh.UnconfirmDocAsync(docId.Value, user);
            await _wh.DeleteDocAsync(docId.Value);
        }

        e.SurplusDocId = null;
        e.ShortageDocId = null;
        e.Status = StocktakeStatus.Review;
        e.AppliedBy = null;
        e.AppliedAt = null;

        await _db.SaveChangesAsync();
        return (await GetSessionAsync(e.Id))!;
    }

    public async Task<StkSession> CancelSessionAsync(int id, string? user)
    {
        var e = await _db.StkSessions.FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status == StocktakeStatus.Applied)
            throw new InvalidOperationException("دوره اعمال‌شده را ابتدا برگشت بزنید.");
        if (e.Status == StocktakeStatus.Cancelled)
            throw new InvalidOperationException("این دوره قبلاً لغو شده است.");

        e.Status = StocktakeStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (await GetSessionAsync(e.Id))!;
    }

    public async Task DeleteSessionAsync(int id)
    {
        var e = await _db.StkSessions.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status == StocktakeStatus.Applied)
            throw new InvalidOperationException("ابتدا اعمال دوره را برگشت بزنید.");

        var scans = await _db.StkScans.Where(s => s.SessionId == id).ToListAsync();
        _db.StkScans.RemoveRange(scans);
        _db.StkLines.RemoveRange(e.Lines);
        _db.StkSessions.Remove(e);
        await _db.SaveChangesAsync();
    }

    /// <summary>شناسه نوع سند اصلاح موجودی — اگر نبود، اولین نوع سند با ماهیت مناسب.</summary>
    private async Task<int> DocTypeAsync(string code, StockNature nature)
    {
        var id = await _db.InvDocTypes.Where(t => t.Code == code && t.IsActive)
            .Select(t => (int?)t.Id).FirstOrDefaultAsync();
        if (id is > 0) return id.Value;

        id = await _db.InvDocTypes.Where(t => t.Nature == nature && t.IsActive && !t.IsTransfer)
            .OrderBy(t => t.SortOrder).Select(t => (int?)t.Id).FirstOrDefaultAsync();

        return id ?? throw new InvalidOperationException(
            $"نوع سند «{code}» تعریف نشده است؛ در «نوع رسید و حواله» آن را بسازید.");
    }

    // =====================================================================
    // ۴) ثبت شمارش
    // =====================================================================

    public async Task<StkCountResult> CountAsync(StkCountCommand cmd, string? user)
    {
        var e = await _db.StkSessions.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == cmd.SessionId)
            ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Counting)
            return Fail(e, "این دوره در مرحله شمارش نیست.");

        // ---------- شناسایی کالا ----------
        var productId = cmd.ProductId ?? 0;
        var qty = cmd.Quantity;
        string? barcode = null;

        if (productId <= 0)
        {
            if (string.IsNullOrWhiteSpace(cmd.Barcode))
                return Fail(e, "کالا یا بارکد را مشخص کنید.");

            var scan = await ScanAsync(cmd.Barcode, e.WarehouseId);
            if (!scan.Found) return Fail(e, scan.Message);

            productId = scan.ProductId;
            barcode = scan.Code;

            // اسکن یک کارتن یعنی PackQty واحد
            if (cmd.Accumulate && cmd.Quantity == 1 && scan.PackQty > 0) qty = scan.PackQty;
        }

        // ---------- یافتن یا ساختن سطر ----------
        var line = e.Lines.FirstOrDefault(l => l.ProductId == productId);
        var added = false;

        if (line is null)
        {
            // کالایی که در لیست نبود — در همه‌ی دامنه‌ها اضافه می‌شود (کالای بیگانه در انبار)
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
            if (product is null) return Fail(e, "کالا یافت نشد.");

            var st = await _db.InvStocks.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == e.WarehouseId);

            line = new Db.StkLine
            {
                SessionId = e.Id,
                RowNo = (e.Lines.Count == 0 ? 0 : e.Lines.Max(l => l.RowNo)) + 1,
                ProductId = productId,
                SystemQty = st?.Quantity ?? 0,
                UnitCost = st?.AvgCost ?? product.PurchasePrice,
                CountedQty = 0,
                IsCounted = false
            };
            e.Lines.Add(line);
            added = true;
        }

        // ---------- اعمال مقدار ----------
        if (cmd.Accumulate)
            line.CountedQty += qty;
        else
            line.CountedQty = qty;

        if (line.CountedQty < 0) line.CountedQty = 0;

        line.IsCounted = true;
        line.CountedBy = user;
        line.CountedAt = DateTime.Now;
        line.ScanCount++;
        if (!string.IsNullOrWhiteSpace(cmd.Note)) line.Note = cmd.Note.Trim();

        await _db.SaveChangesAsync();

        _db.StkScans.Add(new Db.StkScan
        {
            SessionId = e.Id,
            LineId = line.Id,
            ProductId = productId,
            Barcode = barcode,
            Quantity = qty,
            Accumulated = cmd.Accumulate,
            ScannedBy = user,
            ScannedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return await BuildResultAsync(e, line, added);
    }

    public async Task<StkCountResult> ClearLineAsync(int sessionId, int lineId, string? user)
    {
        var e = await _db.StkSessions.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status != StocktakeStatus.Counting)
            return Fail(e, "این دوره در مرحله شمارش نیست.");

        var line = e.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return Fail(e, "قلم یافت نشد.");

        line.CountedQty = 0;
        line.IsCounted = false;
        line.CountedBy = null;
        line.CountedAt = null;
        line.ScanCount = 0;

        var scans = await _db.StkScans.Where(s => s.LineId == lineId).ToListAsync();
        _db.StkScans.RemoveRange(scans);

        await _db.SaveChangesAsync();
        return await BuildResultAsync(e, line, false);
    }

    public async Task DeleteLineAsync(int sessionId, int lineId)
    {
        var e = await _db.StkSessions.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        if (e.Status is StocktakeStatus.Applied or StocktakeStatus.Cancelled)
            throw new InvalidOperationException("این دوره قابل ویرایش نیست.");

        var line = e.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new InvalidOperationException("قلم یافت نشد.");

        if (line.SystemQty != 0)
            throw new InvalidOperationException(
                "این کالا در انبار موجودی دارد و نمی‌تواند از لیست شمارش حذف شود.");

        var scans = await _db.StkScans.Where(s => s.LineId == lineId).ToListAsync();
        _db.StkScans.RemoveRange(scans);
        _db.StkLines.Remove(line);
        await _db.SaveChangesAsync();
    }

    private StkCountResult Fail(Db.StkSession e, string? message) => new()
    {
        Success = false,
        Message = message ?? "خطای نامشخص",
        TotalLines = e.Lines.Count,
        CountedLines = e.Lines.Count(l => l.IsCounted),
        DiffLines = e.Lines.Count(l => l.IsCounted && l.CountedQty != l.SystemQty)
    };

    private async Task<StkCountResult> BuildResultAsync(Db.StkSession e, Db.StkLine line, bool added)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == line.ProductId);
        var barcode = await _db.BcdBarcodes.AsNoTracking()
            .Where(b => b.ProductId == line.ProductId && b.IsPrimary && b.IsActive)
            .Select(b => b.Code).FirstOrDefaultAsync();

        return new StkCountResult
        {
            Success = true,
            LineAdded = added,
            Line = new StkLine
            {
                Id = line.Id,
                RowNo = line.RowNo,
                ProductId = line.ProductId,
                ProductCode = product?.Code ?? "",
                ProductName = product?.Name ?? "",
                Unit = product?.Unit ?? "",
                Barcode = barcode ?? product?.Barcode,
                ShelfCode = product?.ShelfCode,
                SystemQty = line.SystemQty,
                CountedQty = line.CountedQty,
                IsCounted = line.IsCounted,
                UnitCost = line.UnitCost,
                Note = line.Note,
                CountedBy = line.CountedBy,
                CountedAt = line.CountedAt,
                ScanCount = line.ScanCount
            },
            TotalLines = e.Lines.Count,
            CountedLines = e.Lines.Count(l => l.IsCounted),
            DiffLines = e.Lines.Count(l => l.IsCounted && l.CountedQty != l.SystemQty)
        };
    }

    // =====================================================================
    // ۵) گزارش مغایرت
    // =====================================================================

    public async Task<StkDiffResult> GetDiffAsync(int sessionId, bool onlyDiff)
    {
        var e = await _db.StkSessions.AsNoTracking()
            .Include(s => s.Warehouse)
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("دوره انبارگردانی یافت نشد.");

        var lines = e.Lines.AsEnumerable();
        if (onlyDiff) lines = lines.Where(l => l.IsCounted && l.CountedQty != l.SystemQty);

        var rows = lines.OrderBy(l => l.RowNo).Select(l => new StkDiffRow
        {
            ProductId = l.ProductId,
            ProductCode = l.Product?.Code ?? "",
            ProductName = l.Product?.Name ?? "",
            Unit = l.Product?.Unit ?? "",
            ShelfCode = l.Product?.ShelfCode,
            SystemQty = l.SystemQty,
            CountedQty = l.CountedQty,
            Diff = l.CountedQty - l.SystemQty,
            UnitCost = l.UnitCost,
            DiffValue = (l.CountedQty - l.SystemQty) * l.UnitCost,
            IsCounted = l.IsCounted,
            Note = l.Note
        }).ToList();

        var counted = e.Lines.Count(l => l.IsCounted);
        var diffLines = e.Lines.Count(l => l.IsCounted && l.CountedQty != l.SystemQty);

        var surplus = e.Lines.Where(l => l.IsCounted && l.CountedQty > l.SystemQty).ToList();
        var shortage = e.Lines.Where(l => l.IsCounted && l.CountedQty < l.SystemQty).ToList();

        var result = new StkDiffResult
        {
            SessionId = e.Id,
            Number = e.Number,
            Title = e.Title,
            WarehouseName = e.Warehouse?.Name ?? "",
            Date = e.Date,
            Status = e.Status,
            Rows = rows,
            TotalLines = e.Lines.Count,
            CountedLines = counted,
            UncountedLines = e.Lines.Count - counted,
            SurplusQty = surplus.Sum(l => l.CountedQty - l.SystemQty),
            ShortageQty = shortage.Sum(l => l.SystemQty - l.CountedQty),
            SurplusValue = surplus.Sum(l => (l.CountedQty - l.SystemQty) * l.UnitCost),
            ShortageValue = shortage.Sum(l => (l.SystemQty - l.CountedQty) * l.UnitCost),
            Accuracy = counted == 0 ? 0 : Math.Round((counted - diffLines) * 100m / counted, 1)
        };

        result.NetValue = result.SurplusValue - result.ShortageValue;
        return result;
    }

    // =====================================================================
    // کمکی
    // =====================================================================

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
