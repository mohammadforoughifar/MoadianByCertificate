using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// پیاده‌سازی ماژول انبارداری.
///
/// موتور قیمت‌گذاری: پس از هر تغییر در اسناد قطعی، دفتر کاردکس هر زوج (کالا، انبار)
/// از ابتدا بازسازی می‌شود؛ بنابراین ویرایش/ابطال اسناد گذشته هم مانده و بهای
/// تمام‌شده را دقیقاً اصلاح می‌کند. روش قیمت‌گذاری به ترتیب از «کالا»، سپس
/// «گروه کالا (و والدهایش)» و در نهایت «تنظیمات کلی» گرفته می‌شود.
/// </summary>
public class WarehousingService : IWarehousingService
{
    private readonly Db.AppDbContext _db;

    public WarehousingService(Db.AppDbContext db) => _db = db;

    // =====================================================================
    //  گروه کالا (درختی)
    // =====================================================================

    public Task<List<InvCategory>> GetCategoriesFlatAsync(bool activeOnly = false, Paging.Request? pagination = null)
        => BuildCategoriesAsync(activeOnly, pagination);

    private async Task<List<InvCategory>> BuildCategoriesAsync(bool activeOnly, Paging.Request? pagination)
    {
        var scope = _db.ProductCategories.AsNoTracking().Where(c => !activeOnly || c.IsActive);
        IOrderedQueryable<Db.ProductCategory> Order(IQueryable<Db.ProductCategory> q) => q
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id);
        var all = await Order(scope).ReadTreePageAsync(
            Order(scope.Where(c => c.ParentId == null || c.ParentId == 0)),
            ids => Order(scope.Where(c => c.ParentId != null && ids.Contains(c.ParentId.Value))),
            c => c.Id, pagination);
        var categoryIds = all.Select(c => c.Id).ToList();

