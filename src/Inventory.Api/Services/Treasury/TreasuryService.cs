using System.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Accounting;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Treasury;

/// <summary>
/// پیاده‌سازی ماژول خزانه‌داری.
///
/// اثر قطعی شدن یک سند خزانه:
///   دریافت  → صندوق/بانک، اسناد دریافتنی و تخفیف بدهکار | حساب دریافتنی طرف حساب بستانکار
///   پرداخت  → حساب پرداختنی طرف حساب بدهکار | صندوق/بانک، اسناد پرداختنی و تخفیف بستانکار
///   انتقال  → حساب مقصد و کارمزد بدهکار | حساب مبدأ بستانکار
///
/// چرخه چک: نزد صندوق → واگذاری به بانک → وصول | برگشت | خرج | ابطال
/// هر گام یک سند حسابداری مستقل تولید می‌کند.
/// </summary>
public class TreasuryService : ITreasuryService
{
    private sealed class TrsInvoiceAllocationDtoWithVoucher
    {
        public int TrsVoucherId { get; set; }
        public TrsInvoiceAllocationDto Allocation { get; set; } = new();
    }

    private sealed class TrsInvoiceInfo
    {
        public int Number { get; set; }
        public InvoiceKind Kind { get; set; }
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
    }

    private readonly Db.AppDbContext _db;
    private readonly IAccountingService _acc;

    public TreasuryService(Db.AppDbContext db, IAccountingService acc)
    {
        _db = db;
        _acc = acc;
    }

    // =====================================================================
    // ۱) صندوق و بانک
    // =====================================================================

    public async Task<List<TrsAccount>> GetAccountsAsync(bool activeOnly = false, bool withBalances = false, Paging.Request? pagination = null)
    {
        var q = _db.TrsAccounts.AsNoTracking().Include(a => a.Account).AsQueryable();
        if (activeOnly) q = q.Where(a => a.IsActive);

        var rows = await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Code).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var list = rows.Select(MapAccount).ToList();

        if (!withBalances || list.Count == 0) return list;

