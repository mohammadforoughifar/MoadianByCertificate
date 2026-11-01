using Inventory.Api.Services;
using Inventory.Api.Services.Accounting;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// پیاده‌سازی ماژول فاکتور.
///
/// زنجیره‌ی کامل با قطعی شدن یک فاکتور:
///   ۱) سند انبار (رسید یا حواله) ساخته و قطعی می‌شود  → موجودی و کاردکس به‌روز می‌شود
///   ۲) سند حسابداری سند انبار صادر می‌شود              → موجودی کالا / بهای تمام‌شده
///   ۳) سند حسابداری خود فاکتور صادر می‌شود             → طرف حساب / فروش یا خرید / مالیات
/// </summary>
public class InvoicingService : IInvoicingService
{
    private readonly Db.AppDbContext _db;
    private readonly IWarehousingService _wh;
    private readonly IAccountingService _acc;
    private readonly ILogger<InvoicingService> _log;

    public InvoicingService(Db.AppDbContext db, IWarehousingService wh, IAccountingService acc,
        ILogger<InvoicingService> log)
    {
        _db = db;
        _wh = wh;
        _acc = acc;
        _log = log;
    }

    // =====================================================================
    // ۱) خواندن فاکتورها
    // =====================================================================

    public async Task<PagedResult<FacInvoice>> GetInvoicesAsync(InvoiceKind? kind, InvoiceStatus? status,
        int? partyId, int? warehouseId, string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.FacInvoices.AsNoTracking().AsQueryable();

        if (kind is not null) q = q.Where(i => i.Kind == kind);
        if (status is not null) q = q.Where(i => i.Status == status);
        if (partyId is > 0) q = q.Where(i => i.PartyId == partyId);
        if (warehouseId is > 0) q = q.Where(i => i.WarehouseId == warehouseId);
        if (from is not null) q = q.Where(i => i.Date >= from.Value.Date);
        if (to is not null) q = q.Where(i => i.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i => (i.RefNumber ?? "").Contains(s)
                             || (i.Description ?? "").Contains(s)
                             || (i.Party != null && i.Party.Name.Contains(s))
                             || i.Number.ToString().Contains(s));
        }

        var total = await q.CountAsync();