        var counts = await _db.Products
            .Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value))
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);

        var vatStats = await _db.Products.AsNoTracking()
            .Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value))
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                HasVat = g.Any(p => p.IsVatIncluded),
                Rate = g.Where(p => p.IsVatIncluded).Select(p => p.VatRate).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.Id);

        var settingsMethod = await GetSettingsMethodAsync();
        var whSettings = await GetWarehousingSettingsAsync();
        var catCodeLen = whSettings.CategoryCodeLength > 0 ? whSettings.CategoryCodeLength : 2;
        var delim = whSettings.CodeDelimiter ?? "";
        var byParent = all.GroupBy(c => c.ParentId ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id).ToList());

        var result = new List<InvCategory>();

        void Walk(int parentKey, int depth, string path, string parentHierCode, ValuationMethod inherited, bool inheritedFlag)
        {
            if (!byParent.TryGetValue(parentKey, out var children)) return;
            foreach (var c in children)
            {
                var eff = c.Valuation ?? inherited;
                var isInherited = c.Valuation is null;
                var full = string.IsNullOrEmpty(path) ? c.Name : $"{path} ← {c.Name}";
                var vat = vatStats.TryGetValue(c.Id, out var vs) ? (vs.HasVat, vs.Rate > 0 ? vs.Rate : 10m) : (true, 10m);

                var stepCode = (c.Code ?? "").Trim();
                if (string.IsNullOrEmpty(stepCode))
                {
                    stepCode = c.Id.ToString().PadLeft(catCodeLen, '0');
                }
                else if (stepCode.All(char.IsDigit) && stepCode.Length < catCodeLen)
                {
                    stepCode = stepCode.PadLeft(catCodeLen, '0');
                }

                var hierCode = string.IsNullOrEmpty(parentHierCode)
                    ? stepCode
                    : (string.IsNullOrEmpty(delim) ? $"{parentHierCode}{stepCode}" : $"{parentHierCode}{delim}{stepCode}");

                result.Add(new InvCategory
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    ParentId = c.ParentId,
                    Description = c.Description,
                    IsActive = c.IsActive,
                    SortOrder = c.SortOrder,
                    Valuation = c.Valuation,
                    IsVatIncluded = vat.Item1,
                    VatRate = vat.Item2,
                    EffectiveValuation = eff,
                    ValuationInherited = isInherited,
                    CreatedAt = c.CreatedAt,
                    ProductCount = counts.TryGetValue(c.Id, out var n) ? n : 0,
                    Depth = depth,
                    FullPath = full,
                    HierarchyCode = hierCode
                });

                Walk(c.Id, depth + 1, full, hierCode, eff, isInherited);
            }
        }

        Walk(0, 0, "", "", settingsMethod, true);

        // تعداد کالا با احتساب زیرگروه‌ها
        var map = result.ToDictionary(c => c.Id);
        foreach (var c in result) c.TotalProductCount = c.ProductCount;
        foreach (var c in result)
        {
            var pid = c.ParentId;
            while (pid is > 0 && map.TryGetValue(pid.Value, out var parent))
            {
                parent.TotalProductCount += c.ProductCount;
                pid = parent.ParentId;
            }
        }

        return result;
    }

    public async Task<List<InvCategory>> GetCategoryTreeAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var flat = await BuildCategoriesAsync(activeOnly, pagination);
        var map = flat.ToDictionary(c => c.Id, c => c);
        var roots = new List<InvCategory>();

        foreach (var c in flat)
        {
            if (c.ParentId is > 0 && map.TryGetValue(c.ParentId.Value, out var parent))
                parent.Children.Add(c);
            else
                roots.Add(c);
        }
        return roots;
    }

    public async Task<InvCategory> SaveCategoryAsync(InvCategory dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام گروه کالا الزامی است.");

        if (await _db.ProductCategories.AnyAsync(c => c.Name == name && c.Id != dto.Id))
            throw new InvalidOperationException($"گروه کالایی با نام «{name}» قبلاً ثبت شده است.");

        if (dto.ParentId == dto.Id && dto.Id != 0)
            throw new InvalidOperationException("گروه نمی‌تواند والد خودش باشد.");

        if (dto.Id != 0 && dto.ParentId is > 0 && await IsDescendantAsync(dto.ParentId.Value, dto.Id))
            throw new InvalidOperationException("گروه والد نمی‌تواند یکی از زیرگروه‌های همین گروه باشد.");

        Db.ProductCategory entity;
        if (dto.Id == 0)
        {
            entity = new Db.ProductCategory { CreatedAt = DateTime.Now };
            _db.ProductCategories.Add(entity);
        }
        else
        {
            entity = await _db.ProductCategories.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("گروه کالا یافت نشد.");
        }

        var oldName = entity.Name;

        entity.Name = name;
        entity.Code = NullIfEmpty(dto.Code);
        entity.ParentId = dto.ParentId is > 0 ? dto.ParentId : null;
        entity.Description = NullIfEmpty(dto.Description);
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Valuation = dto.Valuation;
        entity.IsVatIncluded = dto.IsVatIncluded;
        entity.VatRate = dto.VatRate;

        await _db.SaveChangesAsync();

        // همگام‌سازی نام گروه روی کالاها (فیلد متنی قدیمی)
        if (!string.IsNullOrEmpty(oldName) && oldName != name)
        {
            var prods = await _db.Products.Where(p => p.CategoryId == entity.Id).ToListAsync();
            foreach (var p in prods) p.Category = name;
            await _db.SaveChangesAsync();
        }

        var flat = await GetCategoriesFlatAsync();
        return flat.First(c => c.Id == entity.Id);
    }

    public async Task<int> ApplyCategoryVatAsync(int categoryId, bool isVatIncluded, decimal vatRate)
    {
        var cat = await _db.ProductCategories.FindAsync(categoryId);
        if (cat is not null)
        {
            cat.IsVatIncluded = isVatIncluded;
            cat.VatRate = vatRate;
        }

        var subtreeIds = await CategorySubtreeIdsAsync(categoryId);
        var products = await _db.Products
            .Where(p => p.CategoryId.HasValue && subtreeIds.Contains(p.CategoryId.Value))
            .ToListAsync();

        foreach (var p in products)
        {
            p.IsVatIncluded = isVatIncluded;
            p.VatRate = vatRate;
        }

        await _db.SaveChangesAsync();
        return products.Count;
    }

    private async Task<List<int>> CategorySubtreeIdsAsync(int categoryId)
    {
        var all = await _db.ProductCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var result = new HashSet<int> { categoryId };
        bool added;
        do
        {
            added = false;
            foreach (var c in all)
            {
                if (c.ParentId.HasValue && result.Contains(c.ParentId.Value) && !result.Contains(c.Id))
                {
                    result.Add(c.Id);
                    added = true;
                }
            }
        } while (added);
        return result.ToList();
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var entity = await _db.ProductCategories.FindAsync(id)
                     ?? throw new InvalidOperationException("گروه کالا یافت نشد.");

        if (await _db.ProductCategories.AnyAsync(c => c.ParentId == id))
            throw new InvalidOperationException("این گروه زیرگروه دارد؛ ابتدا زیرگروه‌ها را حذف یا جابه‌جا کنید.");

        if (await _db.Products.AnyAsync(p => p.CategoryId == id))
            throw new InvalidOperationException("برای این گروه کالا ثبت شده است؛ امکان حذف وجود ندارد.");

        _db.ProductCategories.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task MoveCategoryAsync(InvCategoryMove cmd)
    {
        var entity = await _db.ProductCategories.FindAsync(cmd.Id)
                     ?? throw new InvalidOperationException("گروه کالا یافت نشد.");

        if (cmd.NewParentId is > 0)
        {
            if (cmd.NewParentId == cmd.Id)
                throw new InvalidOperationException("گروه نمی‌تواند والد خودش باشد.");
            if (await IsDescendantAsync(cmd.NewParentId.Value, cmd.Id))
                throw new InvalidOperationException("گروه والد نمی‌تواند یکی از زیرگروه‌های همین گروه باشد.");
        }

        entity.ParentId = cmd.NewParentId is > 0 ? cmd.NewParentId : null;
        entity.SortOrder = cmd.SortOrder;
        await _db.SaveChangesAsync();
    }

    /// <summary>آیا candidate یکی از فرزندان (در هر عمقی) ancestorId است؟</summary>
    private async Task<bool> IsDescendantAsync(int candidateId, int ancestorId)
    {
        var all = await _db.ProductCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var map = all.ToDictionary(c => c.Id, c => c.ParentId);

        var cur = candidateId;
        var guard = 0;
        while (map.TryGetValue(cur, out var parent) && parent is > 0 && guard++ < 100)
        {
            if (parent.Value == ancestorId) return true;
            cur = parent.Value;
        }
        return false;
    }

    // =====================================================================
    //  ویژگی‌های کالا
    // =====================================================================

    public async Task<List<InvAttribute>> GetAttributesAsync(bool activeOnly = false, int? categoryId = null, Paging.Request? pagination = null)
    {
        var q = _db.ProductAttributeDefs.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(a => a.IsActive);

        if (categoryId is > 0)
        {
            var chain = await CategoryChainAsync(categoryId.Value);
            q = q.Where(a => a.CategoryId == null || chain.Contains(a.CategoryId.Value));
        }
        var list = await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ThenBy(a => a.Id).ToPageListAsync(pagination);

        var ids = list.Select(a => a.Id).ToList();
        var options = await _db.ProductAttributeOptions.AsNoTracking()
            .Where(o => ids.Contains(o.AttributeId))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToListAsync();
        var usage = await _db.ProductAttributeValues
            .Where(v => ids.Contains(v.AttributeId))
            .GroupBy(v => v.AttributeId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);
        var catNames = await _db.ProductCategories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return list.Select(a => new InvAttribute
        {
            Id = a.Id,
            Name = a.Name,
            Code = a.Code,
            ValueType = a.ValueType,
            Unit = a.Unit,
            CategoryId = a.CategoryId,
            CategoryName = a.CategoryId is > 0 && catNames.TryGetValue(a.CategoryId.Value, out var cn) ? cn : null,
            IsRequired = a.IsRequired,
            ShowInList = a.ShowInList,
            SortOrder = a.SortOrder,
            IsActive = a.IsActive,
            Description = a.Description,
            UsageCount = usage.TryGetValue(a.Id, out var u) ? u : 0,
            Options = options.Where(o => o.AttributeId == a.Id)
                .Select(o => new InvAttributeOption { Id = o.Id, AttributeId = o.AttributeId, Title = o.Title, SortOrder = o.SortOrder })
                .ToList()
        }).ToList();
    }

    public async Task<InvAttribute> SaveAttributeAsync(InvAttribute dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام ویژگی الزامی است.");

        if (dto.ValueType == AttrValueType.List && dto.Options.Count(o => !string.IsNullOrWhiteSpace(o.Title)) == 0)
            throw new InvalidOperationException("برای ویژگی فهرستی حداقل یک گزینه تعریف کنید.");

        Db.ProductAttributeDef entity;
        if (dto.Id == 0)
        {
            entity = new Db.ProductAttributeDef { CreatedAt = DateTime.Now };
            _db.ProductAttributeDefs.Add(entity);
        }
        else
        {
            entity = await _db.ProductAttributeDefs.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("ویژگی یافت نشد.");
        }

        entity.Name = name;
        entity.Code = NullIfEmpty(dto.Code);
        entity.ValueType = dto.ValueType;
        entity.Unit = NullIfEmpty(dto.Unit);
        entity.CategoryId = dto.CategoryId is > 0 ? dto.CategoryId : null;
        entity.IsRequired = dto.IsRequired;
        entity.ShowInList = dto.ShowInList;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        entity.Description = NullIfEmpty(dto.Description);
        await _db.SaveChangesAsync();

        // ---------- گزینه‌ها ----------
        var current = await _db.ProductAttributeOptions.Where(o => o.AttributeId == entity.Id).ToListAsync();
        var keep = dto.Options.Where(o => !string.IsNullOrWhiteSpace(o.Title)).ToList();

        foreach (var old in current.Where(c => keep.All(k => k.Id != c.Id)))
            _db.ProductAttributeOptions.Remove(old);

        var i = 0;
        foreach (var o in keep)
        {
            var opt = current.FirstOrDefault(c => c.Id == o.Id && o.Id != 0);
            if (opt is null)
            {
                opt = new Db.ProductAttributeOption { AttributeId = entity.Id };
                _db.ProductAttributeOptions.Add(opt);
            }
            opt.Title = o.Title.Trim();
            opt.SortOrder = i++;
        }
        await _db.SaveChangesAsync();

        var all = await GetAttributesAsync();
        return all.First(a => a.Id == entity.Id);
    }

    public async Task DeleteAttributeAsync(int id)
    {
        var entity = await _db.ProductAttributeDefs.FindAsync(id)
                     ?? throw new InvalidOperationException("ویژگی یافت نشد.");

        var values = await _db.ProductAttributeValues.Where(v => v.AttributeId == id).ToListAsync();
        var options = await _db.ProductAttributeOptions.Where(o => o.AttributeId == id).ToListAsync();

        _db.ProductAttributeValues.RemoveRange(values);
        _db.ProductAttributeOptions.RemoveRange(options);
        _db.ProductAttributeDefs.Remove(entity);
        await _db.SaveChangesAsync();
    }

    /// <summary>زنجیره‌ی گروه و همه‌ی والدهایش (برای ارث‌بری ویژگی و روش قیمت‌گذاری).</summary>
    private async Task<List<int>> CategoryChainAsync(int categoryId)
    {
        var all = await _db.ProductCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var map = all.ToDictionary(c => c.Id, c => c.ParentId);

        var chain = new List<int>();
        var cur = (int?)categoryId;
        var guard = 0;
        while (cur is > 0 && guard++ < 100)
        {
            chain.Add(cur.Value);
            cur = map.TryGetValue(cur.Value, out var p) ? p : null;
        }
        return chain;
    }

    // =====================================================================
    //  کالا
    // =====================================================================

    public async Task<PagedResult<InvProduct>> GetProductsAsync(string? search, int? categoryId, int? warehouseId,
        bool belowOnly, bool activeOnly, int page, int pageSize)
    {
        var q = _db.Products.AsNoTracking().AsQueryable();

        if (activeOnly) q = q.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s)
                          || (p.Barcode != null && p.Barcode.Contains(s))
                          || (p.TaxCode != null && p.TaxCode.Contains(s))
                          || (p.Brand != null && p.Brand.Contains(s))
                          || (p.PartNumber != null && p.PartNumber.Contains(s)));
        }

        if (categoryId is > 0)
        {
            var ids = await DescendantCategoryIdsAsync(categoryId.Value);
            q = q.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
        }

        if (warehouseId is > 0)
            q = q.Where(p => p.WarehouseId == null || p.WarehouseId == warehouseId);

        var stockQuery = _db.InvStocks.AsNoTracking()
            .Where(s => warehouseId == null || warehouseId == 0 || s.WarehouseId == warehouseId);
        if (belowOnly)
        {
            // SQLite 8 cannot SUM decimal. Only the quantity predicate uses REAL,
            // rounded to the model's 3 decimal places; monetary values stay decimal.
            if (_db.Database.IsSqlite())
                q = q.Where(p => p.ReorderPoint > 0 && Math.Round(stockQuery
                    .Where(s => s.ProductId == p.Id).Sum(s => (double?)s.Quantity) ?? 0, 3) <= (double)p.ReorderPoint);
            else
                q = q.Where(p => p.ReorderPoint > 0 && (stockQuery
                    .Where(s => s.ProductId == p.Id).Sum(s => (decimal?)s.Quantity) ?? 0) <= p.ReorderPoint);
        }
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var total = await q.CountAsync();
        var list = await q.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        var productIds = list.Select(p => p.Id).ToList();
        var stocks = await stockQuery.Where(s => productIds.Contains(s.ProductId)).ToListAsync();

        var qtyMap = stocks.GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => new { Qty = g.Sum(x => x.Quantity), Val = g.Sum(x => x.Value) });

        var items = new List<InvProduct>();
        var catFlat = await GetCategoriesFlatAsync();
        var catMap = catFlat.ToDictionary(c => c.Id);
        var whMap = await _db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => (w.Name, w.Valuation));
        var settingsMethod = await GetSettingsMethodAsync();

        foreach (var p in list)
        {
            var dto = MapProduct(p, catMap, whMap, settingsMethod);
            if (qtyMap.TryGetValue(p.Id, out var st))
            {
                dto.TotalStock = st.Qty;
                dto.StockValue = st.Val;
                dto.AvgCost = st.Qty != 0 ? st.Val / st.Qty : 0;
            }
            items.Add(dto);
        }

        return new PagedResult<InvProduct> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<InvProduct?> GetProductAsync(int id)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return null;

        var catFlat = await GetCategoriesFlatAsync();
        var catMap = catFlat.ToDictionary(c => c.Id);
        var whMap = await _db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => (w.Name, w.Valuation));
        var dto = MapProduct(p, catMap, whMap, await GetSettingsMethodAsync());

        var st = await _db.InvStocks.AsNoTracking().Where(s => s.ProductId == id).ToListAsync();
        dto.TotalStock = st.Sum(s => s.Quantity);
        dto.StockValue = st.Sum(s => s.Value);
        dto.AvgCost = dto.TotalStock != 0 ? dto.StockValue / dto.TotalStock : 0;

        dto.Attributes = await BuildAttributeValuesAsync(p.CategoryId, id);
        return dto;
    }

    public async Task<InvProduct> NewProductAsync(int? categoryId)
    {
        var whSettings = await GetWarehousingSettingsAsync();
        var settingsMethod = await GetSettingsMethodAsync();
        var dto = new InvProduct
        {
            IsActive = true,
            Unit = "عدد",
            VatRate = whSettings.DefaultVatRate > 0 ? whSettings.DefaultVatRate : 10,
            IsVatIncluded = true,
            CategoryId = categoryId is > 0 ? categoryId : null,
            EffectiveValuation = settingsMethod,
            ValuationSource = "تنظیمات کلی"
        };

        InvCategory? cat = null;
        if (categoryId is > 0)
        {
            var catFlat = await GetCategoriesFlatAsync();
            cat = catFlat.FirstOrDefault(c => c.Id == categoryId);
            if (cat is not null)
            {
                dto.CategoryName = cat.Name;
                dto.CategoryPath = cat.FullPath;
                dto.EffectiveValuation = cat.EffectiveValuation;
                dto.ValuationSource = $"گروه کالا: {cat.Name}";
                if (cat.IsVatIncluded.HasValue)
                {
                    dto.IsVatIncluded = cat.IsVatIncluded.Value;
                    dto.VatRate = cat.VatRate ?? whSettings.DefaultVatRate;
                }
            }
        }

        var (prefix, suffix, code) = await GenerateProductCodeAsync(cat, whSettings);
        dto.CategoryCodePrefix = prefix;
        dto.ProductCodeSuffix = suffix;
        dto.Code = code;

        dto.Attributes = await BuildAttributeValuesAsync(dto.CategoryId, 0);
        return dto;
    }

    public async Task<InvProduct> SaveProductAsync(InvProduct dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام کالا الزامی است.");

        var whSettings = await GetWarehousingSettingsAsync();
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            InvCategory? cat = null;
            if (dto.CategoryId is > 0)
            {
                var catFlat = await GetCategoriesFlatAsync();
                cat = catFlat.FirstOrDefault(c => c.Id == dto.CategoryId);
            }
            var (_, _, generated) = await GenerateProductCodeAsync(cat, whSettings);
            code = generated;
        }

        if (await _db.Products.AnyAsync(p => p.Code == code && p.Id != dto.Id))
            throw new InvalidOperationException($"کالایی با کد «{code}» قبلاً ثبت شده است.");

        if (!string.IsNullOrWhiteSpace(dto.TaxCode))
        {
            var tc = dto.TaxCode.Trim();
            if (tc.Length is < 5 or > 20)
                throw new InvalidOperationException("کد (شناسه) کالای مالیاتی معتبر نیست.");
            if (await _db.Products.AnyAsync(p => p.TaxCode == tc && p.Id != dto.Id))
                throw new InvalidOperationException($"شناسه کالای مالیاتی «{tc}» برای کالای دیگری ثبت شده است.");
        }

        // ---------- بررسی یکتایی کد مالیاتی ----------

        Db.Product entity;
        if (dto.Id == 0)
        {
            entity = new Db.Product { CreatedAt = DateTime.Now };
            _db.Products.Add(entity);
        }
        else
        {
            entity = await _db.Products.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("کالا یافت نشد.");
        }

        var catName = dto.CategoryId is > 0
            ? (await _db.ProductCategories.FindAsync(dto.CategoryId.Value))?.Name
            : null;

        entity.Code = code;
        entity.Name = name;
        entity.EnName = NullIfEmpty(dto.EnName);
        entity.CategoryId = dto.CategoryId is > 0 ? dto.CategoryId : null;
        entity.Category = catName;
        entity.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "عدد" : dto.Unit.Trim();
        entity.SecondUnit = NullIfEmpty(dto.SecondUnit);
        entity.UnitFactor = dto.UnitFactor;
        entity.Brand = NullIfEmpty(dto.Brand);
        entity.Model = NullIfEmpty(dto.Model);
        entity.PartNumber = NullIfEmpty(dto.PartNumber);
        entity.Barcode = NullIfEmpty(dto.Barcode);
        entity.IsActive = dto.IsActive;
        entity.IsService = dto.IsService;

        entity.PurchasePrice = dto.PurchasePrice;
        entity.SalePrice = dto.SalePrice;
        entity.SalePrice2 = dto.SalePrice2;
        entity.TaxCode = NullIfEmpty(dto.TaxCode);
        entity.TaxUnitCode = NullIfEmpty(dto.TaxUnitCode);
        entity.IsVatIncluded = dto.IsVatIncluded;
        entity.VatRate = dto.VatRate;
        entity.DutyRate = dto.DutyRate;
        entity.OtherTaxRate = dto.OtherTaxRate;
        entity.CustomsCode = NullIfEmpty(dto.CustomsCode);
        entity.CountryOfOrigin = NullIfEmpty(dto.CountryOfOrigin);

        entity.WarehouseId = dto.WarehouseId is > 0 ? dto.WarehouseId : null;
        entity.Valuation = dto.Valuation;
        entity.ReorderPoint = dto.ReorderPoint;
        entity.MaxStock = dto.MaxStock;
        entity.MinOrderQty = dto.MinOrderQty;
        entity.ShelfCode = NullIfEmpty(dto.ShelfCode);
        entity.TrackBatch = dto.TrackBatch;
        entity.TrackSerial = dto.TrackSerial;
        entity.TrackExpiry = dto.TrackExpiry;
        entity.ShelfLifeDays = dto.ShelfLifeDays;
        entity.Weight = dto.Weight;
        entity.Length = dto.Length;
        entity.Width = dto.Width;
        entity.Height = dto.Height;

        entity.Description = NullIfEmpty(dto.Description);
        entity.Note = NullIfEmpty(dto.Note);
        entity.ImageUrl = NullIfEmpty(dto.ImageUrl);

        await _db.SaveChangesAsync();

        // ---------- ویژگی‌های کالا و الزام آن‌ها ----------
        var currentValues = await _db.ProductAttributeValues.Where(v => v.ProductId == entity.Id).ToListAsync();
        var incomingAttrIds = dto.Attributes.Where(v => v.AttributeId > 0).Select(v => v.AttributeId).ToHashSet();
        var toRemove = currentValues.Where(x => !incomingAttrIds.Contains(x.AttributeId)).ToList();
        _db.ProductAttributeValues.RemoveRange(toRemove);

        foreach (var v in dto.Attributes.Where(x => x.AttributeId > 0))
        {
            var row = currentValues.FirstOrDefault(x => x.AttributeId == v.AttributeId);
            if (row is null)
            {
                row = new Db.ProductAttributeValue { ProductId = entity.Id, AttributeId = v.AttributeId };
                _db.ProductAttributeValues.Add(row);
            }
            row.IsRequired = v.IsRequired;
            row.OptionId = v.OptionId is > 0 ? v.OptionId : null;
            row.TextValue = NullIfEmpty(v.TextValue);
            row.NumberValue = v.NumberValue;
            row.BoolValue = v.BoolValue;
            row.DateValue = v.DateValue;
        }
        await _db.SaveChangesAsync();

        return (await GetProductAsync(entity.Id))!;
    }

    public async Task DeleteProductAsync(int id)
    {
        var entity = await _db.Products.FindAsync(id)
                     ?? throw new InvalidOperationException("کالا یافت نشد.");

        if (await _db.InvDocLines.AnyAsync(l => l.ProductId == id))
            throw new InvalidOperationException("برای این کالا سند انبار ثبت شده است؛ امکان حذف وجود ندارد. می‌توانید آن را غیرفعال کنید.");

        if (await _db.TransactionLines.AnyAsync(l => l.ProductId == id))
            throw new InvalidOperationException("این کالا در اسناد خرید/فروش استفاده شده است؛ امکان حذف وجود ندارد.");

        var values = await _db.ProductAttributeValues.Where(v => v.ProductId == id).ToListAsync();
        _db.ProductAttributeValues.RemoveRange(values);

        var invStocks = await _db.InvStocks.Where(s => s.ProductId == id).ToListAsync();
        _db.InvStocks.RemoveRange(invStocks);

        var stocks = await _db.Stocks.Where(s => s.ProductId == id).ToListAsync();
        _db.Stocks.RemoveRange(stocks);

        _db.Products.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<List<LookupItem>> GetProductLookupsAsync(string? search = null, int? warehouseId = null, Paging.Request? pagination = null)
    {
        var q = _db.Products.AsNoTracking().Where(p => p.IsActive && !p.IsService);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s) || (p.Barcode != null && p.Barcode.Contains(s)));
        }
        if (warehouseId is > 0)
            q = q.Where(p => p.WarehouseId == null || p.WarehouseId == warehouseId);

        return await q.OrderBy(p => p.Name).ThenBy(x => x.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.Code + " — " + p.Name })
            .ToPageListAsync(pagination, defaultCap: 500);
    }

    private static bool IsEmptyValue(InvProductAttrValue v) =>
        v.OptionId is null or 0
        && string.IsNullOrWhiteSpace(v.TextValue)
        && v.NumberValue is null
        && v.BoolValue is null
        && v.DateValue is null;

    /// <summary>ساخت فهرست ویژگی‌های قابل ثبت برای یک کالا (تعریف‌ها + مقدار فعلی).</summary>
    private async Task<List<InvProductAttrValue>> BuildAttributeValuesAsync(int? categoryId, int productId)
    {
        var defs = await GetAttributesAsync(activeOnly: true, categoryId: categoryId);
        var values = productId == 0
            ? new List<Db.ProductAttributeValue>()
            : await _db.ProductAttributeValues.AsNoTracking().Where(v => v.ProductId == productId).ToListAsync();

        // ویژگی‌هایی که مقدار دارند ولی دیگر به گروه فعلی مربوط نیستند هم نمایش داده می‌شوند
        var extraIds = values.Select(v => v.AttributeId).Except(defs.Select(d => d.Id)).ToList();
        if (extraIds.Count > 0)
        {
            var extras = (await GetAttributesAsync()).Where(a => extraIds.Contains(a.Id));
            defs = defs.Concat(extras).ToList();
        }

        var list = new List<InvProductAttrValue>();
        foreach (var d in defs.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
        {
            var v = values.FirstOrDefault(x => x.AttributeId == d.Id);
            var item = new InvProductAttrValue
            {
                Id = v?.Id ?? 0,
                AttributeId = d.Id,
                AttributeName = d.Name,
                ValueType = d.ValueType,
                Unit = d.Unit,
                IsRequired = v is not null ? v.IsRequired : d.IsRequired,
                OptionId = v?.OptionId,
                TextValue = v?.TextValue,
                NumberValue = v?.NumberValue,
                BoolValue = v?.BoolValue,
                DateValue = v?.DateValue,
                Options = d.Options
            };
            item.Display = DisplayValue(item);
            list.Add(item);
        }
        return list;
    }

    private static string DisplayValue(InvProductAttrValue v) => v.ValueType switch
    {
        AttrValueType.Text => v.TextValue ?? "",
        AttrValueType.Number => v.NumberValue?.ToString("0.###") ?? "",
        AttrValueType.Boolean => v.BoolValue is null ? "" : (v.BoolValue.Value ? "بله" : "خیر"),
        AttrValueType.Date => v.DateValue is null ? "" : PersianDate.ToShort(v.DateValue.Value),
        AttrValueType.List => v.Options.FirstOrDefault(o => o.Id == v.OptionId)?.Title ?? "",
        _ => ""
    };

    private InvProduct MapProduct(Db.Product p, Dictionary<int, InvCategory> catMap,
        Dictionary<int, (string Name, ValuationMethod? Valuation)> whMap, ValuationMethod settingsMethod)
    {
        InvCategory? cat = p.CategoryId is > 0 && catMap.TryGetValue(p.CategoryId.Value, out var c) ? c : null;
        (string Name, ValuationMethod? Valuation) whInfo = default;
        var hasWh = p.WarehouseId is > 0 && whMap.TryGetValue(p.WarehouseId.Value, out whInfo);

        // اولویت قیمت‌گذاری: ۱) کالا ← ۲) گروه کالا ← ۳) انبار ← ۴) تنظیمات کلی سیستم
        ValuationMethod eff;
        string src;

        if (p.Valuation is not null)
        {
            eff = p.Valuation.Value;
            src = "کالا (اولویت ۱)";
        }
        else if (cat is not null && (cat.Valuation is not null || !cat.ValuationInherited))
        {
            eff = cat.EffectiveValuation;
            src = cat.Valuation is not null ? $"گروه کالا: {cat.Name} (اولویت ۲)" : "گروه کالای والد (اولویت ۲)";
        }
        else if (hasWh && whInfo.Valuation is not null)
        {
            eff = whInfo.Valuation.Value;
            src = $"انبار: {whInfo.Name} (اولویت ۳)";
        }
        else
        {
            eff = cat?.EffectiveValuation ?? settingsMethod;
            src = "تنظیمات کلی سیستم (پیش‌فرض)";
        }

        string prefix = cat?.HierarchyCode ?? "";
        string suffix = p.Code ?? "";
        if (!string.IsNullOrEmpty(prefix) && suffix.StartsWith(prefix))
        {
            suffix = suffix.Substring(prefix.Length).TrimStart('-', '/', '.');
        }

        return new InvProduct
        {
            Id = p.Id,
            Code = p.Code,
            CategoryCodePrefix = prefix,
            ProductCodeSuffix = suffix,
            Name = p.Name,
            EnName = p.EnName,
            CategoryId = p.CategoryId,
            CategoryName = cat?.Name ?? p.Category,
            CategoryPath = cat?.FullPath,
            Unit = p.Unit,
            SecondUnit = p.SecondUnit,
            UnitFactor = p.UnitFactor,
            Brand = p.Brand,
            Model = p.Model,
            PartNumber = p.PartNumber,
            Barcode = p.Barcode,
            IsActive = p.IsActive,
            IsService = p.IsService,

            PurchasePrice = p.PurchasePrice,
            SalePrice = p.SalePrice,
            SalePrice2 = p.SalePrice2,
            TaxCode = p.TaxCode,
            TaxUnitCode = p.TaxUnitCode,
            IsVatIncluded = p.IsVatIncluded,
            VatRate = p.VatRate,
            DutyRate = p.DutyRate,
            OtherTaxRate = p.OtherTaxRate,
            CustomsCode = p.CustomsCode,
            CountryOfOrigin = p.CountryOfOrigin,

            WarehouseId = p.WarehouseId,
            WarehouseName = hasWh ? whInfo.Name : null,
            Valuation = p.Valuation,
            EffectiveValuation = eff,
            ValuationSource = src,
            ReorderPoint = p.ReorderPoint,
            MaxStock = p.MaxStock,
            MinOrderQty = p.MinOrderQty,
            ShelfCode = p.ShelfCode,
            TrackBatch = p.TrackBatch,
            TrackSerial = p.TrackSerial,
            TrackExpiry = p.TrackExpiry,
            ShelfLifeDays = p.ShelfLifeDays,
            Weight = p.Weight,
            Length = p.Length,
            Width = p.Width,
            Height = p.Height,

            Description = p.Description,
            Note = p.Note,
            ImageUrl = p.ImageUrl,
            CreatedAt = p.CreatedAt
        };
    }

    private async Task<List<int>> DescendantCategoryIdsAsync(int rootId)
    {
        var all = await _db.ProductCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId }).ToListAsync();

        var result = new List<int> { rootId };
        var added = true;
        var guard = 0;
        while (added && guard++ < 50)
        {
            added = false;
            foreach (var c in all)
            {
                if (c.ParentId is > 0 && result.Contains(c.ParentId.Value) && !result.Contains(c.Id))
                {
                    result.Add(c.Id);
                    added = true;
                }
            }
        }
        return result;
    }

    private async Task<string> NextProductCodeAsync()
    {
        var whSettings = await GetWarehousingSettingsAsync();
        var (_, _, fullCode) = await GenerateProductCodeAsync(null, whSettings);
        return fullCode;
    }

    private async Task<(string prefix, string suffix, string fullCode)> GenerateProductCodeAsync(
        InvCategory? cat, WarehousingSettingsDto settings)
    {
        var prodLen = settings.ProductCodeLength > 0 ? settings.ProductCodeLength : 4;
        var delim = settings.CodeDelimiter ?? "";

        string prefix = "";
        if (cat is not null && settings.AutoCodeFromCategory)
        {
            prefix = (cat.HierarchyCode ?? "").Trim();
        }

        int maxSerial = 0;
        var query = _db.Products.AsNoTracking().AsQueryable();
        if (cat is not null && !string.IsNullOrEmpty(prefix))
        {
            query = query.Where(p => p.CategoryId == cat.Id || (p.Code != null && p.Code.StartsWith(prefix)));
        }

        var codes = await query.Select(p => p.Code).ToListAsync();
        foreach (var c in codes)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var rem = c.Trim();
            if (!string.IsNullOrEmpty(prefix) && rem.StartsWith(prefix))
            {
                rem = rem.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(delim) && rem.StartsWith(delim))
                    rem = rem.Substring(delim.Length);
            }
            var digits = new string(rem.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > maxSerial)
                maxSerial = n;
        }

        var nextSerial = (maxSerial + 1).ToString().PadLeft(prodLen, '0');
        string fullCode;
        if (string.IsNullOrEmpty(prefix))
        {
            fullCode = nextSerial;
        }
        else
        {
            fullCode = string.IsNullOrEmpty(delim) ? $"{prefix}{nextSerial}" : $"{prefix}{delim}{nextSerial}";
        }

        return (prefix, nextSerial, fullCode);
    }

    // =====================================================================
    //  تنظیمات جامع انبارداری و ساختار کدینگ
    // =====================================================================

    public async Task<WarehousingSettingsDto> GetWarehousingSettingsAsync()
    {
        var s = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        if (s is null) return new WarehousingSettingsDto();
        return new WarehousingSettingsDto
        {
            CostingMethod = s.CostingMethod ?? "Average",
            AllowNegativeStock = s.AllowNegativeStock,
            DefaultVatRate = s.DefaultVatRate > 0 ? s.DefaultVatRate : 10m,
            DefaultDutyRate = s.DefaultDutyRate,
            CategoryCodeLength = s.CategoryCodeLength > 0 ? s.CategoryCodeLength : 2,
            ProductCodeLength = s.ProductCodeLength > 0 ? s.ProductCodeLength : 4,
            AutoCodeFromCategory = s.AutoCodeFromCategory,
            CodeDelimiter = s.CodeDelimiter ?? ""
        };
    }

    public async Task<WarehousingSettingsDto> SaveWarehousingSettingsAsync(WarehousingSettingsDto dto)
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync();
        if (s is null)
        {
            s = new Db.AppSetting();
            _db.AppSettings.Add(s);
        }
        if (!string.IsNullOrWhiteSpace(dto.CostingMethod)) s.CostingMethod = dto.CostingMethod;
        s.AllowNegativeStock = dto.AllowNegativeStock;
        s.DefaultVatRate = dto.DefaultVatRate;
        s.DefaultDutyRate = dto.DefaultDutyRate;
        s.CategoryCodeLength = Math.Clamp(dto.CategoryCodeLength, 1, 10);
        s.ProductCodeLength = Math.Clamp(dto.ProductCodeLength, 1, 15);
        s.AutoCodeFromCategory = dto.AutoCodeFromCategory;
        s.CodeDelimiter = dto.CodeDelimiter ?? "";
        await _db.SaveChangesAsync();
        return await GetWarehousingSettingsAsync();
    }

    // =====================================================================
    //  انبارها
    // =====================================================================

    public async Task<List<InvWarehouse>> GetWarehousesAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var q = _db.Warehouses.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(w => w.IsActive);
        var list = await q.OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var ids = list.Select(w => w.Id).ToList();
        var stocks = await _db.InvStocks.AsNoTracking().Where(s => ids.Contains(s.WarehouseId)).ToListAsync();
        var docCounts = await _db.InvDocs.GroupBy(d => d.WarehouseId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);

        return list.Select(w =>
        {
            var st = stocks.Where(s => s.WarehouseId == w.Id).ToList();
            return new InvWarehouse
            {
                Id = w.Id,
                Code = w.Code,
                Name = w.Name,
                Kind = w.Kind,
                Valuation = w.Valuation,
                Address = w.Address,
                Phone = w.Phone,
                KeeperName = w.KeeperName,
                IsDefault = w.IsDefault,
                AllowNegative = w.AllowNegative,
                IsActive = w.IsActive,
                Note = w.Note,
                ItemCount = st.Count(s => s.Quantity != 0),
                TotalQuantity = st.Sum(s => s.Quantity),
                TotalValue = st.Sum(s => s.Value),
                DocCount = docCounts.TryGetValue(w.Id, out var n) ? n : 0
            };
        }).ToList();
    }

    public async Task<InvWarehouse> SaveWarehouseAsync(InvWarehouse dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام انبار الزامی است.");

        if (await _db.Warehouses.AnyAsync(w => w.Name == name && w.Id != dto.Id))
            throw new InvalidOperationException($"انباری با نام «{name}» قبلاً ثبت شده است.");

        var code = NullIfEmpty(dto.Code);
        if (code is not null && await _db.Warehouses.AnyAsync(w => w.Code == code && w.Id != dto.Id))
            throw new InvalidOperationException($"کد انبار «{code}» تکراری است.");

        Db.Warehouse entity;
        if (dto.Id == 0)
        {
            entity = new Db.Warehouse();
            _db.Warehouses.Add(entity);
        }
        else
        {
            entity = await _db.Warehouses.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("انبار یافت نشد.");
        }

        entity.Name = name;
        entity.Code = code;
        entity.Kind = dto.Kind;
        entity.Valuation = dto.Valuation;
        entity.Address = NullIfEmpty(dto.Address);
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.KeeperName = NullIfEmpty(dto.KeeperName);
        entity.AllowNegative = dto.AllowNegative;
        entity.IsActive = dto.IsActive;
        entity.Note = NullIfEmpty(dto.Note);
        entity.IsDefault = dto.IsDefault;

        await _db.SaveChangesAsync();

        if (dto.IsDefault)
        {
            var others = await _db.Warehouses.Where(w => w.Id != entity.Id && w.IsDefault).ToListAsync();
            foreach (var o in others) o.IsDefault = false;
            await _db.SaveChangesAsync();
        }

        return (await GetWarehousesAsync()).First(w => w.Id == entity.Id);
    }

    public async Task DeleteWarehouseAsync(int id)
    {
        var entity = await _db.Warehouses.FindAsync(id)
                     ?? throw new InvalidOperationException("انبار یافت نشد.");

        if (await _db.InvDocs.AnyAsync(d => d.WarehouseId == id || d.CounterWarehouseId == id))
            throw new InvalidOperationException("برای این انبار سند ثبت شده است؛ امکان حذف وجود ندارد.");

        if (await _db.InvStocks.AnyAsync(s => s.WarehouseId == id && s.Quantity != 0))
            throw new InvalidOperationException("این انبار موجودی دارد؛ امکان حذف وجود ندارد.");

        if (await _db.Transactions.AnyAsync(t => t.WarehouseId == id))
            throw new InvalidOperationException("این انبار در اسناد خرید/فروش استفاده شده است؛ امکان حذف وجود ندارد.");

        _db.Warehouses.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    //  انواع رسید و حواله
    // =====================================================================

    public async Task<List<InvDocType>> GetDocTypesAsync(bool activeOnly = false, StockNature? nature = null, Paging.Request? pagination = null)
    {
        var q = _db.InvDocTypes.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(t => t.IsActive);
        if (nature.HasValue) q = q.Where(t => t.Nature == nature.Value);

        var list = await q.OrderBy(t => t.Nature).ThenBy(t => t.SortOrder).ThenBy(t => t.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var counts = await _db.InvDocs.GroupBy(d => d.DocTypeId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);

        return list.Select(t => MapDocType(t, counts.TryGetValue(t.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<InvDocType> SaveDocTypeAsync(InvDocType dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام نوع سند الزامی است.");

        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("کد نوع سند الزامی است.");

        if (await _db.InvDocTypes.AnyAsync(t => t.Code == code && t.Id != dto.Id))
            throw new InvalidOperationException($"کد «{code}» برای نوع سند دیگری ثبت شده است.");

        Db.InvDocType entity;
        if (dto.Id == 0)
        {
            entity = new Db.InvDocType { CreatedAt = DateTime.Now };
            _db.InvDocTypes.Add(entity);
        }
        else
        {
            entity = await _db.InvDocTypes.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("نوع سند یافت نشد.");

            // تغییر ماهیت نوع سندی که سند قطعی دارد، مانده انبار را به‌هم می‌ریزد
            if (entity.Nature != dto.Nature &&
                await _db.InvDocs.AnyAsync(d => d.DocTypeId == entity.Id && d.Status == InvDocStatus.Confirmed))
                throw new InvalidOperationException("برای این نوع سند، سند قطعی ثبت شده است؛ تغییر ماهیت مجاز نیست.");
        }

        entity.Code = code;
        entity.Name = name;
        entity.Nature = dto.Nature;
        entity.IsTransfer = dto.Nature == StockNature.Neutral && dto.IsTransfer;
        entity.RequiresParty = dto.RequiresParty;
        entity.RequiresPrice = dto.RequiresPrice;
        entity.NumberPrefix = NullIfEmpty(dto.NumberPrefix) ?? code;
        entity.Color = NullIfEmpty(dto.Color);
        entity.Icon = NullIfEmpty(dto.Icon);
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Description = NullIfEmpty(dto.Description);

        await _db.SaveChangesAsync();
        return MapDocType(entity, await _db.InvDocs.CountAsync(d => d.DocTypeId == entity.Id));
    }

    public async Task DeleteDocTypeAsync(int id)
    {
        var entity = await _db.InvDocTypes.FindAsync(id)
                     ?? throw new InvalidOperationException("نوع سند یافت نشد.");

        if (entity.IsSystem)
            throw new InvalidOperationException("این نوع سند سیستمی است و حذف نمی‌شود؛ می‌توانید آن را غیرفعال کنید.");

        if (await _db.InvDocs.AnyAsync(d => d.DocTypeId == id))
            throw new InvalidOperationException("برای این نوع سند، سند ثبت شده است؛ امکان حذف وجود ندارد.");

        _db.InvDocTypes.Remove(entity);
        await _db.SaveChangesAsync();
    }

    private static InvDocType MapDocType(Db.InvDocType t, int docCount) => new()
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Nature = t.Nature,
        IsTransfer = t.IsTransfer,
        RequiresParty = t.RequiresParty,
        RequiresPrice = t.RequiresPrice,
        NumberPrefix = t.NumberPrefix,
        Color = t.Color,
        Icon = t.Icon,
        IsSystem = t.IsSystem,
        IsActive = t.IsActive,
        SortOrder = t.SortOrder,
        Description = t.Description,
        DocCount = docCount
    };

    // =====================================================================
    //  اسناد انبار
    // =====================================================================

    public async Task<PagedResult<InvDoc>> GetDocsAsync(StockNature? nature, int? docTypeId, int? warehouseId,
        InvDocStatus? status, string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var types = await _db.InvDocTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);

        var q = _db.InvDocs.AsNoTracking().AsQueryable();

        if (docTypeId is > 0) q = q.Where(d => d.DocTypeId == docTypeId);
        else if (nature.HasValue)
        {
            var ids = types.Values.Where(t => t.Nature == nature.Value).Select(t => t.Id).ToList();
            q = q.Where(d => ids.Contains(d.DocTypeId));
        }

        if (warehouseId is > 0) q = q.Where(d => d.WarehouseId == warehouseId || d.CounterWarehouseId == warehouseId);
        if (status.HasValue) q = q.Where(d => d.Status == status.Value);
        if (from.HasValue) q = q.Where(d => d.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(d => d.Date < to.Value.Date.AddDays(1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(d => d.Number.Contains(s)
                          || (d.RefNumber != null && d.RefNumber.Contains(s))
                          || (d.Description != null && d.Description.Contains(s)));
        }

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync();

        var whNames = await _db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name);
        var partyNames = await _db.Parties.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);
        var docIds = list.Select(d => d.Id).ToList();
        var lineCounts = await _db.InvDocLines.Where(l => docIds.Contains(l.DocId))
            .GroupBy(l => l.DocId).Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);

        var items = list.Select(d => MapDoc(d, types, whNames, partyNames,
            lineCounts.TryGetValue(d.Id, out var n) ? n : 0)).ToList();

        return new PagedResult<InvDoc> { Items = items, TotalCount = total };
    }

    public async Task<InvDoc?> GetDocAsync(int id)
    {
        var d = await _db.InvDocs.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return null;

        var types = await _db.InvDocTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var whNames = await _db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name);
        var partyNames = await _db.Parties.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

        var dto = MapDoc(d, types, whNames, partyNames, d.Lines.Count);

        var productIds = d.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var stocks = await _db.InvStocks.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && s.WarehouseId == d.WarehouseId)
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

        dto.Lines = d.Lines.OrderBy(l => l.RowNo).ThenBy(l => l.Id).Select(l => new InvDocLine
        {
            Id = l.Id,
            RowNo = l.RowNo,
            ProductId = l.ProductId,
            ProductCode = products.TryGetValue(l.ProductId, out var p) ? p.Code : "",
            ProductName = products.TryGetValue(l.ProductId, out var p2) ? p2.Name : "—",
            Unit = products.TryGetValue(l.ProductId, out var p3) ? p3.Unit : "",
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            Discount = l.Discount,
            IsVatIncluded = l.IsVatIncluded,
            VatRate = l.VatRate,
            BatchNo = l.BatchNo,
            SerialNo = l.SerialNo,
            ExpiryDate = l.ExpiryDate,
            Description = l.Description,
            OutCost = l.OutCost,
            CurrentStock = stocks.TryGetValue(l.ProductId, out var q) ? q : 0
        }).ToList();

        return dto;
    }

    public async Task<InvDoc> SaveDocAsync(InvDoc dto, string? user)
    {
        var type = await _db.InvDocTypes.FindAsync(dto.DocTypeId)
                   ?? throw new InvalidOperationException("نوع سند را انتخاب کنید.");

        if (!type.IsActive)
            throw new InvalidOperationException("این نوع سند غیرفعال است.");

        var warehouse = await _db.Warehouses.FindAsync(dto.WarehouseId)
                        ?? throw new InvalidOperationException("انبار را انتخاب کنید.");

        if (type.IsTransfer)
        {
            if (dto.CounterWarehouseId is null or 0)
                throw new InvalidOperationException("برای سند انتقال، انبار مقصد را انتخاب کنید.");
            if (dto.CounterWarehouseId == dto.WarehouseId)
                throw new InvalidOperationException("انبار مبدأ و مقصد نمی‌توانند یکی باشند.");
        }

        if (type.RequiresParty && dto.PartyId is null or 0)
            throw new InvalidOperationException("انتخاب طرف حساب برای این نوع سند الزامی است.");

        var lines = dto.Lines.Where(l => l.ProductId > 0 && l.Quantity != 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("حداقل یک سطر کالا وارد کنید.");

        if (lines.Any(l => l.Quantity < 0))
            throw new InvalidOperationException("مقدار سطرها نمی‌تواند منفی باشد.");

        if (type.RequiresPrice && lines.Any(l => l.UnitPrice <= 0))
            throw new InvalidOperationException("برای این نوع سند، مبلغ واحد همه سطرها الزامی است.");

        Db.InvDoc entity;
        if (dto.Id == 0)
        {
            entity = new Db.InvDoc
            {
                CreatedAt = DateTime.Now,
                CreatedBy = user,
                Number = await NextDocNumberAsync(type)
            };
            _db.InvDocs.Add(entity);
        }
        else
        {
            entity = await _db.InvDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == dto.Id)
                     ?? throw new InvalidOperationException("سند یافت نشد.");

            if (entity.Status == InvDocStatus.Cancelled)
                throw new InvalidOperationException("سند ابطال‌شده قابل ویرایش نیست.");
        }

        // زوج‌های (کالا، انبار) که قبل از ویرایش درگیر بودند — برای بازسازی کاردکس
        var affected = await CollectAffectedAsync(entity);

        entity.DocTypeId = type.Id;
        entity.WarehouseId = warehouse.Id;
        entity.CounterWarehouseId = type.IsTransfer ? dto.CounterWarehouseId : null;
        entity.PartyId = dto.PartyId is > 0 ? dto.PartyId : null;
        entity.Date = dto.Date == default ? DateTime.Now : dto.Date;
        entity.RefNumber = NullIfEmpty(dto.RefNumber);
        entity.Description = NullIfEmpty(dto.Description);

        // ---------- سطرها ----------
        var current = entity.Lines.ToList();
        foreach (var old in current.Where(c => lines.All(l => l.Id != c.Id)))
            _db.InvDocLines.Remove(old);

        var rowNo = 1;
        foreach (var l in lines)
        {
            var row = l.Id != 0 ? current.FirstOrDefault(c => c.Id == l.Id) : null;
            if (row is null)
            {
                row = new Db.InvDocLine { DocId = entity.Id };
                entity.Lines.Add(row);
            }
            row.RowNo = rowNo++;
            row.ProductId = l.ProductId;
            row.Quantity = l.Quantity;
            row.UnitPrice = l.UnitPrice;
            row.Discount = l.Discount;
            row.IsVatIncluded = l.IsVatIncluded;
            row.VatRate = l.VatRate;
            row.VatAmount = l.VatAmount;
            row.BatchNo = NullIfEmpty(l.BatchNo);
            row.SerialNo = NullIfEmpty(l.SerialNo);
            row.ExpiryDate = l.ExpiryDate;
            row.Description = NullIfEmpty(l.Description);
        }

        entity.TotalQuantity = lines.Sum(l => l.Quantity);
        entity.TotalValue = lines.Sum(l => l.Amount);

        await _db.SaveChangesAsync();

        // اگر سند قطعی ویرایش شد، کاردکس همه‌ی زوج‌های درگیر (قبل و بعد) بازسازی می‌شود
        if (entity.Status == InvDocStatus.Confirmed)
        {
            foreach (var pair in await CollectAffectedAsync(entity)) affected.Add(pair);
            await ValidateStockAsync(entity);
            await RebuildManyAsync(affected);
        }

        return (await GetDocAsync(entity.Id))!;
    }

    public async Task DeleteDocAsync(int id)
    {
        var entity = await _db.InvDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id)
                     ?? throw new InvalidOperationException("سند یافت نشد.");

        if (entity.Status == InvDocStatus.Confirmed)
            throw new InvalidOperationException("سند قطعی قابل حذف نیست؛ ابتدا آن را به پیش‌نویس برگردانید یا ابطال کنید.");

        var affected = await CollectAffectedAsync(entity);

        _db.InvDocLines.RemoveRange(entity.Lines);
        _db.InvDocs.Remove(entity);
        await _db.SaveChangesAsync();

        await RebuildManyAsync(affected);
    }

    public async Task<InvDoc> ConfirmDocAsync(int id, string? user)
    {
        var entity = await _db.InvDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id)
                     ?? throw new InvalidOperationException("سند یافت نشد.");

        if (entity.Status == InvDocStatus.Confirmed)
            throw new InvalidOperationException("این سند قبلاً قطعی شده است.");
        if (entity.Status == InvDocStatus.Cancelled)
            throw new InvalidOperationException("سند ابطال‌شده قابل قطعی‌سازی نیست.");
        if (entity.Lines.Count == 0)
            throw new InvalidOperationException("سند بدون سطر کالا قابل قطعی‌سازی نیست.");

        entity.Status = InvDocStatus.Confirmed;
        entity.ConfirmedBy = user;
        entity.ConfirmedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        try
        {
            await ValidateStockAsync(entity);
            await RebuildManyAsync(await CollectAffectedAsync(entity));
        }
        catch
        {
            entity.Status = InvDocStatus.Draft;
            entity.ConfirmedBy = null;
            entity.ConfirmedAt = null;
            await _db.SaveChangesAsync();
            throw;
        }

        return (await GetDocAsync(entity.Id))!;
    }

    public async Task<InvDoc> UnconfirmDocAsync(int id, string? user)
    {
        var entity = await _db.InvDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id)
                     ?? throw new InvalidOperationException("سند یافت نشد.");

        if (entity.Status != InvDocStatus.Confirmed)
            throw new InvalidOperationException("فقط سند قطعی به پیش‌نویس برمی‌گردد.");

        var affected = await CollectAffectedAsync(entity);

        entity.Status = InvDocStatus.Draft;
        entity.ConfirmedBy = null;
        entity.ConfirmedAt = null;
        await _db.SaveChangesAsync();

        await RebuildManyAsync(affected);
        return (await GetDocAsync(entity.Id))!;
    }

    public async Task<InvDoc> CancelDocAsync(int id, string? user)
    {
        var entity = await _db.InvDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id)
                     ?? throw new InvalidOperationException("سند یافت نشد.");

        if (entity.Status == InvDocStatus.Cancelled)
            throw new InvalidOperationException("این سند قبلاً ابطال شده است.");

        var affected = await CollectAffectedAsync(entity);

        entity.Status = InvDocStatus.Cancelled;
        entity.ConfirmedBy = null;
        entity.ConfirmedAt = null;
        await _db.SaveChangesAsync();

        await RebuildManyAsync(affected);
        return (await GetDocAsync(entity.Id))!;
    }

    private InvDoc MapDoc(Db.InvDoc d, Dictionary<int, Db.InvDocType> types,
        Dictionary<int, string> whNames, Dictionary<int, string> partyNames, int lineCount)
    {
        types.TryGetValue(d.DocTypeId, out var t);
        return new InvDoc
        {
            Id = d.Id,
            Number = d.Number,
            DocTypeId = d.DocTypeId,
            DocTypeName = t?.Name ?? "—",
            DocTypeColor = t?.Color,
            DocTypeIcon = t?.Icon,
            Nature = t?.Nature ?? StockNature.Increase,
            IsTransfer = t?.IsTransfer ?? false,
            WarehouseId = d.WarehouseId,
            WarehouseName = whNames.TryGetValue(d.WarehouseId, out var wn) ? wn : null,
            CounterWarehouseId = d.CounterWarehouseId,
            CounterWarehouseName = d.CounterWarehouseId is > 0 && whNames.TryGetValue(d.CounterWarehouseId.Value, out var cwn) ? cwn : null,
            PartyId = d.PartyId,
            PartyName = d.PartyId is > 0 && partyNames.TryGetValue(d.PartyId.Value, out var pn) ? pn : null,
            Date = d.Date,
            RefNumber = d.RefNumber,
            Description = d.Description,
            Status = d.Status,
            TotalQuantity = d.TotalQuantity,
            TotalValue = d.TotalValue,
            LineCount = lineCount,
            CreatedBy = d.CreatedBy,
            CreatedAt = d.CreatedAt,
            ConfirmedBy = d.ConfirmedBy,
            ConfirmedAt = d.ConfirmedAt
        };
    }

    private async Task<string> NextDocNumberAsync(Db.InvDocType type)
    {
        var prefix = string.IsNullOrWhiteSpace(type.NumberPrefix) ? type.Code : type.NumberPrefix!;
        var year = PersianDate.FromGregorian(DateTime.Now).Year % 100;
        var head = $"{prefix}-{year:00}-";

        var numbers = await _db.InvDocs.Where(d => d.Number.StartsWith(head))
            .Select(d => d.Number).ToListAsync();

        var max = 0;
        foreach (var n in numbers)
        {
            var tail = n[head.Length..];
            if (int.TryParse(tail, out var v) && v > max) max = v;
        }
        return $"{head}{max + 1:0000}";
    }

    // =====================================================================
    //  موتور موجودی و قیمت‌گذاری (کاردکس)
    // =====================================================================

    /// <summary>زوج‌های (کالا، انبار) که این سند رویشان اثر دارد.</summary>
    private async Task<HashSet<(int ProductId, int WarehouseId)>> CollectAffectedAsync(Db.InvDoc doc)
    {
        var set = new HashSet<(int, int)>();
        var lines = doc.Lines.Count > 0
            ? doc.Lines.ToList()
            : await _db.InvDocLines.Where(l => l.DocId == doc.Id).ToListAsync();

        foreach (var l in lines)
        {
            set.Add((l.ProductId, doc.WarehouseId));
            if (doc.CounterWarehouseId is > 0) set.Add((l.ProductId, doc.CounterWarehouseId.Value));
        }
        return set;
    }

    private async Task RebuildManyAsync(IEnumerable<(int ProductId, int WarehouseId)> pairs)
    {
        // انبار مبدأ پیش از مقصد بازسازی می‌شود تا بهای انتقال درست منتقل شود
        foreach (var pair in pairs.Distinct().OrderBy(p => p.ProductId).ThenBy(p => p.WarehouseId))
            await RebuildLedgerAsync(pair.ProductId, pair.WarehouseId);

        // اسناد انتقال: بار دوم برای انتقال بهای تمام‌شده مبدأ به مقصد
        foreach (var pair in pairs.Distinct().OrderBy(p => p.ProductId).ThenBy(p => p.WarehouseId))
            await RebuildLedgerAsync(pair.ProductId, pair.WarehouseId);

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// بازسازی کامل دفتر کاردکس یک کالا در یک انبار بر اساس همه‌ی اسناد قطعی —
    /// با روش قیمت‌گذاری موثر آن کالا (میانگین / FIFO / LIFO).
    /// </summary>
    private async Task RebuildLedgerAsync(int productId, int warehouseId)
    {
        var method = await GetEffectiveMethodAsync(productId, warehouseId);

        var old = await _db.InvLedger.Where(e => e.ProductId == productId && e.WarehouseId == warehouseId).ToListAsync();
        _db.InvLedger.RemoveRange(old);

        var docs = await _db.InvDocs.Include(d => d.Lines)
            .Where(d => d.Status == InvDocStatus.Confirmed
                        && (d.WarehouseId == warehouseId || d.CounterWarehouseId == warehouseId)
                        && d.Lines.Any(l => l.ProductId == productId))
            .OrderBy(d => d.Date).ThenBy(d => d.Id)
            .ToListAsync();

        var typeIds = docs.Select(d => d.DocTypeId).Distinct().ToList();
        var types = await _db.InvDocTypes.Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

        var product = await _db.Products.FindAsync(productId);
        var fallbackCost = product?.PurchasePrice ?? 0;

        // لایه‌های موجودی (برای FIFO/LIFO)
        var layers = new List<(decimal Qty, decimal Cost)>();
        decimal balQty = 0, balValue = 0;
        var seq = 0;

        foreach (var doc in docs)
        {
            if (!types.TryGetValue(doc.DocTypeId, out var type)) continue;

            var isSource = doc.WarehouseId == warehouseId;
            var isDestination = doc.CounterWarehouseId == warehouseId;

            foreach (var line in doc.Lines.Where(l => l.ProductId == productId).OrderBy(l => l.RowNo).ThenBy(l => l.Id))
            {
                var entry = new Db.InvLedgerEntry
                {
                    ProductId = productId,
                    WarehouseId = warehouseId,
                    Date = doc.Date,
                    DocId = doc.Id,
                    DocLineId = line.Id,
                    DocTypeId = doc.DocTypeId,
                    Nature = type.Nature,
                    Number = doc.Number,
                    Description = line.Description ?? doc.Description,
                    Seq = ++seq
                };

                var incoming = (type.Nature == StockNature.Increase && isSource)
                               || (type.IsTransfer && isDestination);
                var outgoing = (type.Nature == StockNature.Decrease && isSource)
                               || (type.IsTransfer && isSource);

                if (incoming)
                {
                    var unitCost = type.IsTransfer && isDestination
                        ? (line.OutCost ?? (line.UnitPrice > 0 ? line.UnitPrice : fallbackCost))
                        : (line.UnitPrice > 0 ? line.UnitPrice : (balQty > 0 ? balValue / balQty : fallbackCost));

                    var value = line.Quantity * unitCost;
                    layers.Add((line.Quantity, unitCost));
                    balQty += line.Quantity;
                    balValue += value;

                    entry.QtyIn = line.Quantity;
                    entry.UnitCost = unitCost;
                    entry.ValueIn = value;
                    entry.RemainingQty = line.Quantity;
                }
                else if (outgoing)
                {
                    var outValue = ConsumeLayers(layers, line.Quantity, method, balQty, balValue, fallbackCost);
                    var unitCost = line.Quantity != 0 ? outValue / line.Quantity : 0;

                    balQty -= line.Quantity;
                    balValue -= outValue;
                    if (balQty == 0) balValue = 0;

                    entry.QtyOut = line.Quantity;
                    entry.UnitCost = unitCost;
                    entry.ValueOut = outValue;

                    // بهای خروج روی سطر ذخیره می‌شود (برای اسناد انتقال و گزارش‌های ریالی)
                    if (isSource)
                    {
                        var dbLine = await _db.InvDocLines.FindAsync(line.Id);
                        if (dbLine is not null) dbLine.OutCost = unitCost;
                    }
                }
                else
                {
                    // ماهیت خنثی بدون انتقال — فقط یادداشت در کاردکس، بدون اثر بر مانده
                    entry.UnitCost = line.UnitPrice;
                }

                entry.BalanceQty = balQty;
                entry.BalanceValue = balValue;
                _db.InvLedger.Add(entry);
            }
        }

        // ---------- مانده انبارداری ----------
        var stock = await _db.InvStocks.FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        if (stock is null && balQty == 0 && balValue == 0)
        {
            await _db.SaveChangesAsync();
            await SyncLegacyStockAsync(productId, warehouseId, 0);
            return;
        }
        if (stock is null)
        {
            stock = new Db.InvStock { ProductId = productId, WarehouseId = warehouseId };
            _db.InvStocks.Add(stock);
        }
        stock.Quantity = balQty;
        stock.Value = balValue;
        stock.AvgCost = balQty != 0 ? balValue / balQty : 0;
        stock.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        await SyncLegacyStockAsync(productId, warehouseId, balQty);
    }

    /// <summary>مصرف لایه‌های موجودی بر اساس روش قیمت‌گذاری و برگرداندن بهای تمام‌شده خروج.</summary>
    private static decimal ConsumeLayers(List<(decimal Qty, decimal Cost)> layers, decimal qty,
        ValuationMethod method, decimal balQty, decimal balValue, decimal fallbackCost)
    {
        if (qty <= 0) return 0;

        if (method == ValuationMethod.Average)
        {
            var avg = balQty > 0 ? balValue / balQty : (layers.Count > 0 ? layers[^1].Cost : fallbackCost);
            if (avg == 0) avg = fallbackCost;

            // لایه‌ها هم به‌روز می‌شوند تا مانده‌ی مقداری درست بماند
            var rem = qty;
            while (rem > 0 && layers.Count > 0)
            {
                var first = layers[0];
                if (first.Qty <= rem) { rem -= first.Qty; layers.RemoveAt(0); }
                else { layers[0] = (first.Qty - rem, first.Cost); rem = 0; }
            }
            return qty * avg;
        }

        decimal value = 0, remaining = qty;
        while (remaining > 0 && layers.Count > 0)
        {
            var index = method == ValuationMethod.Fifo ? 0 : layers.Count - 1;
            var layer = layers[index];

            var take = Math.Min(layer.Qty, remaining);
            value += take * layer.Cost;
            remaining -= take;

            if (layer.Qty - take <= 0) layers.RemoveAt(index);
            else layers[index] = (layer.Qty - take, layer.Cost);
        }

        // خروج بیش از موجودی (اگر مجاز باشد) با آخرین بها یا بهای پیش‌فرض
        if (remaining > 0)
        {
            var cost = balQty > 0 ? balValue / balQty : fallbackCost;
            value += remaining * cost;
        }
        return value;
    }

    /// <summary>روش قیمت‌گذاری موثر: ۱) کالا ← ۲) گروه کالا (و والدها) ← ۳) انبار ← ۴) تنظیمات کلی.</summary>
    private async Task<ValuationMethod> GetEffectiveMethodAsync(int productId, int? warehouseId = null)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
        // اولویت ۱: کالا
        if (product?.Valuation is not null) return product.Valuation.Value;

        // اولویت ۲: گروه کالا
        if (product?.CategoryId is > 0)
        {
            var chain = await CategoryChainAsync(product.CategoryId.Value);
            var cats = await _db.ProductCategories.AsNoTracking()
                .Where(c => chain.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            foreach (var id in chain)
                if (cats.TryGetValue(id, out var c) && c.Valuation is not null)
                    return c.Valuation.Value;
        }

        // اولویت ۳: انبار
        var whId = warehouseId ?? product?.WarehouseId;
        if (whId is > 0)
        {
            var wh = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == whId.Value);
            if (wh?.Valuation is not null)
                return wh.Valuation.Value;
        }

        // اولویت ۴: تنظیمات کلی
        return await GetSettingsMethodAsync();
    }

    private async Task<ValuationMethod> GetSettingsMethodAsync()
    {
        var s = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        return (s?.CostingMethod ?? "Average").ToUpperInvariant() switch
        {
            "FIFO" => ValuationMethod.Fifo,
            "LIFO" => ValuationMethod.Lifo,
            _ => ValuationMethod.Average
        };
    }

    /// <summary>بررسی منفی نشدن موجودی هنگام قطعی‌سازی سند.</summary>
    private async Task ValidateStockAsync(Db.InvDoc doc)
    {
        var type = await _db.InvDocTypes.FindAsync(doc.DocTypeId);
        if (type is null) return;

        var takesOut = type.Nature == StockNature.Decrease || type.IsTransfer;
        if (!takesOut) return;

        var settings = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        var warehouse = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == doc.WarehouseId);
        if ((settings?.AllowNegativeStock ?? false) || (warehouse?.AllowNegative ?? false)) return;

        var lines = doc.Lines.Count > 0 ? doc.Lines.ToList()
            : await _db.InvDocLines.Where(l => l.DocId == doc.Id).ToListAsync();

        foreach (var g in lines.GroupBy(l => l.ProductId))
        {
            var need = g.Sum(l => l.Quantity);

            // موجودی بدون احتساب همین سند.
            // SQLite جمعِ decimal را در SQL پشتیبانی نمی‌کند؛ روی SQLite مقدارها خوانده و در حافظه جمع می‌شوند
            // (همان الگویی که در همین فایل برای بهای تمام‌شده هم به‌کار رفته است).
            decimal balance;
            var ledgerQ = _db.InvLedger
                .Where(e => e.ProductId == g.Key && e.WarehouseId == doc.WarehouseId && e.DocId != doc.Id);
            if (_db.Database.IsSqlite())
            {
                var rows = await ledgerQ.Select(e => new { e.QtyIn, e.QtyOut }).ToListAsync();
                balance = rows.Sum(x => x.QtyIn - x.QtyOut);
            }
            else
            {
                balance = await ledgerQ.SumAsync(e => (decimal?)(e.QtyIn - e.QtyOut)) ?? 0;
            }

            if (need > balance)
            {
                var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == g.Key);
                throw new InvalidOperationException(
                    $"موجودی کالای «{p?.Name}» در انبار «{warehouse?.Name}» کافی نیست. " +
                    $"موجودی: {balance:0.###} — درخواستی: {need:0.###}");
            }
        }
    }

    /// <summary>
    /// همگام‌سازی مانده با جدول قدیمی Stocks تا صفحات قبلی (نقطه سفارش، داشبورد، فروش)
    /// اثر اسناد انبارداری را ببینند: مانده = گردش اسناد خرید/فروش + گردش اسناد انبار.
    /// </summary>
    private async Task SyncLegacyStockAsync(int productId, int warehouseId, decimal invQty)
    {
        var legacyQty = await LegacyQuantityAsync(productId, warehouseId);
        var total = legacyQty + invQty;

        var stock = await _db.Stocks.FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        if (stock is null)
        {
            if (total == 0) return;
            stock = new Db.Stock { ProductId = productId, WarehouseId = warehouseId };
            _db.Stocks.Add(stock);
        }
        stock.Quantity = total;

        var inv = await _db.InvStocks.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        if (inv is not null && inv.AvgCost > 0) stock.AvgCost = inv.AvgCost;

        await _db.SaveChangesAsync();
    }

    /// <summary>مانده حاصل از اسناد قدیمی خرید/فروش/اصلاح.</summary>
    private async Task<decimal> LegacyQuantityAsync(int productId, int warehouseId)
    {
        var txns = await _db.Transactions.Include(t => t.Lines)
            .Where(t => t.WarehouseId == warehouseId && t.Lines.Any(l => l.ProductId == productId))
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        decimal qty = 0;
        foreach (var t in txns)
            foreach (var l in t.Lines.Where(l => l.ProductId == productId))
                qty = t.Type switch
                {
                    TransactionType.Purchase => qty + l.Quantity,
                    TransactionType.Sale => qty - l.Quantity,
                    _ => l.Quantity
                };
        return qty;
    }

    // =====================================================================
    //  کاردکس و موجودی
    // =====================================================================

    public async Task<InvKardexResult> GetKardexAsync(int productId, int? warehouseId, DateTime? from, DateTime? to,
        Paging.Request? pagination = null)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId)
                      ?? throw new InvalidOperationException("کالا یافت نشد.");

        var method = await GetEffectiveMethodAsync(productId, warehouseId);

        var q = _db.InvLedger.AsNoTracking().Where(e => e.ProductId == productId);
        if (warehouseId is > 0) q = q.Where(e => e.WarehouseId == warehouseId);

        // بازهٔ نمایش [from, to) — ترتیب پایدار Date → DocId → Seq (و Id به‌عنوان tie-breaker).
        var window = q;
        if (from.HasValue) { var start = from.Value.Date; window = window.Where(e => e.Date >= start); }
        if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
        {
            var end = to.Value.Date.AddDays(1);
            window = window.Where(e => e.Date < end);
        }
        window = window.OrderBy(e => e.Date).ThenBy(e => e.DocId).ThenBy(e => e.Seq).ThenBy(e => e.Id);

        // فقط ردیف‌های صفحهٔ جاری materialize می‌شوند (COUNT + SKIP/TAKE در SQL).
        var pageRows = await window.ToPageListAsync(pagination);

        // ماندهٔ ماشینی پس از پردازش «دقیقاً» ردیف‌های یک prefix از دفتر:
        // runQty جمع سادهٔ دلتاهاست؛ runValue با قاعدهٔ صفر شدن ماندهٔ ریالی هنگام رسیدن ماندهٔ مقداری به صفر.
        // مقدار ماشینی = جمع دلتای مقدار تا آخرین تقاطع صفر (V(p) − V(z))؛ تقاطع با کوئری همبسته پیدا می‌شود.
        async Task<(decimal Qty, decimal Value)> MachineStateAsync(IQueryable<Db.InvLedgerEntry> before)
        {
            decimal qty, value;
            if (_db.Database.IsSqlite())
            {
                // SQLite جمع decimal ندارد؛ مانند بقیهٔ متدهای پروژه با double و گردکردن ۳ رقمی.
                qty = (decimal)Math.Round(await before.Select(x => x.QtyIn - x.QtyOut).SumAsync(x => (double?)x) ?? 0, 3);
                value = (decimal)Math.Round(await before.Select(x => x.ValueIn - x.ValueOut).SumAsync(x => (double?)x) ?? 0, 3);
            }
            else
            {
                qty = await before.Select(x => x.QtyIn - x.QtyOut).SumAsync(x => (decimal?)x) ?? 0m;
                value = await before.Select(x => x.ValueIn - x.ValueOut).SumAsync(x => (decimal?)x) ?? 0m;
            }

            // آخرین ردیفِ prefix که جمع مقدار تا آن ردیف دقیقاً صفر شده است (همان لحظهٔ صفر شدن ماندهٔ ریالی).
            Db.InvLedgerEntry? zero;
            if (_db.Database.IsSqlite())
            {
                zero = await before.Where(s => Math.Round(q
                        .Where(x => x.Date < s.Date
                            || (x.Date == s.Date && x.DocId < s.DocId)
                            || (x.Date == s.Date && x.DocId == s.DocId && x.Seq < s.Seq)
                            || (x.Date == s.Date && x.DocId == s.DocId && x.Seq == s.Seq && x.Id <= s.Id))
                        .Select(x => x.QtyIn - x.QtyOut).Sum(x => (double?)x) ?? 0, 3) == 0)
                    .OrderByDescending(s => s.Date).ThenByDescending(s => s.DocId)
                    .ThenByDescending(s => s.Seq).ThenByDescending(s => s.Id)
                    .FirstOrDefaultAsync();
            }
            else
            {
                zero = await before.Where(s => (q
                        .Where(x => x.Date < s.Date
                            || (x.Date == s.Date && x.DocId < s.DocId)
                            || (x.Date == s.Date && x.DocId == s.DocId && x.Seq < s.Seq)
                            || (x.Date == s.Date && x.DocId == s.DocId && x.Seq == s.Seq && x.Id <= s.Id))
                        .Select(x => x.QtyIn - x.QtyOut).Sum(x => (decimal?)x) ?? 0m) == 0m)
                    .OrderByDescending(s => s.Date).ThenByDescending(s => s.DocId)
                    .ThenByDescending(s => s.Seq).ThenByDescending(s => s.Id)
                    .FirstOrDefaultAsync();
            }
            if (zero != null)
            {
                // جمع دلتای مقدار تا «خود» ردیف تقاطع؛ ماشین در آن ردیف ماندهٔ ریالی را صفر می‌کند.
                var throughZero = q.Where(x => x.Date < zero.Date
                    || (x.Date == zero.Date && x.DocId < zero.DocId)
                    || (x.Date == zero.Date && x.DocId == zero.DocId && x.Seq < zero.Seq)
                    || (x.Date == zero.Date && x.DocId == zero.DocId && x.Seq == zero.Seq && x.Id <= zero.Id));
                if (_db.Database.IsSqlite())
                    value -= (decimal)Math.Round(await throughZero.Select(x => x.ValueIn - x.ValueOut).SumAsync(x => (double?)x) ?? 0, 3);
                else
                    value -= await throughZero.Select(x => x.ValueIn - x.ValueOut).SumAsync(x => (decimal?)x) ?? 0m;
            }
            return (qty, value);
        }

        var whName = warehouseId is > 0
            ? await _db.Warehouses.AsNoTracking().Where(w => w.Id == warehouseId.Value).Select(w => w.Name).FirstOrDefaultAsync()
            : null;

        var result = new InvKardexResult
        {
            ProductId = product.Id,
            ProductCode = product.Code,
            ProductName = product.Name,
            Unit = product.Unit,
            WarehouseName = warehouseId is > 0 && whName != null ? whName : "همه انبارها",
            Method = method,
            MethodTitle = method switch
            {
                ValuationMethod.Fifo => "اولین صادره از اولین وارده (FIFO)",
                ValuationMethod.Lifo => "اولین صادره از آخرین وارده (LIFO)",
                _ => "میانگین موزون"
            }
        };

        // ماندهٔ ابتدای بازه: وضعیت ماشین پس از آخرین ردیفِ قبل از from — مستقل از صفحه.
        if (from.HasValue)
            (result.OpeningQty, result.OpeningValue) = await MachineStateAsync(
                q.Where(x => x.Date < from.Value.Date));

        // خلاصه‌های مالی بازه با Aggregate سطح دیتابیس — مستقل از صفحه.
        if (_db.Database.IsSqlite())
        {
            result.TotalInQty = (decimal)Math.Round(await window.Select(x => x.QtyIn).SumAsync(x => (double?)x) ?? 0, 3);
            result.TotalInValue = (decimal)Math.Round(await window.Select(x => x.ValueIn).SumAsync(x => (double?)x) ?? 0, 3);
            result.TotalOutQty = (decimal)Math.Round(await window.Select(x => x.QtyOut).SumAsync(x => (double?)x) ?? 0, 3);
            result.TotalOutValue = (decimal)Math.Round(await window.Select(x => x.ValueOut).SumAsync(x => (double?)x) ?? 0, 3);
        }
        else
        {
            result.TotalInQty = await window.Select(x => x.QtyIn).SumAsync(x => (decimal?)x) ?? 0m;
            result.TotalInValue = await window.Select(x => x.ValueIn).SumAsync(x => (decimal?)x) ?? 0m;
            result.TotalOutQty = await window.Select(x => x.QtyOut).SumAsync(x => (decimal?)x) ?? 0m;
            result.TotalOutValue = await window.Select(x => x.ValueOut).SumAsync(x => (decimal?)x) ?? 0m;
        }

        // ماندهٔ پایانی: وضعیت ماشین پس از کل دفتر (آخرین ردیف) — مستقل از صفحه و بازه.
        (result.ClosingQty, result.ClosingValue) = await MachineStateAsync(q);

        if (pageRows.Count > 0)
        {
            // واکشی اطلاعات تکمیلی فقط با شناسه‌های صفحهٔ جاری.
            var docIds = pageRows.Select(e => e.DocId).Distinct().ToList();
            var docs = docIds.Count == 0
                ? new Dictionary<int, int?>()
                : await _db.InvDocs.AsNoTracking().Where(d => docIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.PartyId }).ToDictionaryAsync(d => d.Id, d => (int?)d.PartyId);
            var partyIds = docs.Values.Where(p => p is > 0).Select(p => p!.Value).Distinct().ToList();
            var partyNames = partyIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name);
            var whIds = pageRows.Select(e => e.WarehouseId).Distinct().ToList();
            if (warehouseId is > 0 && !whIds.Contains(warehouseId.Value)) whIds.Add(warehouseId.Value);
            var whNames = whIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Warehouses.AsNoTracking().Where(w => whIds.Contains(w.Id))
                    .ToDictionaryAsync(w => w.Id, w => w.Name);
            var typeIds = pageRows.Select(e => e.DocTypeId).Distinct().ToList();
            var typeNames = typeIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.InvDocTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => t.Name);

            // ماندهٔ ابتدای صفحه: وضعیت ماشین پس از آخرین ردیفِ قبل از اولین ردیف صفحه.
            var first = pageRows[0];
            var (runQty, runValue) = await MachineStateAsync(q.Where(x => x.Date < first.Date
                || (x.Date == first.Date && x.DocId < first.DocId)
                || (x.Date == first.Date && x.DocId == first.DocId && x.Seq < first.Seq)
                || (x.Date == first.Date && x.DocId == first.DocId && x.Seq == first.Seq && x.Id < first.Id)));

            foreach (var e in pageRows)
            {
                runQty += e.QtyIn - e.QtyOut;
                runValue += e.ValueIn - e.ValueOut;
                if (runQty == 0) runValue = 0;

                docs.TryGetValue(e.DocId, out var partyId);

                result.Rows.Add(new InvKardexRow
                {
                    Date = e.Date,
                    DocId = e.DocId,
                    Number = e.Number,
                    DocTypeName = typeNames.TryGetValue(e.DocTypeId, out var tn) ? tn : "—",
                    Nature = e.Nature,
                    WarehouseName = whNames.TryGetValue(e.WarehouseId, out var w) ? w : null,
                    PartyName = partyId is > 0 && partyNames.TryGetValue(partyId.Value, out var pn) ? pn : null,
                    Description = e.Description,
                    InQty = e.QtyIn,
                    InPrice = e.QtyIn > 0 ? e.UnitCost : 0,
                    InValue = e.ValueIn,
                    OutQty = e.QtyOut,
                    OutPrice = e.QtyOut > 0 ? e.UnitCost : 0,
                    OutValue = e.ValueOut,
                    BalanceQty = runQty,
                    BalanceValue = runValue
                });
            }
        }

        return result;
    }

    public async Task<PagedResult<InvStockRow>> GetStockAsync(int? warehouseId, int? categoryId, string? search,
        bool belowOnly, int page, int pageSize)
    {
        var stocks = _db.InvStocks.AsNoTracking().AsQueryable();
        if (warehouseId is > 0) stocks = stocks.Where(s => s.WarehouseId == warehouseId);
        var products = _db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            products = products.Where(p => p.Name.Contains(term) || p.Code.Contains(term)
                || (p.Barcode != null && p.Barcode.Contains(term)));
        }
        if (categoryId is > 0)
        {
            var ids = await DescendantCategoryIdsAsync(categoryId.Value);
            products = products.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
        }
        var query = from stock in stocks
                    join product in products on stock.ProductId equals product.Id
                    join warehouse in _db.Warehouses on stock.WarehouseId equals warehouse.Id into warehouses
                    from warehouse in warehouses.DefaultIfEmpty()
                    select new InvStockRow
                    {
                        ProductId = product.Id, ProductCode = product.Code, ProductName = product.Name,
                        Unit = product.Unit, CategoryName = product.Category,
                        WarehouseId = stock.WarehouseId, WarehouseName = warehouse == null ? null : warehouse.Name,
                        Quantity = stock.Quantity, AvgCost = stock.AvgCost, Value = stock.Value,
                        ReorderPoint = product.ReorderPoint
                    };
        if (belowOnly) query = query.Where(r => r.ReorderPoint > 0 && r.Quantity <= r.ReorderPoint);
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var total = await query.CountAsync();
        var rows = await query.OrderBy(r => r.ProductName).ThenBy(r => r.ProductId).ThenBy(r => r.WarehouseId)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        return new PagedResult<InvStockRow> { Items = rows, TotalCount = total, Page = page, PageSize = pageSize };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