        await FillBalancesAsync(list);
        return list;
    }

    public async Task<TrsAccount?> GetAccountAsync(int id)
    {
        var e = await _db.TrsAccounts.AsNoTracking().Include(a => a.Account)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (e is null) return null;

        var dto = MapAccount(e);
        await FillBalancesAsync(new List<TrsAccount> { dto });
        return dto;
    }

    public async Task<List<LookupItem>> GetAccountLookupsAsync(bool activeOnly = true, Paging.Request? pagination = null)
    {
        var q = _db.TrsAccounts.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(a => a.IsActive);

        return await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Code).ThenBy(x => x.Id)
            .Select(a => new LookupItem
            {
                Id = a.Id,
                Name = a.BankName == null || a.Kind != TreasuryAccountKind.Bank
                    ? a.Name
                    : a.Name + " — " + a.BankName
            })
            .ToPageListAsync(pagination);
    }

    public async Task<TrsAccount> SaveAccountAsync(TrsAccount dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام صندوق/بانک الزامی است.");

        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
            code = await NextAccountCodeAsync(dto.Kind);

        if (await _db.TrsAccounts.AnyAsync(a => a.Code == code && a.Id != dto.Id))
            throw new InvalidOperationException($"کد «{code}» قبلاً استفاده شده است.");

        if (dto.Kind == TreasuryAccountKind.Bank && string.IsNullOrWhiteSpace(dto.BankName))
            throw new InvalidOperationException("برای حساب بانکی، نام بانک الزامی است.");

        var e = dto.Id > 0
            ? await _db.TrsAccounts.FirstOrDefaultAsync(a => a.Id == dto.Id)
              ?? throw new InvalidOperationException("صندوق/بانک یافت نشد.")
            : new Db.TrsAccount();

        e.Code = code;
        e.Name = dto.Name.Trim();
        e.Kind = dto.Kind;
        e.AccountId = dto.AccountId is > 0 ? dto.AccountId : null;
        e.BankName = Trim(dto.BankName);
        e.BranchName = Trim(dto.BranchName);
        e.BranchCode = Trim(dto.BranchCode);
        e.AccountNumber = Trim(dto.AccountNumber);
        e.Iban = Trim(dto.Iban);
        e.CardNumber = Trim(dto.CardNumber);
        e.OpeningBalance = dto.OpeningBalance;
        e.IsActive = dto.IsActive;
        e.Description = Trim(dto.Description);
        e.SortOrder = dto.SortOrder;

        if (dto.Id == 0) _db.TrsAccounts.Add(e);
        await _db.SaveChangesAsync();

        // فقط یک حساب می‌تواند پیش‌فرض باشد
        if (dto.IsDefault)
        {
            var others = await _db.TrsAccounts.Where(a => a.IsDefault && a.Id != e.Id).ToListAsync();
            foreach (var o in others) o.IsDefault = false;
            e.IsDefault = true;
            await _db.SaveChangesAsync();
        }
        else if (e.IsDefault)
        {
            e.IsDefault = false;
            await _db.SaveChangesAsync();
        }

        return (await GetAccountAsync(e.Id))!;
    }

    public async Task DeleteAccountAsync(int id)
    {
        var e = await _db.TrsAccounts.FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new InvalidOperationException("صندوق/بانک یافت نشد.");

        if (await _db.TrsVoucherLines.AnyAsync(l => l.TrsAccountId == id)
            || await _db.TrsVouchers.AnyAsync(v => v.FromAccountId == id || v.ToAccountId == id)
            || await _db.TrsCheques.AnyAsync(c => c.TrsAccountId == id))
            throw new InvalidOperationException("این حساب در اسناد خزانه استفاده شده است؛ به‌جای حذف آن را غیرفعال کنید.");

        _db.TrsAccounts.Remove(e);
        await _db.SaveChangesAsync();
    }

    /// <summary>مانده و گردش هر حساب را از سطرهای اسناد قطعی‌شده محاسبه می‌کند.</summary>
    private async Task FillBalancesAsync(List<TrsAccount> list)
    {
        var ids = list.Select(a => a.Id).ToList();

        // گردش سطرهای دریافت/پرداخت (نقد، کارت، حواله)
        var lines = await _db.TrsVoucherLines.AsNoTracking()
            .Where(l => l.TrsAccountId != null && ids.Contains(l.TrsAccountId!.Value)
                        && l.TrsVoucher!.Status == TreasuryStatus.Confirmed)
            .Select(l => new { AccountId = l.TrsAccountId!.Value, l.TrsVoucher!.Kind, l.Amount })
            .ToListAsync();

        // انتقال بین حساب‌ها
        var transfers = await _db.TrsVouchers.AsNoTracking()
            .Where(v => v.Kind == TreasuryKind.Transfer && v.Status == TreasuryStatus.Confirmed)
            .Select(v => new { v.FromAccountId, v.ToAccountId, v.TotalAmount, v.FeeAmount })
            .ToListAsync();

        // وصول چک‌ها: پول واقعاً در تاریخ وصول وارد/خارج بانک می‌شود
        var chequeMoves = await _db.TrsCheques.AsNoTracking()
            .Where(c => c.Status == ChequeStatus.Cleared && c.TrsAccountId != null
                        && ids.Contains(c.TrsAccountId!.Value))
            .Select(c => new { AccountId = c.TrsAccountId!.Value, c.Kind, c.Amount })
            .ToListAsync();

        foreach (var a in list)
        {
            var mine = lines.Where(l => l.AccountId == a.Id).ToList();
            var inSum = mine.Where(l => l.Kind == TreasuryKind.Receipt).Sum(l => l.Amount);
            var outSum = mine.Where(l => l.Kind == TreasuryKind.Payment).Sum(l => l.Amount);

            inSum += transfers.Where(t => t.ToAccountId == a.Id).Sum(t => t.TotalAmount);
            outSum += transfers.Where(t => t.FromAccountId == a.Id).Sum(t => t.TotalAmount + t.FeeAmount);

            var mineCheques = chequeMoves.Where(c => c.AccountId == a.Id).ToList();
            inSum += mineCheques.Where(c => c.Kind == ChequeKind.Received).Sum(c => c.Amount);
            outSum += mineCheques.Where(c => c.Kind == ChequeKind.Issued).Sum(c => c.Amount);

            a.TotalIn = inSum;
            a.TotalOut = outSum;
            a.Balance = a.OpeningBalance + inSum - outSum;
            a.MoveCount = mine.Count + mineCheques.Count
                          + transfers.Count(t => t.FromAccountId == a.Id || t.ToAccountId == a.Id);
        }
    }

    private async Task<string> NextAccountCodeAsync(TreasuryAccountKind kind)
    {
        var prefix = kind switch
        {
            TreasuryAccountKind.Cash => "CSH",
            TreasuryAccountKind.Bank => "BNK",
            TreasuryAccountKind.Pos => "POS",
            _ => "PTY"
        };
        var count = await _db.TrsAccounts.CountAsync(a => a.Kind == kind);
        var code = $"{prefix}-{count + 1:00}";
        while (await _db.TrsAccounts.AnyAsync(a => a.Code == code))
        {
            count++;
            code = $"{prefix}-{count + 1:00}";
        }
        return code;
    }

    private static TrsAccount MapAccount(Db.TrsAccount e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Name = e.Name,
        Kind = e.Kind,
        AccountId = e.AccountId,
        AccountCode = e.Account?.Code,
        AccountName = e.Account?.Name,
        BankName = e.BankName,
        BranchName = e.BranchName,
        BranchCode = e.BranchCode,
        AccountNumber = e.AccountNumber,
        Iban = e.Iban,
        CardNumber = e.CardNumber,
        OpeningBalance = e.OpeningBalance,
        IsDefault = e.IsDefault,
        IsActive = e.IsActive,
        Description = e.Description,
        SortOrder = e.SortOrder
    };

    // =====================================================================
    // ۲) اسناد خزانه — خواندن
    // =====================================================================

    public async Task<PagedResult<TrsVoucher>> GetVouchersAsync(TreasuryKind? kind, TreasuryStatus? status,
        int? partyId, int? trsAccountId, string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.TrsVouchers.AsNoTracking().AsQueryable();

        if (kind is not null) q = q.Where(v => v.Kind == kind);
        if (status is not null) q = q.Where(v => v.Status == status);
        if (partyId is > 0) q = q.Where(v => v.PartyId == partyId);
        if (from is not null) q = q.Where(v => v.Date >= from.Value.Date);
        if (to is not null) q = q.Where(v => v.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        if (trsAccountId is > 0)
            q = q.Where(v => v.FromAccountId == trsAccountId || v.ToAccountId == trsAccountId
                             || v.Lines.Any(l => l.TrsAccountId == trsAccountId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(v => (v.RefNumber ?? "").Contains(s)
                             || (v.Description ?? "").Contains(s)
                             || (v.Party != null && v.Party.Name.Contains(s))
                             || v.Number.ToString().Contains(s));
        }

        var total = await q.CountAsync();

        var rows = await q
            .OrderByDescending(v => v.Date).ThenByDescending(v => v.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(v => new TrsVoucher
            {
                Id = v.Id,
                Number = v.Number,
                Kind = v.Kind,
                Date = v.Date,
                PartyId = v.PartyId,
                PartyName = v.Party != null ? v.Party.Name : null,
                Description = v.Description,
                RefNumber = v.RefNumber,
                Status = v.Status,
                FromAccountId = v.FromAccountId,
                FromAccountName = v.FromAccount != null ? v.FromAccount.Name : null,
                ToAccountId = v.ToAccountId,
                ToAccountName = v.ToAccount != null ? v.ToAccount.Name : null,
                FeeAmount = v.FeeAmount,
                InvoiceId = v.InvoiceId,
                TotalAmount = v.TotalAmount,
                CashAmount = v.CashAmount,
                ChequeAmount = v.ChequeAmount,
                DiscountAmount = v.DiscountAmount,
                VoucherId = v.VoucherId,
                LineCount = v.Lines.Count,
                CreatedBy = v.CreatedBy,
                CreatedAt = v.CreatedAt,
                ConfirmedBy = v.ConfirmedBy,
                ConfirmedAt = v.ConfirmedAt
            })
            .ToListAsync();

        // بارگذاریِ پیوندهای فاکتور به‌صورت جداگانه تا فهرست سندها سبک بماند.
        var voucherIds = rows.Select(v => v.Id).ToList();
        var allocationRows = voucherIds.Count == 0
            ? new List<TrsInvoiceAllocationDtoWithVoucher>()
            : await _db.TrsInvoiceAllocations.AsNoTracking()
                .Where(a => voucherIds.Contains(a.TrsVoucherId))
                .Select(a => new TrsInvoiceAllocationDtoWithVoucher
                {
                    TrsVoucherId = a.TrsVoucherId,
                    Allocation = new TrsInvoiceAllocationDto
                    {
                        InvoiceId = a.InvoiceId,
                        InvoiceNumber = a.Invoice != null ? a.Invoice.Number : 0,
                        InvoiceKind = a.Invoice != null ? a.Invoice.Kind : InvoiceKind.Sale,
                        InvoiceDate = a.Invoice != null ? a.Invoice.Date : DateTime.MinValue,
                        InvoiceTotal = a.Invoice != null ? a.Invoice.TotalNet : 0,
                        Amount = a.Amount
                    }
                }).ToListAsync();

        var allocationGroups = allocationRows.GroupBy(a => a.TrsVoucherId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Allocation).OrderBy(a => a.InvoiceNumber).ToList());
        var legacyInvoiceIds = rows.Where(v => v.InvoiceId is > 0 && !allocationGroups.ContainsKey(v.Id))
            .Select(v => v.InvoiceId!.Value).Distinct().ToList();
        var legacyInvoiceInfo = new Dictionary<int, TrsInvoiceInfo>();
        if (legacyInvoiceIds.Count > 0)
        {
            var legacyInfoRows = await _db.FacInvoices.AsNoTracking()
                .Where(i => legacyInvoiceIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Number, i.Kind, i.Date, i.TotalNet })
                .ToListAsync();
            legacyInvoiceInfo = legacyInfoRows.ToDictionary(i => i.Id, i => new TrsInvoiceInfo
            {
                Number = i.Number,
                Kind = i.Kind,
                Date = i.Date,
                Total = i.TotalNet
            });
        }

        foreach (var row in rows)
        {
            if (allocationGroups.TryGetValue(row.Id, out var allocations))
            {
                row.InvoiceAllocations = allocations;
                if (allocations.Count == 1)
                {
                    row.InvoiceId = allocations[0].InvoiceId;
                    row.InvoiceNumber = allocations[0].InvoiceNumber;
                }
                else
                {
                    row.InvoiceId = null;
                    row.InvoiceNumber = null;
                }
            }
            else if (row.InvoiceId is > 0 && legacyInvoiceInfo.TryGetValue(row.InvoiceId.Value, out var info))
            {
                row.InvoiceNumber = info.Number;
                if (row.TotalAmount > 0)
                    row.InvoiceAllocations.Add(new TrsInvoiceAllocationDto
                    {
                        InvoiceId = row.InvoiceId.Value,
                        InvoiceNumber = info.Number,
                        InvoiceKind = info.Kind,
                        InvoiceDate = info.Date,
                        InvoiceTotal = info.Total,
                        Amount = row.TotalAmount
                    });
            }
        }

        await FillVoucherNumbersAsync(rows);
        return new PagedResult<TrsVoucher> { Items = rows, TotalCount = total };
    }

    public async Task<TrsVoucher?> GetVoucherAsync(int id)
    {
        var e = await _db.TrsVouchers.AsNoTracking()
            .Include(v => v.Party)
            .Include(v => v.FromAccount)
            .Include(v => v.ToAccount)
            .Include(v => v.Lines).ThenInclude(l => l.TrsAccount)
            .Include(v => v.Lines).ThenInclude(l => l.Cheque)
            .Include(v => v.InvoiceAllocations).ThenInclude(a => a.Invoice)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (e is null) return null;

        var dto = new TrsVoucher
        {
            Id = e.Id,
            Number = e.Number,
            Kind = e.Kind,
            Date = e.Date,
            PartyId = e.PartyId,
            PartyName = e.Party?.Name,
            Description = e.Description,
            RefNumber = e.RefNumber,
            Status = e.Status,
            FromAccountId = e.FromAccountId,
            FromAccountName = e.FromAccount?.Name,
            ToAccountId = e.ToAccountId,
            ToAccountName = e.ToAccount?.Name,
            FeeAmount = e.FeeAmount,
            InvoiceId = e.InvoiceId,
            TotalAmount = e.TotalAmount,
            CashAmount = e.CashAmount,
            ChequeAmount = e.ChequeAmount,
            DiscountAmount = e.DiscountAmount,
            VoucherId = e.VoucherId,
            LineCount = e.Lines.Count,
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt,
            ConfirmedBy = e.ConfirmedBy,
            ConfirmedAt = e.ConfirmedAt,
            Lines = e.Lines.OrderBy(l => l.RowNo).Select(l => new TrsVoucherLine
            {
                Id = l.Id,
                RowNo = l.RowNo,
                Method = l.Method,
                TrsAccountId = l.TrsAccountId,
                TrsAccountName = l.TrsAccount?.Name,
                Amount = l.Amount,
                RefNumber = l.RefNumber,
                Description = l.Description,
                ChequeId = l.ChequeId,
                ChequeNumber = l.Cheque?.Number,
                SayadId = l.Cheque?.SayadId,
                ChequeBankName = l.Cheque?.BankName,
                ChequeBranchName = l.Cheque?.BranchName,
                ChequeAccountNumber = l.Cheque?.AccountNumber,
                IssueDate = l.Cheque?.IssueDate,
                DueDate = l.Cheque?.DueDate,
                ChequeStatus = l.Cheque?.Status
            }).ToList()
        };

        if (e.InvoiceAllocations.Count > 0)
        {
            dto.InvoiceAllocations = e.InvoiceAllocations.OrderBy(a => a.Invoice?.Number).Select(a => new TrsInvoiceAllocationDto
            {
                InvoiceId = a.InvoiceId,
                InvoiceNumber = a.Invoice?.Number ?? 0,
                InvoiceKind = a.Invoice?.Kind ?? InvoiceKind.Sale,
                InvoiceDate = a.Invoice?.Date ?? DateTime.MinValue,
                InvoiceTotal = a.Invoice?.TotalNet ?? 0,
                Amount = a.Amount
            }).ToList();

            if (dto.InvoiceAllocations.Count == 1)
            {
                dto.InvoiceId = dto.InvoiceAllocations[0].InvoiceId;
                dto.InvoiceNumber = dto.InvoiceAllocations[0].InvoiceNumber;
            }
            else
            {
                dto.InvoiceId = null;
                dto.InvoiceNumber = null;
            }
        }
        else if (e.InvoiceId is > 0)
        {
            var info = await _db.FacInvoices.AsNoTracking().Where(i => i.Id == e.InvoiceId)
                .Select(i => new { i.Number, i.Kind, i.Date, i.TotalNet }).FirstOrDefaultAsync();
            if (info is not null)
            {
                dto.InvoiceNumber = info.Number;
                if (e.TotalAmount > 0)
                    dto.InvoiceAllocations.Add(new TrsInvoiceAllocationDto
                    {
                        InvoiceId = e.InvoiceId.Value,
                        InvoiceNumber = info.Number,
                        InvoiceKind = info.Kind,
                        InvoiceDate = info.Date,
                        InvoiceTotal = info.TotalNet,
                        Amount = e.TotalAmount
                    });
            }
        }

        if (dto.InvoiceAllocations.Count > 0)
        {
            var priorSettlements = await GetConfirmedInvoiceSettlementsAsync(
                dto.InvoiceAllocations.Select(a => a.InvoiceId), e.Id);

            Dictionary<int, decimal> effectiveAmounts;
            if (e.Status == TreasuryStatus.Confirmed)
            {
                var reversedChequeAmount = e.Lines
                    .Where(l => l.Method == PayMethod.Cheque && l.Cheque != null
                                && (l.Cheque.Status == ChequeStatus.Bounced
                                    || l.Cheque.Status == ChequeStatus.Cancelled))
                    .Sum(l => l.Amount);
                var currentSources = dto.InvoiceAllocations.Select(a => new InvoiceSettlementSource
                {
                    VoucherId = e.Id,
                    InvoiceId = a.InvoiceId,
                    Amount = a.Amount,
                    VoucherTotal = e.TotalAmount
                });
                effectiveAmounts = InvoiceSettlementMath.Calculate(currentSources,
                    new Dictionary<int, decimal> { [e.Id] = reversedChequeAmount });
            }
            else if (e.Status == TreasuryStatus.Cancelled)
            {
                effectiveAmounts = new Dictionary<int, decimal>();
            }
            else
            {
                effectiveAmounts = dto.InvoiceAllocations.ToDictionary(a => a.InvoiceId, a => a.Amount);
            }

            foreach (var allocation in dto.InvoiceAllocations)
            {
                allocation.RemainingBeforeSettlement = Math.Max(0,
                    allocation.InvoiceTotal - priorSettlements.GetValueOrDefault(allocation.InvoiceId));
                allocation.EffectiveAmount = effectiveAmounts.GetValueOrDefault(allocation.InvoiceId);
            }
        }

        await FillVoucherNumbersAsync(new List<TrsVoucher> { dto });
        return dto;
    }

    /// <summary>شماره سند حسابداری مرتبط را برای نمایش پر می‌کند.</summary>
    private async Task FillVoucherNumbersAsync(List<TrsVoucher> rows)
    {
        var ids = rows.Where(r => r.VoucherId is > 0).Select(r => r.VoucherId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var map = await _db.AccVouchers.AsNoTracking().Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Number);

        foreach (var r in rows)
            if (r.VoucherId is > 0 && map.TryGetValue(r.VoucherId.Value, out var n))
                r.VoucherNumber = n;
    }

    public async Task<TrsVoucher> NewVoucherAsync(TreasuryKind kind)
    {
        var maxNumber = await _db.TrsVouchers.Where(v => v.Kind == kind)
            .Select(v => (int?)v.Number).MaxAsync() ?? 0;

        var dto = new TrsVoucher
        {
            Kind = kind,
            Number = maxNumber + 1,
            Date = DateTime.Now.Date,
            Status = TreasuryStatus.Draft
        };

        if (kind == TreasuryKind.Transfer) return dto;

        // یک سطر نقدی خالی روی حساب پیش‌فرض
        var def = await _db.TrsAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.SortOrder)
            .FirstOrDefaultAsync();

        dto.Lines.Add(new TrsVoucherLine
        {
            RowNo = 1,
            Method = PayMethod.Cash,
            TrsAccountId = def?.Id,
            TrsAccountName = def?.Name
        });

        return dto;
    }

    public async Task<TrsVoucher> NewSettlementAsync(int invoiceId)
    {
        var inv = await _db.FacInvoices.AsNoTracking().Include(i => i.Party)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        if (inv.Status != InvoiceStatus.Confirmed)
            throw new InvalidOperationException("فقط فاکتور قطعی قابل تسویه است.");
        if (inv.Settlement != SettlementType.Credit)
            throw new InvalidOperationException("فاکتور نقدی قبلاً در زمان صدور ثبت شده و در خزانه مانده‌ای برای تسویه ندارد.");
        if (inv.PartyId is not > 0)
            throw new InvalidOperationException("برای تسویه، فاکتور باید طرف حساب داشته باشد.");

        // فروش و برگشت از خرید → دریافت | خرید و برگشت از فروش → پرداخت
        var kind = KindForInvoice(inv.Kind);
        var dto = await NewVoucherAsync(kind);
        var settled = await GetConfirmedInvoiceSettlementsAsync(new[] { inv.Id });
        var paid = settled.GetValueOrDefault(inv.Id);
        var remaining = Math.Max(0, inv.TotalNet - paid);
        if (remaining <= 0)
            throw new InvalidOperationException("این فاکتور قبلاً به‌طور کامل تسویه شده است.");

        dto.PartyId = inv.PartyId;
        dto.PartyName = inv.Party?.Name;
        dto.InvoiceId = inv.Id;
        dto.InvoiceNumber = inv.Number;
        dto.RefNumber = inv.Number.ToString();
        dto.Description = $"تسویه {KindTitle(inv.Kind)} شماره {inv.Number}";
        dto.InvoiceAllocations.Add(new TrsInvoiceAllocationDto
        {
            InvoiceId = inv.Id,
            InvoiceNumber = inv.Number,
            InvoiceKind = inv.Kind,
            InvoiceDate = inv.Date,
            InvoiceTotal = inv.TotalNet,
            RemainingBeforeSettlement = remaining,
            EffectiveAmount = remaining,
            Amount = remaining
        });

        if (dto.Lines.Count > 0) dto.Lines[0].Amount = remaining;
        dto.TotalAmount = remaining;
        dto.CashAmount = remaining;
        return dto;
    }

    public async Task<List<TrsSettlementInvoice>> GetOpenInvoicesAsync(int partyId, TreasuryKind kind)
    {
        if (partyId <= 0 || kind == TreasuryKind.Transfer) return new List<TrsSettlementInvoice>();

        var invoiceKinds = kind == TreasuryKind.Receipt
            ? new[] { InvoiceKind.Sale, InvoiceKind.PurchaseReturn }
            : new[] { InvoiceKind.Purchase, InvoiceKind.SaleReturn };

        // فقط فاکتورهای قطعیِ نسیه قابل تخصیص به دریافت/پرداخت خزانه هستند.
        var invoices = await _db.FacInvoices.AsNoTracking()
            .Where(i => i.PartyId == partyId
                        && i.Status == InvoiceStatus.Confirmed
                        && i.Settlement == SettlementType.Credit
                        && invoiceKinds.Contains(i.Kind)
                        && i.TotalNet > 0)
            .OrderBy(i => i.DueDate ?? i.Date).ThenBy(i => i.Number)
            .Select(i => new TrsSettlementInvoice
            {
                InvoiceId = i.Id,
                InvoiceNumber = i.Number,
                InvoiceKind = i.Kind,
                InvoiceDate = i.Date,
                DueDate = i.DueDate,
                InvoiceTotal = i.TotalNet
            })
            .ToListAsync();

        if (invoices.Count == 0) return invoices;

        var settled = await GetConfirmedInvoiceSettlementsAsync(invoices.Select(i => i.InvoiceId).ToList());
        foreach (var invoice in invoices)
        {
            invoice.SettledAmount = Math.Min(invoice.InvoiceTotal, settled.GetValueOrDefault(invoice.InvoiceId));
            invoice.RemainingAmount = Math.Max(0, invoice.InvoiceTotal - invoice.SettledAmount);
        }

        return invoices.Where(i => i.RemainingAmount > 0).ToList();
    }

    /// <summary>
    /// جمع تخصیص‌های اسناد قطعی را محاسبه می‌کند. برای اسناد تک‌فاکتوری قدیمی که هنوز جدول
    /// تخصیص برایشان ساخته نشده، ستون قدیمی InvoiceId/TotalAmount نیز به‌عنوان fallback خوانده می‌شود.
    /// </summary>
    private async Task<Dictionary<int, decimal>> GetConfirmedInvoiceSettlementsAsync(
        IEnumerable<int> invoiceIds, int? excludeVoucherId = null)
    {
        var ids = invoiceIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, decimal>();

        var allocationVoucherIds = new List<int>();
        foreach (var idChunk in ids.Chunk(1000))
        {
            var allocationVoucherQuery = _db.TrsInvoiceAllocations.AsNoTracking()
                .Where(a => idChunk.Contains(a.InvoiceId)
                            && a.TrsVoucher != null
                            && a.TrsVoucher.Status == TreasuryStatus.Confirmed);
            if (excludeVoucherId is > 0)
                allocationVoucherQuery = allocationVoucherQuery.Where(a => a.TrsVoucherId != excludeVoucherId.Value);
            allocationVoucherIds.AddRange(await allocationVoucherQuery.Select(a => a.TrsVoucherId)
                .Distinct().ToListAsync());
        }
        allocationVoucherIds = allocationVoucherIds.Distinct().ToList();

        var sources = new List<InvoiceSettlementSource>();
        // Load every invoice row for each relevant voucher so returned cheques can be apportioned
        // across a multi-invoice voucher consistently, even when this call requested only one invoice.
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

        foreach (var idChunk in ids.Chunk(1000))
        {
            var legacyQuery = _db.TrsVouchers.AsNoTracking()
                .Where(v => v.InvoiceId.HasValue
                            && idChunk.Contains(v.InvoiceId.Value)
                            && v.Status == TreasuryStatus.Confirmed
                            && !v.InvoiceAllocations.Any());
            if (excludeVoucherId is > 0)
                legacyQuery = legacyQuery.Where(v => v.Id != excludeVoucherId.Value);

            var legacyRows = await legacyQuery
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

        if (sources.Count == 0) return new Dictionary<int, decimal>();

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

        return InvoiceSettlementMath.Calculate(sources, reversed);
    }

    private static TreasuryKind KindForInvoice(InvoiceKind kind)
        => kind is InvoiceKind.Sale or InvoiceKind.PurchaseReturn
            ? TreasuryKind.Receipt
            : TreasuryKind.Payment;

    private static string KindTitle(InvoiceKind k) => k switch
    {
        InvoiceKind.Purchase => "فاکتور خرید",
        InvoiceKind.Sale => "فاکتور فروش",
        InvoiceKind.PurchaseReturn => "برگشت از خرید",
        _ => "برگشت از فروش"
    };

    // =====================================================================
    // ۳) اسناد خزانه — ثبت و ویرایش
    // =====================================================================

    public async Task<TrsVoucher> SaveVoucherAsync(TrsVoucher dto, string? user)
    {
        Validate(dto);

        var e = dto.Id > 0
            ? await _db.TrsVouchers.Include(v => v.Lines).Include(v => v.InvoiceAllocations)
                .FirstOrDefaultAsync(v => v.Id == dto.Id)
              ?? throw new InvalidOperationException("سند خزانه یافت نشد.")
            : new Db.TrsVoucher { Kind = dto.Kind, CreatedBy = user, CreatedAt = DateTime.Now };

        if (e.Status == TreasuryStatus.Confirmed)
            throw new InvalidOperationException("سند قطعی قابل ویرایش نیست؛ ابتدا آن را به پیش‌نویس برگردانید.");
        if (e.Status == TreasuryStatus.Cancelled)
            throw new InvalidOperationException("سند ابطال‌شده قابل ویرایش نیست.");
        if (dto.Id > 0 && e.Kind != dto.Kind)
            throw new InvalidOperationException("نوع سند خزانه قابل تغییر نیست.");

        var expectedTotal = dto.Kind == TreasuryKind.Transfer
            ? dto.TotalAmount
            : dto.Lines.Where(l => l.Amount != 0).Sum(l => l.Amount);
        var allocations = NormalizeInvoiceAllocations(dto, expectedTotal);
        await ValidateInvoiceAllocationsAsync(dto.Kind, dto.PartyId, allocations, expectedTotal, dto.Id);

        if (dto.Id == 0)
        {
            var maxNumber = await _db.TrsVouchers.Where(v => v.Kind == dto.Kind)
                .Select(v => (int?)v.Number).MaxAsync() ?? 0;
            e.Number = maxNumber + 1;
        }
        else if (dto.Number > 0 && dto.Number != e.Number)
        {
            if (await _db.TrsVouchers.AnyAsync(v => v.Kind == e.Kind && v.Number == dto.Number && v.Id != e.Id))
                throw new InvalidOperationException($"سند شماره {dto.Number} قبلاً ثبت شده است.");
            e.Number = dto.Number;
        }

        e.Date = dto.Date.Date;
        e.PartyId = dto.Kind == TreasuryKind.Transfer ? null : (dto.PartyId is > 0 ? dto.PartyId : null);
        e.Description = Trim(dto.Description);
        e.RefNumber = Trim(dto.RefNumber);
        e.InvoiceId = allocations.Count == 1 ? allocations[0].InvoiceId : null;

        if (dto.Kind == TreasuryKind.Transfer)
        {
            e.FromAccountId = dto.FromAccountId;
            e.ToAccountId = dto.ToAccountId;
            e.FeeAmount = dto.FeeAmount;
            e.TotalAmount = dto.TotalAmount;
            e.CashAmount = dto.TotalAmount;
            e.ChequeAmount = 0;
            e.DiscountAmount = 0;

            _db.TrsVoucherLines.RemoveRange(e.Lines);
            e.Lines.Clear();
        }
        else
        {
            e.FromAccountId = null;
            e.ToAccountId = null;
            e.FeeAmount = 0;
            await SyncLinesAsync(e, dto, user);
        }

        SyncInvoiceAllocations(e, allocations);

        if (dto.Id == 0) _db.TrsVouchers.Add(e);
        await _db.SaveChangesAsync();

        return (await GetVoucherAsync(e.Id))!;
    }

    /// <summary>اعتبارسنجی عمومی سند خزانه پیش از ذخیره</summary>
    private static void Validate(TrsVoucher dto)
    {
        if (dto.Lines is null)
            throw new InvalidOperationException("سطرهای سند معتبر نیستند.");

        if (dto.Kind == TreasuryKind.Transfer)
        {
            if (dto.FromAccountId is not > 0 || dto.ToAccountId is not > 0)
                throw new InvalidOperationException("حساب مبدأ و مقصد انتقال را انتخاب کنید.");
            if (dto.FromAccountId == dto.ToAccountId)
                throw new InvalidOperationException("حساب مبدأ و مقصد نمی‌توانند یکی باشند.");
            if (dto.TotalAmount <= 0)
                throw new InvalidOperationException("مبلغ انتقال باید بزرگ‌تر از صفر باشد.");
            if (dto.InvoiceId is > 0 || (dto.InvoiceAllocations?.Count ?? 0) > 0)
                throw new InvalidOperationException("انتقال بین حساب‌ها به فاکتور تخصیص داده نمی‌شود.");
            return;
        }

        var lines = dto.Lines.Where(l => l.Amount != 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("حداقل یک سطر با مبلغ ثبت کنید.");

        foreach (var l in lines)
        {
            if (l.Amount < 0)
                throw new InvalidOperationException("مبلغ سطر نمی‌تواند منفی باشد.");

            if (l.NeedsAccount && l.TrsAccountId is not > 0)
                throw new InvalidOperationException($"برای سطر «{l.MethodTitle}» صندوق/بانک را انتخاب کنید.");

            if (l.Method == PayMethod.Cheque)
            {
                if (string.IsNullOrWhiteSpace(l.ChequeNumber))
                    throw new InvalidOperationException("شماره چک الزامی است.");
                if (l.DueDate is null)
                    throw new InvalidOperationException("تاریخ سررسید چک الزامی است.");
            }
        }
    }

    /// <summary>تبدیل پیوند تک‌فاکتوری قدیمی به تخصیص جدید، بدون شکستن API قبلی.</summary>
    private static List<TrsInvoiceAllocationDto> NormalizeInvoiceAllocations(TrsVoucher dto, decimal totalAmount)
    {
        var allocations = (dto.InvoiceAllocations ?? new List<TrsInvoiceAllocationDto>())
            .Where(a => a.InvoiceId > 0 || a.Amount != 0)
            .Select(a => new TrsInvoiceAllocationDto
            {
                InvoiceId = a.InvoiceId,
                InvoiceNumber = a.InvoiceNumber,
                InvoiceKind = a.InvoiceKind,
                InvoiceDate = a.InvoiceDate,
                InvoiceTotal = a.InvoiceTotal,
                RemainingBeforeSettlement = a.RemainingBeforeSettlement,
                Amount = a.Amount
            })
            .ToList();

        if (allocations.Count == 0 && dto.InvoiceId is > 0 && totalAmount > 0)
            allocations.Add(new TrsInvoiceAllocationDto
            {
                InvoiceId = dto.InvoiceId.Value,
                InvoiceNumber = dto.InvoiceNumber ?? 0,
                Amount = totalAmount
            });

        return allocations;
    }

    private static List<TrsInvoiceAllocationDto> AllocationDtos(Db.TrsVoucher voucher)
    {
        if (voucher.InvoiceAllocations.Count > 0)
            return voucher.InvoiceAllocations.OrderBy(a => a.Invoice?.Number).Select(a => new TrsInvoiceAllocationDto
            {
                InvoiceId = a.InvoiceId,
                InvoiceNumber = a.Invoice?.Number ?? 0,
                InvoiceKind = a.Invoice?.Kind ?? InvoiceKind.Sale,
                InvoiceDate = a.Invoice?.Date ?? DateTime.MinValue,
                InvoiceTotal = a.Invoice?.TotalNet ?? 0,
                Amount = a.Amount
            }).ToList();

        // سازگاری با سطرهای تک‌فاکتوری قدیمی قبل/حین اجرای مهاجرت.
        return voucher.InvoiceId is > 0 && voucher.TotalAmount > 0
            ? new List<TrsInvoiceAllocationDto>
            {
                new()
                {
                    InvoiceId = voucher.InvoiceId.Value,
                    InvoiceNumber = voucher.InvoiceAllocations.FirstOrDefault()?.Invoice?.Number ?? 0,
                    Amount = voucher.TotalAmount
                }
            }
            : new List<TrsInvoiceAllocationDto>();
    }

    /// <summary>کنترل طرف حساب، نوع، ماندهٔ باز و سقف مبلغ تخصیص‌یافته به فاکتورها.</summary>
    private async Task ValidateInvoiceAllocationsAsync(TreasuryKind kind, int? partyId,
        IReadOnlyCollection<TrsInvoiceAllocationDto> allocations, decimal totalAmount, int? excludeVoucherId)
    {
        if (allocations.Count == 0) return;
        if (kind == TreasuryKind.Transfer)
            throw new InvalidOperationException("انتقال بین حساب‌ها به فاکتور تخصیص داده نمی‌شود.");
        if (partyId is not > 0)
            throw new InvalidOperationException("برای تخصیص به فاکتور، طرف حساب را انتخاب کنید.");
        if (allocations.Any(a => a.InvoiceId <= 0 || a.Amount <= 0))
            throw new InvalidOperationException("برای هر فاکتور، مبلغ تخصیص‌یافته باید بیشتر از صفر باشد.");
        if (allocations.GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1))
            throw new InvalidOperationException("هر فاکتور فقط یک‌بار در فهرست تخصیص وارد شود.");

        var allocatedTotal = allocations.Sum(a => a.Amount);
        if (allocatedTotal > totalAmount)
            throw new InvalidOperationException(
                $"جمع تخصیص فاکتورها ({allocatedTotal:N0}) از جمع سند ({totalAmount:N0}) بیشتر است.");

        var ids = allocations.Select(a => a.InvoiceId).Distinct().ToList();
        var invoices = await _db.FacInvoices.AsNoTracking().Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);
        if (invoices.Count != ids.Count)
            throw new InvalidOperationException("یکی از فاکتورهای انتخاب‌شده یافت نشد.");

        var settled = await GetConfirmedInvoiceSettlementsAsync(ids, excludeVoucherId);
        foreach (var allocation in allocations)
        {
            var invoice = invoices[allocation.InvoiceId];
            if (invoice.Status != InvoiceStatus.Confirmed)
                throw new InvalidOperationException($"فاکتور شماره {invoice.Number} قطعی نیست و قابل تسویه نمی‌باشد.");
            if (invoice.Settlement != SettlementType.Credit)
                throw new InvalidOperationException($"فاکتور شماره {invoice.Number} نقدی است و ماندهٔ نسیه ندارد.");
            if (invoice.PartyId != partyId)
                throw new InvalidOperationException("تمام فاکتورهای یک سند تسویه باید متعلق به همان طرف حساب باشند.");
            if (KindForInvoice(invoice.Kind) != kind)
                throw new InvalidOperationException($"نوع دریافت/پرداخت با فاکتور شماره {invoice.Number} هم‌خوانی ندارد.");

            var remaining = Math.Max(0, invoice.TotalNet - settled.GetValueOrDefault(invoice.Id));
            if (remaining <= 0)
                throw new InvalidOperationException($"فاکتور شماره {invoice.Number} قبلاً کامل تسویه شده است.");
            if (allocation.Amount > remaining)
                throw new InvalidOperationException(
                    $"مبلغ تخصیص به فاکتور شماره {invoice.Number} ({allocation.Amount:N0}) از ماندهٔ آن ({remaining:N0}) بیشتر است.");
        }
    }

    /// <summary>سطرهای تخصیص فاکتور را با وضعیت سند خزانه همگام می‌کند.</summary>
    private void SyncInvoiceAllocations(Db.TrsVoucher e, IReadOnlyCollection<TrsInvoiceAllocationDto> incoming)
    {
        var keepInvoiceIds = incoming.Select(a => a.InvoiceId).ToHashSet();
        var removed = e.InvoiceAllocations.Where(a => !keepInvoiceIds.Contains(a.InvoiceId)).ToList();
        if (removed.Count > 0)
        {
            _db.TrsInvoiceAllocations.RemoveRange(removed);
            foreach (var row in removed) e.InvoiceAllocations.Remove(row);
        }

        foreach (var allocation in incoming)
        {
            var row = e.InvoiceAllocations.FirstOrDefault(a => a.InvoiceId == allocation.InvoiceId);
            if (row is null)
            {
                row = new Db.TrsInvoiceAllocation { TrsVoucher = e, InvoiceId = allocation.InvoiceId };
                e.InvoiceAllocations.Add(row);
            }
            row.Amount = allocation.Amount;
        }
    }

    /// <summary>سطرها را همگام می‌کند و برای سطرهای چک، رکورد چک می‌سازد یا به‌روز می‌کند.</summary>
    private async Task SyncLinesAsync(Db.TrsVoucher e, TrsVoucher dto, string? user)
    {
        var incoming = dto.Lines.Where(l => l.Amount != 0).ToList();
        var keepIds = incoming.Where(l => l.Id > 0).Select(l => l.Id).ToHashSet();

        // سطرهای حذف‌شده — چک‌های آن‌ها هم حذف می‌شوند
        var removed = e.Lines.Where(l => !keepIds.Contains(l.Id)).ToList();
        foreach (var r in removed)
        {
            if (r.ChequeId is > 0)
            {
                var ch = await _db.TrsCheques.Include(c => c.Actions).FirstOrDefaultAsync(c => c.Id == r.ChequeId);
                if (ch is not null && ch.Status == ChequeStatus.InHand)
                {
                    _db.TrsChequeActions.RemoveRange(ch.Actions);
                    _db.TrsCheques.Remove(ch);
                }
            }
            _db.TrsVoucherLines.Remove(r);
            e.Lines.Remove(r);
        }

        var chequeKind = dto.Kind == TreasuryKind.Receipt ? ChequeKind.Received : ChequeKind.Issued;
        var row = 1;

        foreach (var l in incoming)
        {
            var line = l.Id > 0 ? e.Lines.FirstOrDefault(x => x.Id == l.Id) : null;
            if (line is null)
            {
                line = new Db.TrsVoucherLine();
                e.Lines.Add(line);
            }

            line.RowNo = row++;
            line.Method = l.Method;
            line.TrsAccountId = l.NeedsAccount ? l.TrsAccountId : null;
            line.Amount = l.Amount;
            line.RefNumber = Trim(l.RefNumber);
            line.Description = Trim(l.Description);

            if (l.Method == PayMethod.Cheque)
            {
                var ch = line.ChequeId is > 0
                    ? await _db.TrsCheques.FirstOrDefaultAsync(c => c.Id == line.ChequeId)
                    : null;

                if (ch is null)
                {
                    ch = new Db.TrsCheque { CreatedBy = user, CreatedAt = DateTime.Now };
                    _db.TrsCheques.Add(ch);
                }

                if (ch.Status != ChequeStatus.InHand && ch.Id > 0)
                    throw new InvalidOperationException(
                        $"چک شماره {ch.Number} تعیین تکلیف شده است و از سند قابل ویرایش نیست.");

                ch.Kind = chequeKind;
                ch.Number = (l.ChequeNumber ?? "").Trim();
                ch.SayadId = Trim(l.SayadId);
                ch.Amount = l.Amount;
                ch.IssueDate = (l.IssueDate ?? dto.Date).Date;
                ch.DueDate = (l.DueDate ?? dto.Date).Date;
                ch.BankName = Trim(l.ChequeBankName);
                ch.BranchName = Trim(l.ChequeBranchName);
                ch.AccountNumber = Trim(l.ChequeAccountNumber);
                ch.PartyId = dto.PartyId is > 0 ? dto.PartyId : null;
                ch.Description = Trim(l.Description) ?? Trim(dto.Description);
                ch.Status = ChequeStatus.InHand;

                // چک صادره روی حساب بانکی ما کشیده می‌شود
                if (chequeKind == ChequeKind.Issued)
                    ch.TrsAccountId = l.TrsAccountId is > 0 ? l.TrsAccountId : ch.TrsAccountId;

                await _db.SaveChangesAsync();

                ch.TrsVoucherId = e.Id > 0 ? e.Id : ch.TrsVoucherId;
                line.ChequeId = ch.Id;
                line.Cheque = ch;
            }
            else if (line.ChequeId is > 0)
            {
                // ابزار سطر از چک به چیز دیگری تغییر کرده
                var old = await _db.TrsCheques.Include(c => c.Actions)
                    .FirstOrDefaultAsync(c => c.Id == line.ChequeId);
                if (old is not null && old.Status == ChequeStatus.InHand)
                {
                    _db.TrsChequeActions.RemoveRange(old.Actions);
                    _db.TrsCheques.Remove(old);
                }
                line.ChequeId = null;
            }
        }

        e.CashAmount = e.Lines.Where(l => l.Method is PayMethod.Cash or PayMethod.Card or PayMethod.Transfer)
            .Sum(l => l.Amount);
        e.ChequeAmount = e.Lines.Where(l => l.Method == PayMethod.Cheque).Sum(l => l.Amount);
        e.DiscountAmount = e.Lines.Where(l => l.Method == PayMethod.Discount).Sum(l => l.Amount);
        e.TotalAmount = e.CashAmount + e.ChequeAmount + e.DiscountAmount;
    }

    // =====================================================================
    // ۴) قطعی، برگشت، ابطال و حذف
    // =====================================================================

    public async Task<TrsVoucher> ConfirmVoucherAsync(int id, string? user)
    {
        // Serializable transaction prevents two parallel confirmations from over-allocating one invoice.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var e = await _db.TrsVouchers
            .Include(v => v.Lines).ThenInclude(l => l.Cheque)
            .Include(v => v.InvoiceAllocations).ThenInclude(a => a.Invoice)
            .FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("سند خزانه یافت نشد.");

        if (e.Status == TreasuryStatus.Confirmed)
            throw new InvalidOperationException("این سند قبلاً قطعی شده است.");
        if (e.Status == TreasuryStatus.Cancelled)
            throw new InvalidOperationException("سند ابطال‌شده قابل قطعی کردن نیست.");

        if (e.Kind != TreasuryKind.Transfer && e.Lines.Count == 0)
            throw new InvalidOperationException("سند بدون سطر قابل قطعی کردن نیست.");
        if (e.TotalAmount <= 0)
            throw new InvalidOperationException("مبلغ سند باید بزرگ‌تر از صفر باشد.");
        if (e.Kind != TreasuryKind.Transfer && e.PartyId is not > 0)
            throw new InvalidOperationException("برای قطعی کردن سند، طرف حساب را مشخص کنید.");

        var allocations = AllocationDtos(e);
        await ValidateInvoiceAllocationsAsync(e.Kind, e.PartyId, allocations, e.TotalAmount, e.Id);

        // برداشت بیش از موجودی صندوق مجاز نیست
        if (e.Kind is TreasuryKind.Payment or TreasuryKind.Transfer)
            await EnsureBalanceAsync(e);

        var rule = e.Kind == TreasuryKind.Transfer
            ? await _db.TrsRules.FirstOrDefaultAsync(r => r.Kind == TreasuryKind.Payment)
            : await _db.TrsRules.FirstOrDefaultAsync(r => r.Kind == e.Kind);

        if (rule is not null && rule.IsActive && rule.AutoVoucher)
            e.VoucherId = await PostVoucherAsync(e, rule, user);

        // چک‌ها با قطعی شدن سند به جریان می‌افتند
        foreach (var l in e.Lines.Where(l => l.Cheque is not null))
        {
            l.Cheque!.TrsVoucherId = e.Id;
            l.Cheque.StatusDate ??= e.Date;
            if (!await _db.TrsChequeActions.AnyAsync(a => a.ChequeId == l.Cheque.Id))
            {
                _db.TrsChequeActions.Add(new Db.TrsChequeAction
                {
                    ChequeId = l.Cheque.Id,
                    Status = ChequeStatus.InHand,
                    Date = e.Date,
                    Description = $"ثبت از {KindTitle(e.Kind)} شماره {e.Number}",
                    CreatedBy = user,
                    CreatedAt = DateTime.Now
                });
            }
        }

        e.Status = TreasuryStatus.Confirmed;
        e.ConfirmedBy = user;
        e.ConfirmedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (await GetVoucherAsync(e.Id))!;
    }

    public async Task<TrsVoucher> UnconfirmVoucherAsync(int id, string? user)
    {
        var e = await _db.TrsVouchers.Include(v => v.Lines).ThenInclude(l => l.Cheque)
            .FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("سند خزانه یافت نشد.");

        if (e.Status != TreasuryStatus.Confirmed)
            throw new InvalidOperationException("فقط سند قطعی به پیش‌نویس برمی‌گردد.");

        await ReverseAsync(e);

        e.Status = TreasuryStatus.Draft;
        e.ConfirmedBy = null;
        e.ConfirmedAt = null;

        await _db.SaveChangesAsync();
        return (await GetVoucherAsync(e.Id))!;
    }

    public async Task<TrsVoucher> CancelVoucherAsync(int id, string? user)
    {
        var e = await _db.TrsVouchers.Include(v => v.Lines).ThenInclude(l => l.Cheque)
            .FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("سند خزانه یافت نشد.");

        if (e.Status == TreasuryStatus.Cancelled)
            throw new InvalidOperationException("این سند قبلاً ابطال شده است.");

        await ReverseAsync(e);

        e.Status = TreasuryStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (await GetVoucherAsync(e.Id))!;
    }

    public async Task DeleteVoucherAsync(int id)
    {
        var e = await _db.TrsVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.Id == id)
                ?? throw new InvalidOperationException("سند خزانه یافت نشد.");

        if (e.Status == TreasuryStatus.Confirmed)
            throw new InvalidOperationException("ابتدا سند را از حالت قطعی خارج کنید.");

        // چک‌های تعیین‌تکلیف‌نشده‌ی این سند نیز حذف می‌شوند
        var chequeIds = e.Lines.Where(l => l.ChequeId is > 0).Select(l => l.ChequeId!.Value).ToList();
        var cheques = await _db.TrsCheques.Include(c => c.Actions)
            .Where(c => chequeIds.Contains(c.Id)).ToListAsync();

        if (cheques.Any(c => c.Status != ChequeStatus.InHand))
            throw new InvalidOperationException("چک‌های این سند تعیین تکلیف شده‌اند؛ ابتدا عملیات چک را برگردانید.");

        foreach (var c in cheques) _db.TrsChequeActions.RemoveRange(c.Actions);
        _db.TrsCheques.RemoveRange(cheques);
        _db.TrsVoucherLines.RemoveRange(e.Lines);
        _db.TrsVouchers.Remove(e);
        await _db.SaveChangesAsync();
    }

    /// <summary>سند حسابداری سند خزانه را حذف و چک‌ها را به وضعیت اولیه برمی‌گرداند.</summary>
    private async Task ReverseAsync(Db.TrsVoucher e)
    {
        if (e.VoucherId is > 0)
        {
            var v = await _db.AccVouchers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == e.VoucherId);
            if (v is not null)
            {
                _db.AccVoucherLines.RemoveRange(v.Lines);
                _db.AccVouchers.Remove(v);
            }
            e.VoucherId = null;
        }

        foreach (var l in e.Lines.Where(l => l.Cheque is not null))
        {
            if (l.Cheque!.Status != ChequeStatus.InHand)
                throw new InvalidOperationException(
                    $"چک شماره {l.Cheque.Number} تعیین تکلیف شده است؛ ابتدا عملیات آن را برگردانید.");

            var actions = await _db.TrsChequeActions.Where(a => a.ChequeId == l.Cheque.Id).ToListAsync();
            _db.TrsChequeActions.RemoveRange(actions);
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>جلوگیری از منفی شدن موجودی صندوق/بانک</summary>
    private async Task EnsureBalanceAsync(Db.TrsVoucher e)
    {
        var affected = new Dictionary<int, decimal>();

        if (e.Kind == TreasuryKind.Transfer && e.FromAccountId is > 0)
            affected[e.FromAccountId.Value] = e.TotalAmount + e.FeeAmount;
        else
            foreach (var l in e.Lines.Where(l => l.TrsAccountId is > 0
                     && l.Method is PayMethod.Cash or PayMethod.Card or PayMethod.Transfer))
                affected[l.TrsAccountId!.Value] = affected.GetValueOrDefault(l.TrsAccountId.Value) + l.Amount;

        if (affected.Count == 0) return;

        var accounts = await GetAccountsAsync(false, true);
        foreach (var (accId, amount) in affected)
        {
            var acc = accounts.FirstOrDefault(a => a.Id == accId);
            if (acc is null) continue;
            if (acc.Balance < amount)
                throw new InvalidOperationException(
                    $"موجودی «{acc.Name}» کافی نیست. مانده: {acc.Balance:N0} — مبلغ درخواستی: {amount:N0}");
        }
    }

    // =====================================================================
    // ۵) سند حسابداری خزانه
    // =====================================================================

    /// <summary>
    /// دریافت: صندوق/بانک + اسناد دریافتنی + تخفیف  بدهکار | دریافتنی طرف حساب بستانکار
    /// پرداخت: پرداختنی طرف حساب بدهکار | صندوق/بانک + اسناد پرداختنی + تخفیف بستانکار
    /// انتقال: حساب مقصد + کارمزد بدهکار | حساب مبدأ بستانکار
    /// </summary>
    private async Task<int?> PostVoucherAsync(Db.TrsVoucher e, Db.TrsRule rule, string? user)
    {
        var title = $"{KindTitle(e.Kind)} شماره {e.Number}";
        var voucher = await NewAccVoucherAsync(e.Date, title, e.RefNumber ?? e.Number.ToString(), user);
        var row = 1;

        if (e.Kind == TreasuryKind.Transfer)
        {
            var fromAcc = await AccountOfAsync(e.FromAccountId);
            var toAcc = await AccountOfAsync(e.ToAccountId);
            if (fromAcc is null || toAcc is null)
                throw new InvalidOperationException("حساب حسابداری صندوق مبدأ یا مقصد تعیین نشده است.");

            voucher.Lines.Add(Line(row++, toAcc.Value, e.TotalAmount, 0, title, e.RefNumber, null));

            if (e.FeeAmount > 0)
            {
                if (rule.FeeAccountId is not > 0)
                    throw new InvalidOperationException("حساب کارمزد بانکی در تنظیمات خزانه تعیین نشده است.");
                voucher.Lines.Add(Line(row++, rule.FeeAccountId.Value, e.FeeAmount, 0,
                    $"کارمزد — {title}", e.RefNumber, null));
            }

            voucher.Lines.Add(Line(row, fromAcc.Value, 0, e.TotalAmount + e.FeeAmount, title, e.RefNumber, null));
        }
        else
        {
            if (rule.PartyAccountId is not > 0)
                throw new InvalidOperationException("حساب طرف حساب در تنظیمات خزانه تعیین نشده است.");

            // در دریافت، صندوق بدهکار و طرف حساب بستانکار می‌شود
            var cashDebit = e.Kind == TreasuryKind.Receipt;

            foreach (var l in e.Lines.OrderBy(l => l.RowNo))
            {
                int accountId;
                string desc;

                switch (l.Method)
                {
                    case PayMethod.Cash:
                    case PayMethod.Card:
                    case PayMethod.Transfer:
                        accountId = await AccountOfAsync(l.TrsAccountId)
                            ?? throw new InvalidOperationException(
                                "برای صندوق/بانک این سطر، حساب حسابداری متناظر تعیین نشده است.");
                        desc = $"{MethodTitle(l.Method)} — {title}";
                        break;

                    case PayMethod.Cheque:
                        if (rule.ChequeAccountId is not > 0)
                            throw new InvalidOperationException(
                                "حساب اسناد دریافتنی/پرداختنی در تنظیمات خزانه تعیین نشده است.");
                        accountId = rule.ChequeAccountId.Value;
                        desc = $"چک شماره {l.Cheque?.Number} — {title}";
                        break;

                    default: // تخفیف و کسورات
                        if (rule.DiscountAccountId is not > 0)
                            throw new InvalidOperationException(
                                "حساب تخفیفات نقدی در تنظیمات خزانه تعیین نشده است.");
                        accountId = rule.DiscountAccountId.Value;
                        desc = $"تخفیف و کسورات — {title}";
                        break;
                }

                voucher.Lines.Add(Line(row++, accountId,
                    cashDebit ? l.Amount : 0,
                    cashDebit ? 0 : l.Amount,
                    desc, l.RefNumber ?? e.RefNumber, e.PartyId));
            }

            // طرف حساب: در تسویهٔ فاکتوری یک آرتیکل به‌ازای هر فاکتور می‌زنیم تا دفتر معین
            // و خود سند حسابداری هم شمارهٔ فاکتور/مبلغ هر تخصیص را نگه دارد.
            var invoiceAllocations = AllocationDtos(e);
            if (invoiceAllocations.Count == 0)
            {
                voucher.Lines.Add(Line(row++, rule.PartyAccountId.Value,
                    cashDebit ? 0 : e.TotalAmount,
                    cashDebit ? e.TotalAmount : 0,
                    title, e.RefNumber, e.PartyId));
            }
            else
            {
                foreach (var allocation in invoiceAllocations.OrderBy(a => a.InvoiceNumber))
                {
                    var invoiceTitle = allocation.InvoiceNumber > 0
                        ? $"{KindTitle(allocation.InvoiceKind)} شماره {allocation.InvoiceNumber}"
                        : "تخصیص فاکتور";
                    voucher.Lines.Add(Line(row++, rule.PartyAccountId.Value,
                        cashDebit ? 0 : allocation.Amount,
                        cashDebit ? allocation.Amount : 0,
                        $"تسویه {invoiceTitle} — {title}",
                        allocation.InvoiceNumber > 0 ? allocation.InvoiceNumber.ToString() : e.RefNumber,
                        e.PartyId));
                }

                var unapplied = e.TotalAmount - invoiceAllocations.Sum(a => a.Amount);
                if (unapplied > 0)
                {
                    voucher.Lines.Add(Line(row++, rule.PartyAccountId.Value,
                        cashDebit ? 0 : unapplied,
                        cashDebit ? unapplied : 0,
                        $"مبلغ تخصیص‌نیافته / پیش‌پرداخت — {title}", e.RefNumber, e.PartyId));
                }
            }
        }

        return await SaveAccVoucherAsync(voucher);
    }

    /// <summary>حساب حسابداری متناظر یک صندوق/بانک</summary>
    private async Task<int?> AccountOfAsync(int? trsAccountId)
    {
        if (trsAccountId is not > 0) return null;
        return await _db.TrsAccounts.Where(a => a.Id == trsAccountId)
            .Select(a => a.AccountId).FirstOrDefaultAsync();
    }

    private async Task<Db.AccVoucher> NewAccVoucherAsync(DateTime date, string title, string? refNumber, string? user)
    {
        var year = await _db.AccFiscalYears.FirstOrDefaultAsync(f => f.IsCurrent)
                   ?? await _db.AccFiscalYears.OrderByDescending(f => f.StartDate).FirstOrDefaultAsync()
                   ?? throw new InvalidOperationException("هیچ سال مالی تعریف نشده است.");

        if (year.IsClosed)
            throw new InvalidOperationException("سال مالی جاری بسته شده است.");

        var maxNumber = await _db.AccVouchers.Where(v => v.FiscalYearId == year.Id)
            .Select(v => (int?)v.Number).MaxAsync() ?? 0;

        return new Db.AccVoucher
        {
            FiscalYearId = year.Id,
            Number = maxNumber + 1,
            Date = date.Date,
            Description = $"سند خودکار — {title}",
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.Manual,
            SourceTitle = title,
            RefNumber = refNumber,
            CreatedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedBy = user,
            ConfirmedAt = DateTime.Now
        };
    }

    private static Db.AccVoucherLine Line(int rowNo, int accountId, decimal debit, decimal credit,
        string? description, string? refNumber, int? partyId) => new()
    {
        RowNo = rowNo,
        AccountId = accountId,
        PartyId = partyId,
        Description = description,
        RefNumber = refNumber,
        Debit = debit,
        Credit = credit
    };

    private async Task<int> SaveAccVoucherAsync(Db.AccVoucher voucher)
    {
        voucher.Lines = voucher.Lines.Where(l => l.Debit != 0 || l.Credit != 0).ToList();

        voucher.TotalDebit = voucher.Lines.Sum(l => l.Debit);
        voucher.TotalCredit = voucher.Lines.Sum(l => l.Credit);

        if (voucher.TotalDebit != voucher.TotalCredit)
            throw new InvalidOperationException(
                $"سند خودکار تراز نیست (بدهکار {voucher.TotalDebit:N0} / بستانکار {voucher.TotalCredit:N0}).");

        _db.AccVouchers.Add(voucher);
        await _db.SaveChangesAsync();
        return voucher.Id;
    }

    // =====================================================================
    // ۶) چک
    // =====================================================================

    public async Task<PagedResult<TrsCheque>> GetChequesAsync(ChequeKind? kind, ChequeStatus? status,
        int? partyId, int? trsAccountId, string? search, DateTime? from, DateTime? to, bool onlyOpen,
        int page, int pageSize)
    {
        var q = _db.TrsCheques.AsNoTracking().AsQueryable();

        if (kind is not null) q = q.Where(c => c.Kind == kind);
        if (status is not null) q = q.Where(c => c.Status == status);
        if (partyId is > 0) q = q.Where(c => c.PartyId == partyId);
        if (trsAccountId is > 0) q = q.Where(c => c.TrsAccountId == trsAccountId);
        if (from is not null) q = q.Where(c => c.DueDate >= from.Value.Date);
        if (to is not null) q = q.Where(c => c.DueDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        if (onlyOpen)
            q = q.Where(c => c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Number.Contains(s)
                             || (c.SayadId ?? "").Contains(s)
                             || (c.BankName ?? "").Contains(s)
                             || (c.OwnerName ?? "").Contains(s)
                             || (c.Party != null && c.Party.Name.Contains(s)));
        }

        var total = await q.CountAsync();

        var rows = await q
            .OrderBy(c => c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection ? 0 : 1)
            .ThenBy(c => c.DueDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new TrsCheque
            {
                Id = c.Id,
                Kind = c.Kind,
                Number = c.Number,
                SayadId = c.SayadId,
                Amount = c.Amount,
                IssueDate = c.IssueDate,
                DueDate = c.DueDate,
                BankName = c.BankName,
                BranchName = c.BranchName,
                AccountNumber = c.AccountNumber,
                OwnerName = c.OwnerName,
                PartyId = c.PartyId,
                PartyName = c.Party != null ? c.Party.Name : null,
                TrsAccountId = c.TrsAccountId,
                TrsAccountName = c.TrsAccount != null ? c.TrsAccount.Name : null,
                Status = c.Status,
                StatusDate = c.StatusDate,
                Description = c.Description,
                TrsVoucherId = c.TrsVoucherId,
                CreatedBy = c.CreatedBy,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        await FillChequeVoucherNumbersAsync(rows);
        return new PagedResult<TrsCheque> { Items = rows, TotalCount = total };
    }

    private async Task FillChequeVoucherNumbersAsync(List<TrsCheque> rows)
    {
        var ids = rows.Where(r => r.TrsVoucherId is > 0).Select(r => r.TrsVoucherId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var map = await _db.TrsVouchers.AsNoTracking().Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Number);

        foreach (var r in rows)
            if (r.TrsVoucherId is > 0 && map.TryGetValue(r.TrsVoucherId.Value, out var n))
                r.TrsVoucherNumber = n;
    }

    public async Task<TrsCheque?> GetChequeAsync(int id)
    {
        var e = await _db.TrsCheques.AsNoTracking()
            .Include(c => c.Party)
            .Include(c => c.TrsAccount)
            .Include(c => c.Actions)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (e is null) return null;

        var accNames = await _db.TrsAccounts.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.Name);

        var accVoucherIds = e.Actions.Where(a => a.VoucherId is > 0).Select(a => a.VoucherId!.Value).ToList();
        var accVoucherNumbers = accVoucherIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.AccVouchers.AsNoTracking().Where(v => accVoucherIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Number);

        var dto = new TrsCheque
        {
            Id = e.Id,
            Kind = e.Kind,
            Number = e.Number,
            SayadId = e.SayadId,
            Amount = e.Amount,
            IssueDate = e.IssueDate,
            DueDate = e.DueDate,
            BankName = e.BankName,
            BranchName = e.BranchName,
            AccountNumber = e.AccountNumber,
            OwnerName = e.OwnerName,
            PartyId = e.PartyId,
            PartyName = e.Party?.Name,
            TrsAccountId = e.TrsAccountId,
            TrsAccountName = e.TrsAccount?.Name,
            Status = e.Status,
            StatusDate = e.StatusDate,
            Description = e.Description,
            TrsVoucherId = e.TrsVoucherId,
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt,
            Actions = e.Actions.OrderBy(a => a.Date).ThenBy(a => a.Id).Select(a => new TrsChequeAction
            {
                Id = a.Id,
                ChequeId = a.ChequeId,
                Status = a.Status,
                Date = a.Date,
                TrsAccountId = a.TrsAccountId,
                TrsAccountName = a.TrsAccountId is > 0 && accNames.ContainsKey(a.TrsAccountId.Value)
                    ? accNames[a.TrsAccountId.Value] : null,
                Description = a.Description,
                VoucherId = a.VoucherId,
                VoucherNumber = a.VoucherId is > 0 && accVoucherNumbers.ContainsKey(a.VoucherId.Value)
                    ? accVoucherNumbers[a.VoucherId.Value] : null,
                CreatedBy = a.CreatedBy,
                CreatedAt = a.CreatedAt
            }).ToList()
        };

        await FillChequeVoucherNumbersAsync(new List<TrsCheque> { dto });
        return dto;
    }

    public async Task<TrsCheque> SaveChequeAsync(TrsCheque dto, string? user)
    {
        if (string.IsNullOrWhiteSpace(dto.Number))
            throw new InvalidOperationException("شماره چک الزامی است.");
        if (dto.Amount <= 0)
            throw new InvalidOperationException("مبلغ چک باید بزرگ‌تر از صفر باشد.");
        if (dto.Kind == ChequeKind.Issued && dto.TrsAccountId is not > 0)
            throw new InvalidOperationException("برای چک صادره، حساب بانکی صادرکننده را انتخاب کنید.");

        var e = dto.Id > 0
            ? await _db.TrsCheques.FirstOrDefaultAsync(c => c.Id == dto.Id)
              ?? throw new InvalidOperationException("چک یافت نشد.")
            : new Db.TrsCheque { CreatedBy = user, CreatedAt = DateTime.Now };

        if (dto.Id > 0 && e.Status is not (ChequeStatus.InHand or ChequeStatus.InCollection))
            throw new InvalidOperationException("چک تعیین تکلیف شده قابل ویرایش نیست.");

        e.Kind = dto.Kind;
        e.Number = dto.Number.Trim();
        e.SayadId = Trim(dto.SayadId);
        e.Amount = dto.Amount;
        e.IssueDate = dto.IssueDate.Date;
        e.DueDate = dto.DueDate.Date;
        e.BankName = Trim(dto.BankName);
        e.BranchName = Trim(dto.BranchName);
        e.AccountNumber = Trim(dto.AccountNumber);
        e.OwnerName = Trim(dto.OwnerName);
        e.PartyId = dto.PartyId is > 0 ? dto.PartyId : null;
        e.TrsAccountId = dto.TrsAccountId is > 0 ? dto.TrsAccountId : null;
        e.Description = Trim(dto.Description);

        if (dto.Id == 0)
        {
            e.Status = ChequeStatus.InHand;
            e.StatusDate = dto.IssueDate.Date;
            _db.TrsCheques.Add(e);
            await _db.SaveChangesAsync();

            _db.TrsChequeActions.Add(new Db.TrsChequeAction
            {
                ChequeId = e.Id,
                Status = ChequeStatus.InHand,
                Date = e.IssueDate,
                Description = "ثبت دستی چک",
                CreatedBy = user,
                CreatedAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();
        return (await GetChequeAsync(e.Id))!;
    }

    /// <summary>
    /// اجرای یک عملیات روی چک (واگذاری، وصول، برگشت، خرج، ابطال) به‌همراه سند حسابداری آن.
    /// </summary>
    public async Task<TrsCheque> RunChequeActionAsync(TrsChequeCommand cmd, string? user)
    {
        var e = await _db.TrsCheques.FirstOrDefaultAsync(c => c.Id == cmd.ChequeId)
                ?? throw new InvalidOperationException("چک یافت نشد.");

        EnsureTransition(e.Kind, e.Status, cmd.Status);

        var rule = await _db.TrsRules.FirstOrDefaultAsync(
            r => r.Kind == (e.Kind == ChequeKind.Received ? TreasuryKind.Receipt : TreasuryKind.Payment));

        var needsBank = cmd.Status is ChequeStatus.InCollection
                        || (cmd.Status == ChequeStatus.Cleared && e.Kind == ChequeKind.Received);

        if (needsBank && cmd.TrsAccountId is not > 0 && e.TrsAccountId is not > 0)
            throw new InvalidOperationException("حساب بانکی این عملیات را انتخاب کنید.");

        var bankId = cmd.TrsAccountId is > 0 ? cmd.TrsAccountId : e.TrsAccountId;

        int? accVoucherId = null;
        if (rule is not null && rule.IsActive && rule.AutoVoucher)
            accVoucherId = await PostChequeActionAsync(e, cmd, rule, bankId, user);

        e.Status = cmd.Status;
        e.StatusDate = cmd.Date.Date;
        if (bankId is > 0) e.TrsAccountId = bankId;

        _db.TrsChequeActions.Add(new Db.TrsChequeAction
        {
            ChequeId = e.Id,
            Status = cmd.Status,
            Date = cmd.Date.Date,
            TrsAccountId = bankId,
            Description = Trim(cmd.Description),
            VoucherId = accVoucherId,
            CreatedBy = user,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync();
        return (await GetChequeAsync(e.Id))!;
    }

    /// <summary>گذارهای مجاز چرخه عمر چک</summary>
    private static void EnsureTransition(ChequeKind kind, ChequeStatus current, ChequeStatus target)
    {
        if (current == target)
            throw new InvalidOperationException("چک هم‌اکنون در همین وضعیت است.");

        if (current is ChequeStatus.Cleared or ChequeStatus.Endorsed or ChequeStatus.Cancelled)
            throw new InvalidOperationException("این چک تعیین تکلیف نهایی شده است و عملیات جدید نمی‌پذیرد.");

        var allowed = kind == ChequeKind.Received
            ? current switch
            {
                ChequeStatus.InHand => new[]
                {
                    ChequeStatus.InCollection, ChequeStatus.Cleared,
                    ChequeStatus.Endorsed, ChequeStatus.Cancelled
                },
                ChequeStatus.InCollection => new[] { ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.InHand },
                ChequeStatus.Bounced => new[] { ChequeStatus.InCollection, ChequeStatus.Cleared, ChequeStatus.Cancelled },
                _ => Array.Empty<ChequeStatus>()
            }
            : current switch
            {
                // چک صادره: خرج شدن معنا ندارد
                ChequeStatus.InHand => new[] { ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled },
                ChequeStatus.Bounced => new[] { ChequeStatus.Cleared, ChequeStatus.Cancelled },
                _ => Array.Empty<ChequeStatus>()
            };

        if (!allowed.Contains(target))
            throw new InvalidOperationException("این تغییر وضعیت برای چک مجاز نیست.");
    }

    /// <summary>
    /// واگذاری (دریافتی): در جریان وصول بدهکار | اسناد دریافتنی بستانکار
    /// وصول   (دریافتی): بانک بدهکار | در جریان وصول (یا اسناد دریافتنی) بستانکار
    /// وصول   (صادره)  : اسناد پرداختنی بدهکار | بانک بستانکار
    /// برگشت  (دریافتی): دریافتنی طرف حساب بدهکار | در جریان وصول/اسناد دریافتنی بستانکار
    /// برگشت  (صادره)  : اسناد پرداختنی بدهکار | پرداختنی طرف حساب بستانکار
    /// واگذاری مجددِ چک برگشتی: دریافتنی طرف حساب بستانکار | در جریان وصول بدهکار
    /// خرج    (دریافتی): پرداختنی طرف حساب بدهکار | اسناد دریافتنی بستانکار
    /// ابطال            : عکس ثبت اولیه؛ ابطالِ چک برگشتی اثر مالی تازه ندارد
    /// </summary>
    private async Task<int?> PostChequeActionAsync(Db.TrsCheque e, TrsChequeCommand cmd, Db.TrsRule rule,
        int? bankId, string? user)
    {
        if (rule.ChequeAccountId is not > 0)
            throw new InvalidOperationException("حساب اسناد دریافتنی/پرداختنی در تنظیمات خزانه تعیین نشده است.");

        var chequeAcc = rule.ChequeAccountId.Value;
        var collectionAcc = rule.CollectionAccountId ?? chequeAcc;
        var received = e.Kind == ChequeKind.Received;

        var title = $"{(received ? "چک دریافتی" : "چک پرداختی")} شماره {e.Number}";
        var actionTitle = cmd.Status switch
        {
            ChequeStatus.InCollection => "واگذاری به بانک",
            ChequeStatus.Cleared => received ? "وصول" : "پاس شدن",
            ChequeStatus.Bounced => "برگشت",
            ChequeStatus.Endorsed => "خرج چک",
            ChequeStatus.Cancelled => "ابطال",
            _ => "برگشت به صندوق"
        };
        var full = $"{actionTitle} — {title}";

        // A bounced cheque already reopened the party balance; cancelling it afterwards is only a status close.
        if (cmd.Status == ChequeStatus.Cancelled && e.Status == ChequeStatus.Bounced)
            return null;

        var voucher = await NewAccVoucherAsync(cmd.Date, full, e.Number, user);
        var row = 1;

        // حساب بانکی حسابداریِ عملیات
        async Task<int> BankAccAsync()
        {
            var id = await AccountOfAsync(bankId);
            if (id is not > 0)
                throw new InvalidOperationException("برای بانک انتخاب‌شده، حساب حسابداری متناظر تعیین نشده است.");
            return id.Value;
        }

        int PartyAcc()
        {
            if (rule.PartyAccountId is not > 0)
                throw new InvalidOperationException("حساب طرف حساب در تنظیمات خزانه تعیین نشده است.");
            return rule.PartyAccountId.Value;
        }

        switch (cmd.Status)
        {
            case ChequeStatus.InCollection when received && e.Status == ChequeStatus.Bounced:
                // Re-lodging a bounced customer cheque restores the party settlement and moves it to collection.
                voucher.Lines.Add(Line(row++, collectionAcc, e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, PartyAcc(), 0, e.Amount, full, e.Number, e.PartyId));
                break;

            case ChequeStatus.InCollection:
                voucher.Lines.Add(Line(row++, collectionAcc, e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, chequeAcc, 0, e.Amount, full, e.Number, e.PartyId));
                break;

            case ChequeStatus.Cleared when received && e.Status == ChequeStatus.Bounced:
            {
                // Direct re-clear after a bounce: restore the party settlement against the bank.
                voucher.Lines.Add(Line(row++, await BankAccAsync(), e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, PartyAcc(), 0, e.Amount, full, e.Number, e.PartyId));
                break;
            }

            case ChequeStatus.Cleared when received:
            {
                var source = e.Status == ChequeStatus.InCollection ? collectionAcc : chequeAcc;
                voucher.Lines.Add(Line(row++, await BankAccAsync(), e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, source, 0, e.Amount, full, e.Number, e.PartyId));
                break;
            }

            case ChequeStatus.Cleared when !received && e.Status == ChequeStatus.Bounced:
                // Direct re-clear after a returned issued cheque settles the reopened payable against the bank.
                voucher.Lines.Add(Line(row++, PartyAcc(), e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, await BankAccAsync(), 0, e.Amount, full, e.Number, e.PartyId));
                break;

            case ChequeStatus.Cleared: // چک صادره پاس شد
                voucher.Lines.Add(Line(row++, chequeAcc, e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, await BankAccAsync(), 0, e.Amount, full, e.Number, e.PartyId));
                break;

            case ChequeStatus.Bounced when received:
            {
                var source = e.Status == ChequeStatus.InCollection ? collectionAcc : chequeAcc;
                voucher.Lines.Add(Line(row++, PartyAcc(), e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, source, 0, e.Amount, full, e.Number, e.PartyId));
                break;
            }

            case ChequeStatus.Bounced: // چک صادره برگشت خورد — بدهی دوباره برقرار می‌شود
                voucher.Lines.Add(Line(row++, chequeAcc, e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, PartyAcc(), 0, e.Amount, full, e.Number, e.PartyId));
                break;

            case ChequeStatus.Endorsed:
                voucher.Lines.Add(Line(row++, PartyAcc(), e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, chequeAcc, 0, e.Amount, full, e.Number, e.PartyId));
                break;

            case ChequeStatus.Cancelled:
            {
                var source = e.Status == ChequeStatus.InCollection ? collectionAcc : chequeAcc;
                if (received)
                {
                    voucher.Lines.Add(Line(row++, PartyAcc(), e.Amount, 0, full, e.Number, e.PartyId));
                    voucher.Lines.Add(Line(row, source, 0, e.Amount, full, e.Number, e.PartyId));
                }
                else
                {
                    voucher.Lines.Add(Line(row++, source, e.Amount, 0, full, e.Number, e.PartyId));
                    voucher.Lines.Add(Line(row, PartyAcc(), 0, e.Amount, full, e.Number, e.PartyId));
                }
                break;
            }

            case ChequeStatus.InHand: // برگشت از جریان وصول به صندوق
                voucher.Lines.Add(Line(row++, chequeAcc, e.Amount, 0, full, e.Number, e.PartyId));
                voucher.Lines.Add(Line(row, collectionAcc, 0, e.Amount, full, e.Number, e.PartyId));
                break;
        }

        if (voucher.Lines.Count < 2) return null;
        return await SaveAccVoucherAsync(voucher);
    }

    public async Task DeleteChequeAsync(int id)
    {
        var e = await _db.TrsCheques.Include(c => c.Actions).FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("چک یافت نشد.");

        if (e.Status != ChequeStatus.InHand)
            throw new InvalidOperationException("فقط چکی که هنوز تعیین تکلیف نشده قابل حذف است.");

        if (await _db.TrsVoucherLines.AnyAsync(l => l.ChequeId == id))
            throw new InvalidOperationException("این چک به یک سند خزانه متصل است؛ آن را از سند حذف کنید.");

        _db.TrsChequeActions.RemoveRange(e.Actions);
        _db.TrsCheques.Remove(e);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // ۷) قواعد
    // =====================================================================

    public async Task<List<TrsRule>> GetRulesAsync(Paging.Request? pagination = null)
    {
        var kinds = new[] { TreasuryKind.Receipt, TreasuryKind.Payment };
        var rows = await _db.TrsRules.ToListAsync();

        // اگر قاعده‌ای نبود، غیرفعال ساخته می‌شود تا کاربر آن را تکمیل کند
        foreach (var k in kinds)
        {
            if (rows.Any(r => r.Kind == k)) continue;
            var created = new Db.TrsRule { Kind = k, IsActive = false, AutoVoucher = true };
            _db.TrsRules.Add(created);
            rows.Add(created);
        }
        await _db.SaveChangesAsync();
        rows = await _db.TrsRules.AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.Id).ToPageListAsync(pagination);

        var accounts = await _db.AccAccounts.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.Code + " — " + a.Name);

        string? Name(int? id) => id is > 0 && accounts.ContainsKey(id.Value) ? accounts[id.Value] : null;

        return rows.OrderBy(r => r.Kind).Select(r => new TrsRule
        {
            Id = r.Id,
            Kind = r.Kind,
            PartyAccountId = r.PartyAccountId,
            PartyAccountName = Name(r.PartyAccountId),
            ChequeAccountId = r.ChequeAccountId,
            ChequeAccountName = Name(r.ChequeAccountId),
            CollectionAccountId = r.CollectionAccountId,
            CollectionAccountName = Name(r.CollectionAccountId),
            DiscountAccountId = r.DiscountAccountId,
            DiscountAccountName = Name(r.DiscountAccountId),
            FeeAccountId = r.FeeAccountId,
            FeeAccountName = Name(r.FeeAccountId),
            AutoVoucher = r.AutoVoucher,
            IsActive = r.IsActive,
            Description = r.Description
        }).ToList();
    }

    public async Task<TrsRule> SaveRuleAsync(TrsRule dto)
    {
        var e = await _db.TrsRules.FirstOrDefaultAsync(r => r.Kind == dto.Kind);
        if (e is null)
        {
            e = new Db.TrsRule { Kind = dto.Kind };
            _db.TrsRules.Add(e);
        }

        if (dto.IsActive && dto.AutoVoucher && dto.PartyAccountId is not > 0)
            throw new InvalidOperationException("برای فعال کردن سند خودکار، حساب طرف حساب الزامی است.");

        e.PartyAccountId = dto.PartyAccountId is > 0 ? dto.PartyAccountId : null;
        e.ChequeAccountId = dto.ChequeAccountId is > 0 ? dto.ChequeAccountId : null;
        e.CollectionAccountId = dto.CollectionAccountId is > 0 ? dto.CollectionAccountId : null;
        e.DiscountAccountId = dto.DiscountAccountId is > 0 ? dto.DiscountAccountId : null;
        e.FeeAccountId = dto.FeeAccountId is > 0 ? dto.FeeAccountId : null;
        e.AutoVoucher = dto.AutoVoucher;
        e.IsActive = dto.IsActive;
        e.Description = Trim(dto.Description);

        await _db.SaveChangesAsync();

        var all = await GetRulesAsync();
        return all.First(r => r.Kind == dto.Kind);
    }

    // =====================================================================
    // ۸) گزارش‌ها
    // =====================================================================

    public async Task<TrsFlowResult> GetFlowAsync(int trsAccountId, DateTime? from, DateTime? to)
    {
        var acc = await _db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == trsAccountId)
                  ?? throw new InvalidOperationException("صندوق/بانک یافت نشد.");

        var result = new TrsFlowResult
        {
            TrsAccountId = acc.Id,
            AccountName = acc.Name,
            AccountKindTitle = acc.Kind switch
            {
                TreasuryAccountKind.Cash => "صندوق",
                TreasuryAccountKind.Bank => "بانک",
                TreasuryAccountKind.Pos => "کارتخوان",
                _ => "تنخواه‌گردان"
            },
            From = from,
            To = to
        };

        var all = await CollectFlowAsync(trsAccountId);

        // مانده ابتدای بازه
        var opening = acc.OpeningBalance;
        if (from is not null)
            opening += all.Where(r => r.Date < from.Value.Date).Sum(r => r.In - r.Out);

        var rows = all
            .Where(r => (from is null || r.Date >= from.Value.Date)
                        && (to is null || r.Date <= to.Value.Date))
            .OrderBy(r => r.Date).ThenBy(r => r.Number)
            .ToList();

        var running = opening;
        foreach (var r in rows)
        {
            running += r.In - r.Out;
            r.Balance = running;
        }

        result.OpeningBalance = opening;
        result.Rows = rows;
        result.TotalIn = rows.Sum(r => r.In);
        result.TotalOut = rows.Sum(r => r.Out);
        result.ClosingBalance = running;
        return result;
    }

    /// <summary>همه‌ی گردش‌های یک حساب: سطرهای اسناد، انتقال‌ها و وصول چک‌ها</summary>
    private async Task<List<TrsFlowRow>> CollectFlowAsync(int accountId)
    {
        var rows = new List<TrsFlowRow>();

        // سطرهای نقد/کارت/حواله
        var lines = await _db.TrsVoucherLines.AsNoTracking()
            .Where(l => l.TrsAccountId == accountId && l.TrsVoucher!.Status == TreasuryStatus.Confirmed
                        && l.Method != PayMethod.Cheque && l.Method != PayMethod.Discount)
            .Select(l => new
            {
                l.TrsVoucher!.Id,
                l.TrsVoucher.Number,
                l.TrsVoucher.Kind,
                l.TrsVoucher.Date,
                l.TrsVoucher.Description,
                PartyName = l.TrsVoucher.Party != null ? l.TrsVoucher.Party.Name : null,
                l.Method,
                l.Amount,
                l.RefNumber
            })
            .ToListAsync();

        foreach (var l in lines)
        {
            var isIn = l.Kind == TreasuryKind.Receipt;
            rows.Add(new TrsFlowRow
            {
                Date = l.Date,
                TrsVoucherId = l.Id,
                Number = l.Number,
                Kind = l.Kind,
                Method = l.Method switch
                {
                    PayMethod.Cash => "نقد",
                    PayMethod.Card => "کارتخوان",
                    _ => "حواله بانکی"
                },
                PartyName = l.PartyName,
                Description = l.Description,
                RefNumber = l.RefNumber,
                In = isIn ? l.Amount : 0,
                Out = isIn ? 0 : l.Amount
            });
        }

        // انتقال بین حساب‌ها
        var transfers = await _db.TrsVouchers.AsNoTracking()
            .Where(v => v.Kind == TreasuryKind.Transfer && v.Status == TreasuryStatus.Confirmed
                        && (v.FromAccountId == accountId || v.ToAccountId == accountId))
            .Select(v => new
            {
                v.Id, v.Number, v.Date, v.Description, v.RefNumber,
                v.FromAccountId, v.ToAccountId, v.TotalAmount, v.FeeAmount,
                FromName = v.FromAccount != null ? v.FromAccount.Name : null,
                ToName = v.ToAccount != null ? v.ToAccount.Name : null
            })
            .ToListAsync();

        foreach (var t in transfers)
        {
            var isIn = t.ToAccountId == accountId;
            rows.Add(new TrsFlowRow
            {
                Date = t.Date,
                TrsVoucherId = t.Id,
                Number = t.Number,
                Kind = TreasuryKind.Transfer,
                Method = "انتقال",
                PartyName = isIn ? t.FromName : t.ToName,
                Description = t.Description ?? (isIn ? $"انتقال از {t.FromName}" : $"انتقال به {t.ToName}"),
                RefNumber = t.RefNumber,
                In = isIn ? t.TotalAmount : 0,
                Out = isIn ? 0 : t.TotalAmount + t.FeeAmount
            });
        }

        // چک‌های وصول‌شده
        var cheques = await _db.TrsCheques.AsNoTracking()
            .Where(c => c.TrsAccountId == accountId && c.Status == ChequeStatus.Cleared)
            .Select(c => new
            {
                c.Id, c.Number, c.Kind, c.Amount, c.StatusDate, c.DueDate,
                PartyName = c.Party != null ? c.Party.Name : null
            })
            .ToListAsync();

        foreach (var c in cheques)
        {
            var isIn = c.Kind == ChequeKind.Received;
            rows.Add(new TrsFlowRow
            {
                Date = (c.StatusDate ?? c.DueDate).Date,
                TrsVoucherId = 0,
                Number = 0,
                Kind = isIn ? TreasuryKind.Receipt : TreasuryKind.Payment,
                Method = "چک",
                PartyName = c.PartyName,
                Description = $"وصول چک شماره {c.Number}",
                RefNumber = c.Number,
                In = isIn ? c.Amount : 0,
                Out = isIn ? 0 : c.Amount
            });
        }

        return rows;
    }

    public async Task<TrsDashboard> GetDashboardAsync(DateTime? from, DateTime? to)
    {
        var d = new TrsDashboard();

        d.Accounts = await GetAccountsAsync(true, true);
        d.TotalBalance = d.Accounts.Sum(a => a.Balance);
        d.TotalCashBalance = d.Accounts
            .Where(a => a.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash)
            .Sum(a => a.Balance);
        d.TotalBankBalance = d.Accounts
            .Where(a => a.Kind is TreasuryAccountKind.Bank or TreasuryAccountKind.Pos)
            .Sum(a => a.Balance);

        // ---------- گردش دوره ----------
        var q = _db.TrsVouchers.AsNoTracking().AsQueryable();
        if (from is not null) q = q.Where(v => v.Date >= from.Value.Date);
        if (to is not null) q = q.Where(v => v.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        var confirmed = q.Where(v => v.Status == TreasuryStatus.Confirmed);

        d.PeriodIn = await confirmed.Where(v => v.Kind == TreasuryKind.Receipt)
            .SumAsync(v => (decimal?)v.TotalAmount) ?? 0;
        d.PeriodOut = await confirmed.Where(v => v.Kind == TreasuryKind.Payment)
            .SumAsync(v => (decimal?)v.TotalAmount) ?? 0;
        d.ReceiptCount = await confirmed.CountAsync(v => v.Kind == TreasuryKind.Receipt);
        d.PaymentCount = await confirmed.CountAsync(v => v.Kind == TreasuryKind.Payment);
        d.DraftCount = await q.CountAsync(v => v.Status == TreasuryStatus.Draft);

        // ---------- چک‌ها ----------
        var open = _db.TrsCheques.AsNoTracking()
            .Where(c => c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection);

        d.ReceivedChequeTotal = await open.Where(c => c.Kind == ChequeKind.Received)
            .SumAsync(c => (decimal?)c.Amount) ?? 0;
        d.IssuedChequeTotal = await open.Where(c => c.Kind == ChequeKind.Issued)
            .SumAsync(c => (decimal?)c.Amount) ?? 0;
        d.ReceivedChequeCount = await open.CountAsync(c => c.Kind == ChequeKind.Received);
        d.IssuedChequeCount = await open.CountAsync(c => c.Kind == ChequeKind.Issued);

        var today = DateTime.Now.Date;
        d.OverdueChequeCount = await open.CountAsync(c => c.DueDate < today);
        d.OverdueChequeTotal = await open.Where(c => c.DueDate < today)
            .SumAsync(c => (decimal?)c.Amount) ?? 0;

        var horizon = today.AddDays(30);
        d.UpcomingCheques = await open
            .Where(c => c.DueDate <= horizon)
            .OrderBy(c => c.DueDate)
            .Take(15)
            .Select(c => new TrsCheque
            {
                Id = c.Id,
                Kind = c.Kind,
                Number = c.Number,
                Amount = c.Amount,
                IssueDate = c.IssueDate,
                DueDate = c.DueDate,
                BankName = c.BankName,
                PartyId = c.PartyId,
                PartyName = c.Party != null ? c.Party.Name : null,
                TrsAccountId = c.TrsAccountId,
                TrsAccountName = c.TrsAccount != null ? c.TrsAccount.Name : null,
                Status = c.Status
            })
            .ToListAsync();

        return d;
    }

    // =====================================================================
    // کمکی
    // =====================================================================

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string MethodTitle(PayMethod m) => m switch
    {
        PayMethod.Cash => "نقد",
        PayMethod.Card => "کارتخوان",
        PayMethod.Transfer => "حواله بانکی",
        PayMethod.Cheque => "چک",
        _ => "تخفیف و کسورات"
    };

    private static string KindTitle(TreasuryKind k) => k switch
    {
        TreasuryKind.Receipt => "سند دریافت",
        TreasuryKind.Payment => "سند پرداخت",
        _ => "انتقال بین حساب‌ها"
    };
}