        // توجه: جمع ستون decimal درون SQL توسط SQLite پشتیبانی نمی‌شود
        // (SQLite cannot apply aggregate operator 'Sum' on expressions of type 'decimal').
        // پس فقط شمارش سطرها از دیتابیس گرفته می‌شود و مجموع مقدار کالاها در حافظه محاسبه می‌شود.
        var rows = await q
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                Inv = i,
                PartyName = i.Party != null ? i.Party.Name : null,
                WarehouseName = i.Warehouse != null ? i.Warehouse.Name : null,
                LineCount = i.Lines.Count
            })
            .ToListAsync();

        var pageIds = rows.Select(r => r.Inv.Id).ToList();
        var qtyRows = await _db.FacInvoiceLines.AsNoTracking()
            .Where(l => pageIds.Contains(l.InvoiceId))
            .Select(l => new { l.InvoiceId, l.Quantity })
            .ToListAsync();
        var qtyMap = qtyRows.GroupBy(x => x.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var docIds = rows.Where(r => r.Inv.InvDocId is not null).Select(r => r.Inv.InvDocId!.Value).ToList();
        var docNumbers = await _db.InvDocs.AsNoTracking().Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Number);

        var vIds = rows.Where(r => r.Inv.VoucherId is not null).Select(r => r.Inv.VoucherId!.Value).ToList();
        var vNumbers = await _db.AccVouchers.AsNoTracking().Where(v => vIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Number);

        var items = rows.Select(r => Map(r.Inv, r.PartyName, r.WarehouseName, r.LineCount,
            qtyMap.TryGetValue(r.Inv.Id, out var qty) ? qty : 0m,
            r.Inv.InvDocId is not null && docNumbers.ContainsKey(r.Inv.InvDocId.Value) ? docNumbers[r.Inv.InvDocId.Value] : null,
            r.Inv.VoucherId is not null && vNumbers.ContainsKey(r.Inv.VoucherId.Value) ? vNumbers[r.Inv.VoucherId.Value] : null))
            .ToList();
        await FillSettlementBalancesAsync(items);

        return new PagedResult<FacInvoice> { Items = items, TotalCount = total };
    }

    public async Task<FacInvoice?> GetInvoiceAsync(int id)
    {
        var e = await _db.FacInvoices.AsNoTracking()
            .Include(i => i.Party)
            .Include(i => i.Warehouse)
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (e is null) return null;

        string? docNumber = e.InvDocId is null ? null
            : await _db.InvDocs.AsNoTracking().Where(d => d.Id == e.InvDocId).Select(d => d.Number).FirstOrDefaultAsync();
        int? voucherNumber = e.VoucherId is null ? null
            : await _db.AccVouchers.AsNoTracking().Where(v => v.Id == e.VoucherId).Select(v => (int?)v.Number).FirstOrDefaultAsync();

        var dto = Map(e, e.Party?.Name, e.Warehouse?.Name, e.Lines.Count, e.Lines.Sum(l => l.Quantity), docNumber, voucherNumber);
        await FillSettlementBalancesAsync(new List<FacInvoice> { dto });

        // موجودی فعلی هر کالا در انبار فاکتور
        var productIds = e.Lines.Select(l => l.ProductId).Distinct().ToList();
        var stocks = await _db.InvStocks.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && s.WarehouseId == e.WarehouseId)
            .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

        dto.Lines = e.Lines.OrderBy(l => l.RowNo).Select(l => new FacInvoiceLine
        {
            Id = l.Id,
            RowNo = l.RowNo,
            ProductId = l.ProductId,
            ProductCode = l.Product?.Code ?? "",
            ProductName = l.Product?.Name ?? "",
            Unit = l.Product?.Unit ?? "",
            TaxCode = l.TaxCode,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            DiscountPercent = l.DiscountPercent,
            Discount = l.Discount,
            VatRate = l.VatRate,
            VatAmount = l.VatAmount,
            Description = l.Description,
            CurrentStock = stocks.TryGetValue(l.ProductId, out var q) ? q : 0
        }).ToList();

        return dto;
    }

    /// <summary>ماندهٔ فاکتور از روی تخصیص‌های قطعی، پس از کسر چک‌های برگشتی/باطل‌شده محاسبه می‌شود.</summary>
    private async Task FillSettlementBalancesAsync(List<FacInvoice> invoices)
    {
        if (invoices.Count == 0) return;

        var creditIds = invoices
            .Where(i => i.Status == InvoiceStatus.Confirmed && i.Settlement == SettlementType.Credit)
            .Select(i => i.Id).Where(id => id > 0).Distinct().ToList();
        var settled = new Dictionary<int, decimal>();

        if (creditIds.Count > 0)
        {
            var allocationVoucherIds = new List<int>();
            foreach (var idChunk in creditIds.Chunk(1000))
            {
                var found = await _db.TrsInvoiceAllocations.AsNoTracking()
                    .Where(a => idChunk.Contains(a.InvoiceId)
                                && a.TrsVoucher != null
                                && a.TrsVoucher.Status == TreasuryStatus.Confirmed)
                    .Select(a => a.TrsVoucherId).Distinct().ToListAsync();
                allocationVoucherIds.AddRange(found);
            }
            allocationVoucherIds = allocationVoucherIds.Distinct().ToList();

            var sources = new List<InvoiceSettlementSource>();
            // A bounced cheque can affect a voucher split across several invoices; load all rows
            // for those vouchers before calculating each invoice's proportional reversal.
            foreach (var voucherChunk in allocationVoucherIds.Chunk(1000))
            {
                var allocationRows = await _db.TrsInvoiceAllocations.AsNoTracking()
                    .Where(a => voucherChunk.Contains(a.TrsVoucherId))
                    .Select(a => new
                    {
                        a.TrsVoucherId,
                        a.InvoiceId,
                        a.Amount,
                        VoucherTotal = a.TrsVoucher!.TotalAmount
                    })
                    .ToListAsync();
                sources.AddRange(allocationRows.Select(a => new InvoiceSettlementSource
                {
                    VoucherId = a.TrsVoucherId,
                    InvoiceId = a.InvoiceId,
                    Amount = a.Amount,
                    VoucherTotal = a.VoucherTotal
                }));
            }

            // سازگاری با داده‌های قدیمی که هنوز پیوندشان فقط در TrsVouchers.InvoiceId است.
            foreach (var idChunk in creditIds.Chunk(1000))
            {
                var legacyRows = await _db.TrsVouchers.AsNoTracking()
                    .Where(v => v.InvoiceId.HasValue
                                && idChunk.Contains(v.InvoiceId.Value)
                                && v.Status == TreasuryStatus.Confirmed
                                && !v.InvoiceAllocations.Any())
                    .Select(v => new { VoucherId = v.Id, InvoiceId = v.InvoiceId!.Value, v.TotalAmount })
                    .ToListAsync();
                sources.AddRange(legacyRows.Select(v => new InvoiceSettlementSource
                {
                    VoucherId = v.VoucherId,
                    InvoiceId = v.InvoiceId,
                    Amount = v.TotalAmount,
                    VoucherTotal = v.TotalAmount
                }));
            }

            if (sources.Count > 0)
            {
                var voucherIds = sources.Select(s => s.VoucherId).Distinct().ToList();
                var reversed = new Dictionary<int, decimal>();
                foreach (var voucherChunk in voucherIds.Chunk(1000))
                {
                    var reversedRows = await _db.TrsCheques.AsNoTracking()
                        .Where(c => c.TrsVoucherId.HasValue
                                    && voucherChunk.Contains(c.TrsVoucherId.Value)
                                    && (c.Status == ChequeStatus.Bounced || c.Status == ChequeStatus.Cancelled))
                        .GroupBy(c => c.TrsVoucherId!.Value)
                        .Select(g => new { VoucherId = g.Key, Amount = g.Sum(c => c.Amount) })
                        .ToListAsync();
                    foreach (var row in reversedRows) reversed[row.VoucherId] = row.Amount;
                }
                settled = InvoiceSettlementMath.Calculate(sources, reversed);
            }
        }

        foreach (var invoice in invoices)
        {
            if (invoice.Status != InvoiceStatus.Confirmed)
            {
                invoice.SettledAmount = 0;
                invoice.RemainingAmount = Math.Max(0, invoice.TotalNet);
            }
            else if (invoice.Settlement == SettlementType.Cash)
            {
                invoice.SettledAmount = invoice.TotalNet;
                invoice.RemainingAmount = 0;
            }
            else
            {
                invoice.SettledAmount = Math.Min(invoice.TotalNet, settled.GetValueOrDefault(invoice.Id));
                invoice.RemainingAmount = Math.Max(0, invoice.TotalNet - invoice.SettledAmount);
            }
        }
    }

    public async Task<FacInvoice> NewInvoiceAsync(InvoiceKind kind)
    {
        var warehouse = await _db.Warehouses.AsNoTracking()
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Id)
            .FirstOrDefaultAsync();

        return new FacInvoice
        {
            Kind = kind,
            Number = await NextNumberAsync(kind),
            Date = DateTime.Now.Date,
            WarehouseId = warehouse?.Id ?? 0,
            WarehouseName = warehouse?.Name,
            Settlement = SettlementType.Credit,
            Status = InvoiceStatus.Draft
        };
    }

    private async Task<int> NextNumberAsync(InvoiceKind kind)
    {
        var max = await _db.FacInvoices.Where(i => i.Kind == kind).Select(i => (int?)i.Number).MaxAsync();
        return (max ?? 0) + 1;
    }

    public async Task<FacInvoiceLine> BuildLineAsync(int productId, InvoiceKind kind, int warehouseId)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId)
                ?? throw new InvalidOperationException("کالا یافت نشد.");

        var buying = kind is InvoiceKind.Purchase or InvoiceKind.PurchaseReturn;
        var stock = await _db.InvStocks.AsNoTracking()
            .Where(s => s.ProductId == productId && s.WarehouseId == warehouseId)
            .Select(s => (decimal?)s.Quantity).FirstOrDefaultAsync() ?? 0;

        return new FacInvoiceLine
        {
            ProductId = p.Id,
            ProductCode = p.Code,
            ProductName = p.Name,
            Unit = p.Unit,
            TaxCode = p.TaxCode,
            Quantity = 1,
            UnitPrice = buying ? p.PurchasePrice : p.SalePrice,
            VatRate = p.VatRate,
            CurrentStock = stock
        };
    }

    // =====================================================================
    // ۲) ذخیره‌ی فاکتور (محاسبه‌ی مبالغ سمت سرور)
    // =====================================================================

    public async Task<FacInvoice> SaveInvoiceAsync(FacInvoice dto, string? user)
    {
        var warehouse = await _db.Warehouses.FindAsync(dto.WarehouseId)
                        ?? throw new InvalidOperationException("انبار فاکتور را انتخاب کنید.");

        var lines = dto.Lines.Where(l => l.ProductId > 0 && l.Quantity != 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("حداقل یک قلم کالا وارد کنید.");
        if (lines.Any(l => l.Quantity < 0))
            throw new InvalidOperationException("مقدار اقلام نمی‌تواند منفی باشد.");
        if (lines.Any(l => l.UnitPrice < 0))
            throw new InvalidOperationException("مبلغ واحد نمی‌تواند منفی باشد.");
        if (dto.Settlement == SettlementType.Credit && dto.PartyId is null or 0)
            throw new InvalidOperationException("برای فاکتور نسیه، انتخاب طرف حساب الزامی است.");

        Db.FacInvoice entity;
        if (dto.Id == 0)
        {
            entity = new Db.FacInvoice
            {
                Kind = dto.Kind,
                Number = dto.Number > 0 ? dto.Number : await NextNumberAsync(dto.Kind),
                CreatedAt = DateTime.Now,
                CreatedBy = user
            };
            _db.FacInvoices.Add(entity);
        }
        else
        {
            entity = await _db.FacInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == dto.Id)
                     ?? throw new InvalidOperationException("فاکتور یافت نشد.");

            if (entity.Status != InvoiceStatus.Draft)
                throw new InvalidOperationException("فقط فاکتور پیش‌نویس قابل ویرایش است.");
        }

        var originalLineQuantities = entity.Lines.ToDictionary(l => l.Id, l => (l.ProductId, l.Quantity));
        if (lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2)
                            && (!originalLineQuantities.TryGetValue(l.Id, out var original)
                                || original.ProductId != l.ProductId || original.Quantity != l.Quantity)))
            throw new InvalidOperationException("مقدار هر قلم حداکثر تا دو رقم اعشار قابل ثبت است؛ مقدار قدیمیِ بدون تغییر قابل حفظ است.");

        // شماره تکراری در همان نوع فاکتور
        if (await _db.FacInvoices.AnyAsync(i => i.Kind == entity.Kind && i.Number == entity.Number && i.Id != entity.Id))
            entity.Number = await NextNumberAsync(entity.Kind);

        entity.RefNumber = dto.RefNumber;
        entity.Date = dto.Date == default ? DateTime.Now.Date : dto.Date.Date;
        entity.DueDate = dto.DueDate?.Date;
        entity.PartyId = dto.PartyId is > 0 ? dto.PartyId : null;
        entity.WarehouseId = warehouse.Id;
        entity.Settlement = dto.Settlement;
        entity.Description = dto.Description;
        entity.InvoiceDiscount = Math.Max(0, dto.InvoiceDiscount);
        entity.ShippingCost = Math.Max(0, dto.ShippingCost);

        // ---------- بازسازی اقلام ----------
        if (entity.Lines.Count > 0) _db.FacInvoiceLines.RemoveRange(entity.Lines);
        entity.Lines.Clear();

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p);

        var rowNo = 1;
        foreach (var l in lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p))
                throw new InvalidOperationException("یکی از کالاهای فاکتور یافت نشد.");

            var gross = l.Quantity * l.UnitPrice;

            // اگر درصد تخفیف پر شده باشد، مبلغ تخفیف از روی آن محاسبه می‌شود
            var discount = l.DiscountPercent > 0 ? Math.Round(gross * l.DiscountPercent / 100m, 0) : l.Discount;
            discount = Math.Clamp(discount, 0, gross);

            var taxable = gross - discount;
            var vatRate = l.VatRate < 0 ? 0 : l.VatRate;
            var vat = Math.Round(taxable * vatRate / 100m, 0);

            entity.Lines.Add(new Db.FacInvoiceLine
            {
                RowNo = rowNo++,
                ProductId = p.Id,
                TaxCode = string.IsNullOrWhiteSpace(l.TaxCode) ? p.TaxCode : l.TaxCode,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent,
                Discount = discount,
                VatRate = vatRate,
                VatAmount = vat,
                Taxable = taxable,
                Total = taxable + vat,
                Description = l.Description
            });
        }

        Recalculate(entity);

        await _db.SaveChangesAsync();
        return (await GetInvoiceAsync(entity.Id))!;
    }

    /// <summary>محاسبه‌ی جمع‌های فاکتور از روی اقلام (تخفیف کلی به‌نسبت روی اقلام سرشکن می‌شود).</summary>
    private static void Recalculate(Db.FacInvoice e)
    {
        e.TotalGross = e.Lines.Sum(l => l.Quantity * l.UnitPrice);
        e.TotalLineDiscount = e.Lines.Sum(l => l.Discount);

        var afterLineDiscount = e.TotalGross - e.TotalLineDiscount;
        e.InvoiceDiscount = Math.Clamp(e.InvoiceDiscount, 0, afterLineDiscount);

        e.TotalTaxable = afterLineDiscount - e.InvoiceDiscount;

        // تخفیف کلی، مأخذ مالیات هر سطر را هم کاهش می‌دهد
        if (e.InvoiceDiscount > 0 && afterLineDiscount > 0)
        {
            var ratio = e.TotalTaxable / afterLineDiscount;
            foreach (var l in e.Lines)
            {
                var taxable = Math.Round((l.Quantity * l.UnitPrice - l.Discount) * ratio, 0);
                l.Taxable = taxable;
                l.VatAmount = Math.Round(taxable * l.VatRate / 100m, 0);
                l.Total = taxable + l.VatAmount;
            }
        }

        e.TotalVat = e.Lines.Sum(l => l.VatAmount);
        e.TotalNet = e.TotalTaxable + e.TotalVat + e.ShippingCost;
    }

    // =====================================================================
    // ۳) قطعی‌سازی و برگشت
    // =====================================================================

    public async Task<FacInvoice> ConfirmInvoiceAsync(int id, string? user)
    {
        var e = await _db.FacInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id)
                ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        if (e.Status == InvoiceStatus.Confirmed) throw new InvalidOperationException("این فاکتور قبلاً قطعی شده است.");
        if (e.Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("فاکتور ابطال‌شده قابل قطعی‌سازی نیست.");
        if (e.Lines.Count == 0) throw new InvalidOperationException("فاکتور بدون قلم کالا قابل قطعی‌سازی نیست.");
        if (e.TotalNet <= 0) throw new InvalidOperationException("مبلغ فاکتور باید بزرگ‌تر از صفر باشد.");

        var rule = await _db.FacRules.AsNoTracking().FirstOrDefaultAsync(r => r.Kind == e.Kind);

        // ---------- ۱) سند انبار ----------
        if (rule is { IsActive: true, AutoInvDoc: true, DocTypeId: > 0 })
        {
            var type = await _db.InvDocTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == rule.DocTypeId)
                       ?? throw new InvalidOperationException("نوع سند انبارِ تعریف‌شده در قاعده یافت نشد.");

            var incoming = e.Kind is InvoiceKind.Purchase or InvoiceKind.SaleReturn;
            var expected = incoming ? StockNature.Increase : StockNature.Decrease;
            if (type.Nature != expected)
                throw new InvalidOperationException(
                    $"ماهیت «{type.Name}» با نوع فاکتور همخوانی ندارد؛ برای «{KindTitle(e.Kind)}» باید {(incoming ? "رسید (افزایشی)" : "حواله (کاهشی)")} باشد.");

            var doc = new InvDoc
            {
                DocTypeId = type.Id,
                WarehouseId = e.WarehouseId,
                PartyId = e.PartyId,
                Date = e.Date,
                RefNumber = $"{KindTitle(e.Kind)} {e.Number}",
                Description = $"صادرشده از {KindTitle(e.Kind)} شماره {e.Number}",
                Lines = e.Lines.OrderBy(l => l.RowNo).Select(l => new InvDocLine
                {
                    RowNo = l.RowNo,
                    ProductId = l.ProductId,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    Discount = l.Discount,
                    Description = l.Description
                }).ToList()
            };

            var saved = await _wh.SaveDocAsync(doc, user);
            await _wh.ConfirmDocAsync(saved.Id, user);
            e.InvDocId = saved.Id;

            // ---------- ۲) سند حسابداری سند انبار (موجودی کالا / بهای تمام‌شده) ----------
            await _acc.PostInventoryDocAsync(saved.Id, user);
        }

        // ---------- ۳) سند حسابداری فاکتور ----------
        if (rule is { IsActive: true, AutoVoucher: true })
            e.VoucherId = await PostInvoiceVoucherAsync(e, rule, user);

        e.Status = InvoiceStatus.Confirmed;
        e.ConfirmedBy = user;
        e.ConfirmedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return (await GetInvoiceAsync(e.Id))!;
    }

    public async Task<FacInvoice> UnconfirmInvoiceAsync(int id, string? user)
    {
        var e = await _db.FacInvoices.FirstOrDefaultAsync(i => i.Id == id)
                ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        if (e.Status != InvoiceStatus.Confirmed)
            throw new InvalidOperationException("فقط فاکتور قطعی به پیش‌نویس برمی‌گردد.");

        await EnsureNoActiveTreasurySettlementsAsync(e.Id);
        await ReverseDocumentsAsync(e, user);

        e.Status = InvoiceStatus.Draft;
        e.ConfirmedBy = null;
        e.ConfirmedAt = null;

        await _db.SaveChangesAsync();
        return (await GetInvoiceAsync(e.Id))!;
    }

    public async Task<FacInvoice> CancelInvoiceAsync(int id, string? user)
    {
        var e = await _db.FacInvoices.FirstOrDefaultAsync(i => i.Id == id)
                ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        if (e.Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("این فاکتور قبلاً ابطال شده است.");

        await EnsureNoActiveTreasurySettlementsAsync(e.Id);
        await ReverseDocumentsAsync(e, user);

        e.Status = InvoiceStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (await GetInvoiceAsync(e.Id))!;
    }

    public async Task DeleteInvoiceAsync(int id)
    {
        var e = await _db.FacInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id)
                ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        if (e.Status == InvoiceStatus.Confirmed)
            throw new InvalidOperationException("ابتدا فاکتور را از حالت قطعی خارج کنید.");
        if (await _db.TrsInvoiceAllocations.AnyAsync(a => a.InvoiceId == e.Id)
            || await _db.TrsVouchers.AnyAsync(v => v.InvoiceId == e.Id))
            throw new InvalidOperationException("این فاکتور در سوابق دریافت/پرداخت خزانه استفاده شده است؛ برای حفظ سابقه قابل حذف نیست.");

        _db.FacInvoiceLines.RemoveRange(e.Lines);
        _db.FacInvoices.Remove(e);
        await _db.SaveChangesAsync();
    }


    /// <summary>فاکتور دارای سند خزانهٔ فعال نباید از دفاتر حذف یا از قطعیت خارج شود.</summary>
    private async Task EnsureNoActiveTreasurySettlementsAsync(int invoiceId)
    {
        var hasAllocations = await _db.TrsInvoiceAllocations
            .AnyAsync(a => a.InvoiceId == invoiceId && a.TrsVoucher!.Status != TreasuryStatus.Cancelled);
        var hasLegacySettlement = await _db.TrsVouchers
            .AnyAsync(v => v.InvoiceId == invoiceId
                           && v.Status != TreasuryStatus.Cancelled
                           && !v.InvoiceAllocations.Any());
        if (hasAllocations || hasLegacySettlement)
            throw new InvalidOperationException(
                "برای این فاکتور سند دریافت/پرداخت خزانه وجود دارد؛ ابتدا سندهای مرتبط را ابطال یا حذف کنید، سپس فاکتور را برگردانید.");
    }

    /// <summary>حذف سند حسابداری فاکتور و سند انبارِ صادرشده (به‌همراه سند حسابداری آن).</summary>
    private async Task ReverseDocumentsAsync(Db.FacInvoice e, string? user)
    {
        // سند حسابداری فاکتور
        var vouchers = await _db.AccVouchers.Include(v => v.Lines)
            .Where(v => v.Source == VoucherSource.Invoice && v.SourceId == e.Id)
            .ToListAsync();
        foreach (var v in vouchers) _db.AccVoucherLines.RemoveRange(v.Lines);
        _db.AccVouchers.RemoveRange(vouchers);
        e.VoucherId = null;

        // سند انبار + سند حسابداری آن
        if (e.InvDocId is > 0)
        {
            var docId = e.InvDocId.Value;
            await _acc.UnpostInventoryDocAsync(docId);

            var doc = await _db.InvDocs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId);
            if (doc is not null)
            {
                if (doc.Status == InvDocStatus.Confirmed) await _wh.UnconfirmDocAsync(docId, user);
                await _wh.DeleteDocAsync(docId);
            }
            e.InvDocId = null;
        }

        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // ۴) سند حسابداری فاکتور
    // =====================================================================

    /// <summary>
    /// فروش:            دریافتنی/صندوق بدهکار | فروش بستانکار + مالیات بستانکار
    /// خرید:            خرید بدهکار + مالیات بدهکار | پرداختنی/صندوق بستانکار
    /// برگشت از فروش:   برگشت از فروش بدهکار + مالیات بدهکار | دریافتنی/صندوق بستانکار
    /// برگشت از خرید:   پرداختنی/صندوق بدهکار | برگشت از خرید بستانکار + مالیات بستانکار
    /// </summary>
    private async Task<int?> PostInvoiceVoucherAsync(Db.FacInvoice e, Db.FacRule rule, string? user)
    {
        var partyAccountId = e.Settlement == SettlementType.Cash ? rule.CashAccountId : rule.PartyAccountId;
        if (partyAccountId is null || rule.MainAccountId is null)
            throw new InvalidOperationException("حساب‌های این نوع فاکتور کامل نیست؛ صفحه «تنظیمات فاکتور» را تکمیل کنید.");
        if (e.TotalVat > 0 && rule.VatAccountId is null)
            throw new InvalidOperationException("حساب مالیات بر ارزش افزوده در تنظیمات فاکتور تعیین نشده است.");
        if (e.ShippingCost > 0 && rule.ShippingAccountId is null)
            throw new InvalidOperationException("حساب هزینه حمل در تنظیمات فاکتور تعیین نشده است.");

        // در فروش و برگشت از خرید، طرف حساب بدهکار می‌شود
        var partyDebit = e.Kind is InvoiceKind.Sale or InvoiceKind.PurchaseReturn;

        var year = await _db.AccFiscalYears.FirstOrDefaultAsync(f => f.IsCurrent)
                   ?? await _db.AccFiscalYears.OrderByDescending(f => f.StartDate).FirstOrDefaultAsync()
                   ?? throw new InvalidOperationException("هیچ سال مالی تعریف نشده است.");

        var maxNumber = await _db.AccVouchers.Where(v => v.FiscalYearId == year.Id)
            .Select(v => (int?)v.Number).MaxAsync() ?? 0;

        var title = $"{KindTitle(e.Kind)} شماره {e.Number}";

        var voucher = new Db.AccVoucher
        {
            FiscalYearId = year.Id,
            Number = maxNumber + 1,
            Date = e.Date.Date,
            Description = $"سند خودکار — {title}",
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.Invoice,
            SourceId = e.Id,
            SourceTitle = title,
            RefNumber = e.RefNumber ?? e.Number.ToString(),
            CreatedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedBy = user,
            ConfirmedAt = DateTime.Now
        };

        var row = 1;

        // طرف حساب (یا صندوق در فاکتور نقدی) — به مبلغ قابل پرداخت
        voucher.Lines.Add(new Db.AccVoucherLine
        {
            RowNo = row++,
            AccountId = partyAccountId.Value,
            PartyId = e.PartyId,
            Description = title,
            RefNumber = e.RefNumber,
            Debit = partyDebit ? e.TotalNet : 0,
            Credit = partyDebit ? 0 : e.TotalNet
        });

        // حساب اصلی: فروش / خرید / برگشتی — به مأخذ مالیات
        if (e.TotalTaxable != 0)
        {
            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++,
                AccountId = rule.MainAccountId.Value,
                PartyId = e.PartyId,
                Description = title,
                RefNumber = e.RefNumber,
                Debit = partyDebit ? 0 : e.TotalTaxable,
                Credit = partyDebit ? e.TotalTaxable : 0
            });
        }

        // مالیات بر ارزش افزوده
        if (e.TotalVat != 0)
        {
            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++,
                AccountId = rule.VatAccountId!.Value,
                PartyId = e.PartyId,
                Description = $"مالیات و عوارض — {title}",
                RefNumber = e.RefNumber,
                Debit = partyDebit ? 0 : e.TotalVat,
                Credit = partyDebit ? e.TotalVat : 0
            });
        }

        // هزینه حمل
        if (e.ShippingCost != 0)
        {
            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++,
                AccountId = rule.ShippingAccountId!.Value,
                PartyId = e.PartyId,
                Description = $"هزینه حمل — {title}",
                RefNumber = e.RefNumber,
                Debit = partyDebit ? 0 : e.ShippingCost,
                Credit = partyDebit ? e.ShippingCost : 0
            });
        }

        voucher.TotalDebit = voucher.Lines.Sum(l => l.Debit);
        voucher.TotalCredit = voucher.Lines.Sum(l => l.Credit);

        if (voucher.TotalDebit != voucher.TotalCredit)
            throw new InvalidOperationException("سند حسابداری فاکتور تراز نشد؛ مبالغ فاکتور را بررسی کنید.");

        _db.AccVouchers.Add(voucher);
        await _db.SaveChangesAsync();
        return voucher.Id;
    }

    // =====================================================================
    // ۴-ب) سند خودکار «فروش فروشگاه» (سفارش ویزیتور پس از تأیید مالی)
    // =====================================================================

    /// <summary>
    /// سند حسابداری سند فروش فروشگاه:
    /// <code>
    /// نقدی:      صندوق/بانک (یا پیش‌دریافت نقدی)      بدهکار | فروش کالا بستانکار
    /// چک/اقساط: حساب دریافتنی طرف حساب                بدهکار | فروش کالا بستانکار
    /// </code>
    /// چک‌ها و اقساط در خود سند فروش ثبت می‌شوند (همان رفتار ماژول فروش).
    /// </summary>
    public async Task<int?> PostStoreSaleVoucherAsync(int saleId, string? user)
    {
        var existing = await _db.AccVouchers.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Source == VoucherSource.StoreSale && v.SourceId == saleId);
        if (existing is not null) return existing.Id;

        var sale = await _db.Transactions.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("سند فروش یافت نشد.");
        if (sale.Type != TransactionType.Sale)
            throw new InvalidOperationException("سند حسابداری خودکار فقط برای فروش صادر می‌شود.");
        if (sale.Amount <= 0) return null;

        var rule = await _db.FacRules.AsNoTracking().FirstOrDefaultAsync(r => r.Kind == InvoiceKind.Sale);
        if (rule is not { IsActive: true, AutoVoucher: true }) return null;

        var mainAccount = rule.MainAccountId
            ?? throw new InvalidOperationException("حساب «فروش کالا» در قاعدهٔ فاکتور فروش تعیین نشده است.");
        var receivableAccount = rule.PartyAccountId
            ?? throw new InvalidOperationException("حساب دریافتنی (طرف حساب) در قاعدهٔ فاکتور فروش تعیین نشده است.");
        var cashAccount = rule.CashAccountId ?? receivableAccount;

        // بخش نقدی: فاکتور نقدی تمام مبلغ، در پرداخت ترکیبی فقط پیش‌دریافت
        var cashPart = sale.PaymentMethod == PaymentMethod.Cash ? sale.Amount : sale.CashAmount;
        if (cashPart < 0) cashPart = 0;
        if (cashPart > sale.Amount) cashPart = sale.Amount;
        var creditPart = sale.Amount - cashPart;

        var year = await _db.AccFiscalYears.FirstOrDefaultAsync(f => f.IsCurrent)
                   ?? await _db.AccFiscalYears.OrderByDescending(f => f.StartDate).FirstOrDefaultAsync()
                   ?? throw new InvalidOperationException("هیچ سال مالی تعریف نشده است.");

        var maxNumber = await _db.AccVouchers.Where(v => v.FiscalYearId == year.Id)
            .Select(v => (int?)v.Number).MaxAsync() ?? 0;

        var title = $"فروش شماره {sale.Number}";

        var voucher = new Db.AccVoucher
        {
            FiscalYearId = year.Id,
            Number = maxNumber + 1,
            Date = sale.Date.Date,
            Description = $"سند خودکار — {title}",
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSale,
            SourceId = sale.Id,
            SourceTitle = title,
            RefNumber = sale.Number,
            CreatedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedBy = user,
            ConfirmedAt = DateTime.Now
        };

        var row = 1;
        if (cashPart > 0)
        {
            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++,
                AccountId = cashAccount,
                PartyId = sale.PartyId,
                Description = sale.PaymentMethod == PaymentMethod.Cash ? title : $"پیش‌دریافت نقدی — {title}",
                RefNumber = sale.Number,
                Debit = cashPart,
                Credit = 0
            });
        }
        if (creditPart > 0)
        {
            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++,
                AccountId = receivableAccount,
                PartyId = sale.PartyId,
                Description = $"{PaymentFa(sale.PaymentMethod)} — {title}",
                RefNumber = sale.Number,
                Debit = creditPart,
                Credit = 0
            });
        }

        voucher.Lines.Add(new Db.AccVoucherLine
        {
            RowNo = row,
            AccountId = mainAccount,
            PartyId = sale.PartyId,
            Description = title,
            RefNumber = sale.Number,
            Debit = 0,
            Credit = sale.Amount
        });

        voucher.TotalDebit = voucher.Lines.Sum(l => l.Debit);
        voucher.TotalCredit = voucher.Lines.Sum(l => l.Credit);
        if (voucher.TotalDebit != voucher.TotalCredit)
            throw new InvalidOperationException("سند حسابداری فروش تراز نشد.");

        _db.AccVouchers.Add(voucher);
        await _db.SaveChangesAsync();
        return voucher.Id;
    }

    public async Task UnpostStoreSaleVoucherAsync(int saleId)
    {
        var voucher = await _db.AccVouchers.Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Source == VoucherSource.StoreSale && v.SourceId == saleId);
        if (voucher is null) return;
        _db.AccVoucherLines.RemoveRange(voucher.Lines);
        _db.AccVouchers.Remove(voucher);
        await _db.SaveChangesAsync();
    }

    private static string PaymentFa(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Installment => "اقساطی",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "پرداخت"
    };

    // =====================================================================
    // ۵) قواعد فاکتور
    // =====================================================================

    public async Task<List<FacRule>> GetRulesAsync(Paging.Request? pagination = null)
    {
        var rules = await _db.FacRules.AsNoTracking().ToListAsync();

        // برای هر نوع فاکتور یک ردیف تضمین می‌شود (حتی اگر هنوز ذخیره نشده باشد)
        foreach (InvoiceKind kind in Enum.GetValues<InvoiceKind>())
        {
            if (rules.Any(r => r.Kind == kind)) continue;
            var created = new Db.FacRule { Kind = kind, IsActive = false };
            _db.FacRules.Add(created);
            rules.Add(created);
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
        rules = await _db.FacRules.AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.Id).ToPageListAsync(pagination);

        var accIds = rules.SelectMany(r => new[] { r.PartyAccountId, r.MainAccountId, r.VatAccountId, r.CashAccountId, r.ShippingAccountId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var accounts = await _db.AccAccounts.AsNoTracking().Where(a => accIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.Code} — {a.Name}");

        var typeIds = rules.Where(r => r.DocTypeId is not null).Select(r => r.DocTypeId!.Value).Distinct().ToList();
        var types = await _db.InvDocTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        string? Name(int? id) => id is not null && accounts.ContainsKey(id.Value) ? accounts[id.Value] : null;

        return rules.OrderBy(r => r.Kind).Select(r => new FacRule
        {
            Id = r.Id,
            Kind = r.Kind,
            DocTypeId = r.DocTypeId,
            DocTypeName = r.DocTypeId is not null && types.ContainsKey(r.DocTypeId.Value) ? types[r.DocTypeId.Value] : null,
            PartyAccountId = r.PartyAccountId,
            PartyAccountName = Name(r.PartyAccountId),
            MainAccountId = r.MainAccountId,
            MainAccountName = Name(r.MainAccountId),
            VatAccountId = r.VatAccountId,
            VatAccountName = Name(r.VatAccountId),
            CashAccountId = r.CashAccountId,
            CashAccountName = Name(r.CashAccountId),
            ShippingAccountId = r.ShippingAccountId,
            ShippingAccountName = Name(r.ShippingAccountId),
            AutoInvDoc = r.AutoInvDoc,
            AutoVoucher = r.AutoVoucher,
            IsActive = r.IsActive,
            Description = r.Description
        }).ToList();
    }

    public async Task<FacRule> SaveRuleAsync(FacRule dto)
    {
        var e = await _db.FacRules.FirstOrDefaultAsync(r => r.Kind == dto.Kind);
        if (e is null)
        {
            e = new Db.FacRule { Kind = dto.Kind };
            _db.FacRules.Add(e);
        }

        e.DocTypeId = dto.DocTypeId is > 0 ? dto.DocTypeId : null;
        e.PartyAccountId = dto.PartyAccountId is > 0 ? dto.PartyAccountId : null;
        e.MainAccountId = dto.MainAccountId is > 0 ? dto.MainAccountId : null;
        e.VatAccountId = dto.VatAccountId is > 0 ? dto.VatAccountId : null;
        e.CashAccountId = dto.CashAccountId is > 0 ? dto.CashAccountId : null;
        e.ShippingAccountId = dto.ShippingAccountId is > 0 ? dto.ShippingAccountId : null;
        e.AutoInvDoc = dto.AutoInvDoc;
        e.AutoVoucher = dto.AutoVoucher;
        e.Description = dto.Description;

        if (dto.IsActive && (e.PartyAccountId is null || e.MainAccountId is null))
            throw new InvalidOperationException("برای فعال کردن، حداقل حساب طرف حساب و حساب اصلی را انتخاب کنید.");
        if (dto.IsActive && e.AutoInvDoc && e.DocTypeId is null)
            throw new InvalidOperationException("برای صدور خودکار سند انبار، نوع سند انبار را انتخاب کنید.");

        e.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();
        return (await GetRulesAsync()).First(r => r.Kind == dto.Kind);
    }

    // =====================================================================
    // ۶) گزارش‌ها
    // =====================================================================

    public async Task<FacSummaryResult> GetSummaryAsync(InvoiceKind kind, string groupBy, DateTime? from, DateTime? to)
    {
        var q = _db.FacInvoices.AsNoTracking()
            .Where(i => i.Kind == kind && i.Status == InvoiceStatus.Confirmed);

        if (from is not null) q = q.Where(i => i.Date >= from.Value.Date);
        if (to is not null) q = q.Where(i => i.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        var invoices = await q.Include(i => i.Party).Include(i => i.Lines).ThenInclude(l => l.Product).ToListAsync();

        var rows = groupBy switch
        {
            "party" => invoices
                .GroupBy(i => i.Party?.Name ?? "بدون طرف حساب")
                .Select(g => new FacSummaryRow
                {
                    Title = g.Key,
                    Count = g.Count(),
                    Quantity = g.Sum(i => i.Lines.Sum(l => l.Quantity)),
                    Taxable = g.Sum(i => i.TotalTaxable),
                    Vat = g.Sum(i => i.TotalVat),
                    Net = g.Sum(i => i.TotalNet)
                }).OrderByDescending(r => r.Net).ToList(),

            "product" => invoices.SelectMany(i => i.Lines)
                .GroupBy(l => l.Product?.Name ?? "—")
                .Select(g => new FacSummaryRow
                {
                    Title = g.Key,
                    Count = g.Count(),
                    Quantity = g.Sum(l => l.Quantity),
                    Taxable = g.Sum(l => l.Taxable),
                    Vat = g.Sum(l => l.VatAmount),
                    Net = g.Sum(l => l.Total)
                }).OrderByDescending(r => r.Net).ToList(),

            "day" => invoices
                .GroupBy(i => PersianDate.ToShort(i.Date))
                .Select(g => new FacSummaryRow
                {
                    Title = g.Key,
                    Count = g.Count(),
                    Quantity = g.Sum(i => i.Lines.Sum(l => l.Quantity)),
                    Taxable = g.Sum(i => i.TotalTaxable),
                    Vat = g.Sum(i => i.TotalVat),
                    Net = g.Sum(i => i.TotalNet)
                }).OrderBy(r => r.Title).ToList(),

            _ => invoices
                .GroupBy(i =>
                {
                    var fa = PersianDate.FromGregorian(i.Date);
                    return $"{fa.Year}/{fa.Month:D2}";
                })
                .Select(g => new FacSummaryRow
                {
                    Title = g.Key,
                    Count = g.Count(),
                    Quantity = g.Sum(i => i.Lines.Sum(l => l.Quantity)),
                    Taxable = g.Sum(i => i.TotalTaxable),
                    Vat = g.Sum(i => i.TotalVat),
                    Net = g.Sum(i => i.TotalNet)
                }).OrderBy(r => r.Title).ToList()
        };

        return new FacSummaryResult
        {
            Kind = kind,
            GroupBy = groupBy,
            From = from,
            To = to,
            Rows = rows,
            TotalCount = invoices.Count,
            TotalQuantity = invoices.Sum(i => i.Lines.Sum(l => l.Quantity)),
            TotalTaxable = invoices.Sum(i => i.TotalTaxable),
            TotalVat = invoices.Sum(i => i.TotalVat),
            TotalNet = invoices.Sum(i => i.TotalNet)
        };
    }

    public async Task<FacDashboard> GetDashboardAsync(DateTime? from, DateTime? to)
    {
        var q = _db.FacInvoices.AsNoTracking().AsQueryable();
        if (from is not null) q = q.Where(i => i.Date >= from.Value.Date);
        if (to is not null) q = q.Where(i => i.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        var confirmed = await q.Where(i => i.Status == InvoiceStatus.Confirmed)
            .Include(i => i.Party).Include(i => i.Lines).ThenInclude(l => l.Product)
            .ToListAsync();

        var balanceRows = confirmed.Select(i => new FacInvoice
        {
            Id = i.Id,
            Status = i.Status,
            Settlement = i.Settlement,
            TotalNet = i.TotalNet
        }).ToList();
        await FillSettlementBalancesAsync(balanceRows);
        var remainingByInvoice = balanceRows.ToDictionary(i => i.Id, i => i.RemainingAmount);
        decimal Outstanding(Db.FacInvoice invoice)
            => remainingByInvoice.GetValueOrDefault(invoice.Id);

        decimal Sum(InvoiceKind k) => confirmed.Where(i => i.Kind == k).Sum(i => i.TotalNet);
        decimal Vat(InvoiceKind k) => confirmed.Where(i => i.Kind == k).Sum(i => i.TotalVat);

        var sales = confirmed.Where(i => i.Kind == InvoiceKind.Sale).ToList();
        var saleReturns = confirmed.Where(i => i.Kind == InvoiceKind.SaleReturn).ToList();

        // بهای تمام‌شده از روی اسناد انبارِ متصل به فاکتورهای فروش
        var saleDocIds = sales.Where(i => i.InvDocId is not null).Select(i => i.InvDocId!.Value).ToList();
        var cogs = saleDocIds.Count == 0 ? 0 : await _db.InvDocLines.AsNoTracking()
            .Where(l => saleDocIds.Contains(l.DocId))
            .SumAsync(l => (decimal?)(l.OutCost ?? 0)) ?? 0;

        var netSales = sales.Sum(i => i.TotalTaxable) - saleReturns.Sum(i => i.TotalTaxable);

        var topProducts = confirmed.Where(i => i.Kind == InvoiceKind.Sale).SelectMany(i => i.Lines)
            .GroupBy(l => l.Product?.Name ?? "—")
            .Select(g => new FacSummaryRow
            {
                Title = g.Key,
                Count = g.Count(),
                Quantity = g.Sum(l => l.Quantity),
                Taxable = g.Sum(l => l.Taxable),
                Vat = g.Sum(l => l.VatAmount),
                Net = g.Sum(l => l.Total)
            })
            .OrderByDescending(r => r.Net).Take(5).ToList();

        var topParties = confirmed.Where(i => i.Kind == InvoiceKind.Sale)
            .GroupBy(i => i.Party?.Name ?? "بدون طرف حساب")
            .Select(g => new FacSummaryRow
            {
                Title = g.Key,
                Count = g.Count(),
                Quantity = g.Sum(i => i.Lines.Sum(l => l.Quantity)),
                Taxable = g.Sum(i => i.TotalTaxable),
                Vat = g.Sum(i => i.TotalVat),
                Net = g.Sum(i => i.TotalNet)
            })
            .OrderByDescending(r => r.Net).Take(5).ToList();

        return new FacDashboard
        {
            SaleTotal = Sum(InvoiceKind.Sale),
            PurchaseTotal = Sum(InvoiceKind.Purchase),
            SaleReturnTotal = Sum(InvoiceKind.SaleReturn),
            PurchaseReturnTotal = Sum(InvoiceKind.PurchaseReturn),
            SaleCount = sales.Count,
            PurchaseCount = confirmed.Count(i => i.Kind == InvoiceKind.Purchase),
            DraftCount = await q.CountAsync(i => i.Status == InvoiceStatus.Draft),
            VatPayable = Vat(InvoiceKind.Sale) - Vat(InvoiceKind.SaleReturn)
                         - (Vat(InvoiceKind.Purchase) - Vat(InvoiceKind.PurchaseReturn)),
            Receivable = confirmed.Where(i => i.Kind == InvoiceKind.Sale && i.Settlement == SettlementType.Credit).Sum(Outstanding)
                         - confirmed.Where(i => i.Kind == InvoiceKind.SaleReturn && i.Settlement == SettlementType.Credit).Sum(Outstanding),
            Payable = confirmed.Where(i => i.Kind == InvoiceKind.Purchase && i.Settlement == SettlementType.Credit).Sum(Outstanding)
                      - confirmed.Where(i => i.Kind == InvoiceKind.PurchaseReturn && i.Settlement == SettlementType.Credit).Sum(Outstanding),
            GrossProfit = netSales - cogs,
            TopProducts = topProducts,
            TopParties = topParties
        };
    }

    // =====================================================================
    // کمکی‌ها
    // =====================================================================

    private static string KindTitle(InvoiceKind kind) => kind switch
    {
        InvoiceKind.Purchase => "فاکتور خرید",
        InvoiceKind.Sale => "فاکتور فروش",
        InvoiceKind.PurchaseReturn => "برگشت از خرید",
        _ => "برگشت از فروش"
    };

    private static FacInvoice Map(Db.FacInvoice e, string? partyName, string? warehouseName,
        int lineCount, decimal quantity, string? docNumber, int? voucherNumber) => new()
    {
        Id = e.Id,
        Number = e.Number,
        RefNumber = e.RefNumber,
        Kind = e.Kind,
        Date = e.Date,
        DueDate = e.DueDate,
        PartyId = e.PartyId,
        PartyName = partyName,
        WarehouseId = e.WarehouseId,
        WarehouseName = warehouseName,
        Settlement = e.Settlement,
        Description = e.Description,
        Status = e.Status,
        TotalGross = e.TotalGross,
        TotalLineDiscount = e.TotalLineDiscount,
        InvoiceDiscount = e.InvoiceDiscount,
        TotalTaxable = e.TotalTaxable,
        TotalVat = e.TotalVat,
        ShippingCost = e.ShippingCost,
        TotalNet = e.TotalNet,
        InvDocId = e.InvDocId,
        InvDocNumber = docNumber,
        VoucherId = e.VoucherId,
        VoucherNumber = voucherNumber,
        LineCount = lineCount,
        TotalQuantity = quantity,
        CreatedBy = e.CreatedBy,
        CreatedAt = e.CreatedAt,
        ConfirmedBy = e.ConfirmedBy,
        ConfirmedAt = e.ConfirmedAt
    };
}
