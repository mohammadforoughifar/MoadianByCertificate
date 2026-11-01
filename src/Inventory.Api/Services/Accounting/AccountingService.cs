using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Accounting;

/// <summary>
/// پیاده‌سازی سرویس حسابداری.
///
/// قواعد کلیدی:
///   • سند فقط وقتی قطعی می‌شود که تراز باشد (جمع بدهکار = جمع بستانکار) و حداقل دو آرتیکل داشته باشد.
///   • فقط اسناد «قطعی» در دفاتر، تراز و مانده‌ها دیده می‌شوند.
///   • شماره سند در هر سال مالی از ۱ شروع می‌شود و پیوسته است.
///   • ثبت سند فقط روی حساب‌های «قابل ثبت» (IsPostable) و در سال مالیِ باز مجاز است.
///   • با قطعی شدن سند انبار، در صورت وجود قاعده‌ی فعال، سند حسابداری خودکار صادر می‌شود.
/// </summary>
public class AccountingService : IAccountingService
{
    private readonly Db.AppDbContext _db;

    public AccountingService(Db.AppDbContext db) => _db = db;

    // =====================================================================
    // ۱) سال مالی
    // =====================================================================

    public async Task<List<AccFiscalYear>> GetFiscalYearsAsync(Paging.Request? pagination = null)
    {
        var years = await _db.AccFiscalYears.AsNoTracking()
            .OrderByDescending(f => f.StartDate).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var stats = (await _db.AccVouchers.AsNoTracking()
                .Where(v => v.Status == VoucherStatus.Confirmed && years.Select(f => f.Id).Contains(v.FiscalYearId))
                .Select(v => new { v.FiscalYearId, v.TotalDebit, v.TotalCredit })
                .ToListAsync())
            .GroupBy(v => v.FiscalYearId)
            .Select(g => new { Id = g.Key, Count = g.Count(), D = g.Sum(x => x.TotalDebit), C = g.Sum(x => x.TotalCredit) })
            .ToList();

        return years.Select(f =>
        {
            var st = stats.FirstOrDefault(s => s.Id == f.Id);
            return new AccFiscalYear
            {
                Id = f.Id,
                Title = f.Title,
                Code = f.Code,
                StartDate = f.StartDate,
                EndDate = f.EndDate,
                IsCurrent = f.IsCurrent,
                IsClosed = f.IsClosed,
                Description = f.Description,
                VoucherCount = st?.Count ?? 0,
                TotalDebit = st?.D ?? 0,
                TotalCredit = st?.C ?? 0
            };
        }).ToList();
    }

    public async Task<AccFiscalYear> SaveFiscalYearAsync(AccFiscalYear dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان سال مالی الزامی است.");
        if (dto.EndDate <= dto.StartDate)
            throw new InvalidOperationException("تاریخ پایان باید بعد از تاریخ شروع باشد.");

        var dup = await _db.AccFiscalYears.AnyAsync(f => f.Title == dto.Title && f.Id != dto.Id);
        if (dup) throw new InvalidOperationException("سال مالی با این عنوان قبلاً ثبت شده است.");

        Db.AccFiscalYear entity;
        if (dto.Id == 0)
        {
            entity = new Db.AccFiscalYear();
            _db.AccFiscalYears.Add(entity);
        }
        else
        {
            entity = await _db.AccFiscalYears.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("سال مالی یافت نشد.");
        }

        entity.Title = dto.Title.Trim();
        entity.Code = dto.Code?.Trim();
        entity.StartDate = dto.StartDate.Date;
        entity.EndDate = dto.EndDate.Date;
        entity.IsClosed = dto.IsClosed;
        entity.Description = dto.Description;

        await _db.SaveChangesAsync();

        if (dto.IsCurrent) await SetCurrentFiscalYearAsync(entity.Id);
        else if (!await _db.AccFiscalYears.AnyAsync(f => f.IsCurrent)) await SetCurrentFiscalYearAsync(entity.Id);

        dto.Id = entity.Id;
        return dto;
    }

    public async Task SetCurrentFiscalYearAsync(int id)
    {
        var all = await _db.AccFiscalYears.ToListAsync();
        foreach (var f in all) f.IsCurrent = f.Id == id;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteFiscalYearAsync(int id)
    {
        var f = await _db.AccFiscalYears.FindAsync(id)
                ?? throw new InvalidOperationException("سال مالی یافت نشد.");
        if (await _db.AccVouchers.AnyAsync(v => v.FiscalYearId == id))
            throw new InvalidOperationException("برای این سال مالی سند ثبت شده است؛ امکان حذف وجود ندارد.");

        _db.AccFiscalYears.Remove(f);
        await _db.SaveChangesAsync();
    }

    private async Task<Db.AccFiscalYear> CurrentYearAsync()
        => await _db.AccFiscalYears.FirstOrDefaultAsync(f => f.IsCurrent)
           ?? await _db.AccFiscalYears.OrderByDescending(f => f.StartDate).FirstOrDefaultAsync()
           ?? throw new InvalidOperationException("هیچ سال مالی تعریف نشده است. ابتدا یک سال مالی بسازید.");

    // =====================================================================
    // ۲) کدینگ حساب‌ها
    // =====================================================================

    public Task<List<AccAccount>> GetAccountsFlatAsync(bool activeOnly = false, bool withBalances = false, Paging.Request? pagination = null)
        => BuildAccountsAsync(activeOnly, withBalances, pagination, tree: false);

    private async Task<List<AccAccount>> BuildAccountsAsync(bool activeOnly, bool withBalances, Paging.Request? pagination, bool tree = true)
    {
        var q = _db.AccAccounts.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(a => a.IsActive);

        IOrderedQueryable<Db.AccAccount> Order(IQueryable<Db.AccAccount> source) => source.OrderBy(a => a.Code).ThenBy(a => a.Id);
        var all = tree
            ? await Order(q).ReadTreePageAsync(
                Order(q.Where(a => a.ParentId == null || !q.Any(p => p.Id == a.ParentId))),
                ids => Order(q.Where(a => a.ParentId != null && ids.Contains(a.ParentId.Value))),
                a => a.Id, pagination)
            : await Order(q).ToPageListAsync(pagination);
        var selectedIds = all.Select(a => a.Id).ToHashSet();
        if (!tree && pagination?.IsPaged == true)
        {
            var seen = new HashSet<int>(selectedIds);
            // FullPath and Depth require ancestors, not the entire account chart.
            var frontier = all.Where(a => a.ParentId.HasValue).Select(a => a.ParentId!.Value).Distinct().ToArray();
            while (frontier.Length > 0)
            {
                var parents = new List<Db.AccAccount>();
                foreach (var batch in frontier.Where(id => !seen.Contains(id)).Chunk(500))
                    parents.AddRange(await q.Where(a => batch.Contains(a.Id)).ToListAsync());
                var added = parents.Where(a => seen.Add(a.Id)).ToList();
                all.AddRange(added);
                frontier = added.Where(a => a.ParentId.HasValue).Select(a => a.ParentId!.Value).Distinct().ToArray();
            }
            if (withBalances)
            {
                // Only selected accounts' descendants contribute to their rollups.
                // Ancestors included above must not suppress traversal of a selected subtree.
                var traversed = new HashSet<int>();
                frontier = selectedIds.ToArray();
                while (frontier.Length > 0)
                {
                    var children = new List<Db.AccAccount>();
                    foreach (var batch in frontier.Where(traversed.Add).Chunk(500))
                        children.AddRange(await q.Where(a => a.ParentId != null && batch.Contains(a.ParentId.Value)).ToListAsync());
                    all.AddRange(children.Where(a => seen.Add(a.Id)));
                    frontier = children.Select(a => a.Id).Where(id => !traversed.Contains(id)).Distinct().ToArray();
                }
            }
        }
        var accountIds = all.Select(a => a.Id).ToList();
        var byId = all.ToDictionary(a => a.Id);
        var childCount = await q.Where(a => a.ParentId != null && accountIds.Contains(a.ParentId.Value))
            .GroupBy(a => a.ParentId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count);

        // ---------- گردش حساب‌های قابل ثبت (فقط اسناد قطعی) ----------
        Dictionary<int, (decimal D, decimal C)> turnover = new();
        if (withBalances)
        {
            turnover = (await _db.AccVoucherLines.AsNoTracking()
                    .Where(l => l.Voucher!.Status == VoucherStatus.Confirmed && accountIds.Contains(l.AccountId))
                    .Select(l => new { l.AccountId, l.Debit, l.Credit })
                    .ToListAsync())
                .GroupBy(x => x.AccountId)
                .ToDictionary(g => g.Key, g => (D: g.Sum(x => x.Debit), C: g.Sum(x => x.Credit)));
        }

        string PathOf(Db.AccAccount a)
        {
            var parts = new List<string> { a.Name };
            var cur = a;
            while (cur.ParentId is not null && byId.TryGetValue(cur.ParentId.Value, out var p))
            {
                parts.Insert(0, p.Name);
                cur = p;
            }
            return string.Join(" ← ", parts);
        }

        int DepthOf(Db.AccAccount a)
        {
            var d = 0;
            var cur = a;
            while (cur.ParentId is not null && byId.TryGetValue(cur.ParentId.Value, out var p)) { d++; cur = p; }
            return d;
        }

        var result = all.Select(a => new AccAccount
        {
            Id = a.Id,
            Code = a.Code,
            Name = a.Name,
            EnName = a.EnName,
            ParentId = a.ParentId,
            ParentName = a.ParentId is not null && byId.TryGetValue(a.ParentId.Value, out var pp) ? pp.Name : null,
            Level = a.Level,
            Type = a.Type,
            Nature = a.Nature,
            IsPermanent = a.IsPermanent,
            IsPostable = a.IsPostable,
            RequiresParty = a.RequiresParty,
            IsSystem = a.IsSystem,
            IsActive = a.IsActive,
            SortOrder = a.SortOrder,
            Description = a.Description,
            Depth = DepthOf(a),
            FullPath = PathOf(a),
            ChildCount = childCount.TryGetValue(a.Id, out var cc) ? cc : 0,
            Debit = turnover.TryGetValue(a.Id, out var t) ? t.D : 0,
            Credit = turnover.TryGetValue(a.Id, out var t2) ? t2.C : 0
        }).ToList();

        // ---------- تجمیع گردش از پایین به بالا ----------
        if (withBalances)
        {
            var map = result.ToDictionary(a => a.Id);
            foreach (var node in result.OrderByDescending(a => a.Depth))
            {
                if (node.ParentId is not null && map.TryGetValue(node.ParentId.Value, out var parent))
                {
                    parent.Debit += node.Debit;
                    parent.Credit += node.Credit;
                }
            }
        }

        return result.Where(a => selectedIds.Contains(a.Id)).OrderBy(a => a.Code, StringComparer.Ordinal).ThenBy(a => a.Id).ToList();
    }

    public async Task<List<AccAccount>> GetAccountTreeAsync(bool activeOnly = false, bool withBalances = false, Paging.Request? pagination = null)
    {
        var flat = await BuildAccountsAsync(activeOnly, withBalances, pagination);
        var map = flat.ToDictionary(a => a.Id);
        var roots = new List<AccAccount>();

        foreach (var a in flat)
        {
            if (a.ParentId is not null && map.TryGetValue(a.ParentId.Value, out var parent))
                parent.Children.Add(a);
            else
                roots.Add(a);
        }
        return roots;
    }

    public async Task<List<LookupItem>> GetPostableAccountLookupsAsync(string? search = null, Paging.Request? pagination = null)
    {
        var q = _db.AccAccounts.AsNoTracking().Where(a => a.IsActive && a.IsPostable);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a => a.Name.Contains(s) || a.Code.Contains(s));
        }
        return await q.OrderBy(a => a.Code).ThenBy(x => x.Id)
            .Select(a => new LookupItem { Id = a.Id, Name = a.Code + " — " + a.Name }).ToPageListAsync(pagination, defaultCap: 300);
    }

    public async Task<AccAccount> SaveAccountAsync(AccAccount dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام حساب الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidOperationException("کد حساب الزامی است.");

        var code = dto.Code.Trim();
        if (await _db.AccAccounts.AnyAsync(a => a.Code == code && a.Id != dto.Id))
            throw new InvalidOperationException($"کد حساب «{code}» تکراری است.");

        if (dto.ParentId == dto.Id && dto.Id != 0)
            throw new InvalidOperationException("حساب نمی‌تواند والد خودش باشد.");
        if (dto.Id != 0 && dto.ParentId is not null && await IsDescendantAsync(dto.ParentId.Value, dto.Id))
            throw new InvalidOperationException("حساب را نمی‌توان زیرمجموعه‌ی یکی از فرزندان خودش کرد.");

        Db.AccAccount entity;
        if (dto.Id == 0)
        {
            entity = new Db.AccAccount();
            _db.AccAccounts.Add(entity);
        }
        else
        {
            entity = await _db.AccAccounts.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("حساب یافت نشد.");

            // اگر حساب گردش دارد، تغییر «قابل ثبت بودن» یا والد آن مجاز نیست
            var hasLines = await _db.AccVoucherLines.AnyAsync(l => l.AccountId == dto.Id);
            if (hasLines && !dto.IsPostable)
                throw new InvalidOperationException("این حساب در اسناد استفاده شده است؛ نمی‌توان آن را غیرقابل‌ثبت کرد.");
        }

        // ---------- ارث‌بری نوع/ماهیت از والد در صورت نبود مقدار ----------
        if (dto.ParentId is not null)
        {
            var parent = await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.ParentId)
                         ?? throw new InvalidOperationException("حساب والد یافت نشد.");

            if (!code.StartsWith(parent.Code, StringComparison.Ordinal))
                throw new InvalidOperationException($"کد حساب باید با کد والد («{parent.Code}») شروع شود.");

            dto.Type = parent.Type;
            dto.Level = parent.Level switch
            {
                AccountLevel.Group => AccountLevel.General,
                AccountLevel.General => AccountLevel.Subsidiary,
                _ => AccountLevel.Detail
            };
            dto.IsPermanent = parent.IsPermanent;
        }
        else
        {
            dto.Level = AccountLevel.Group;
        }

        entity.Code = code;
        entity.Name = dto.Name.Trim();
        entity.EnName = dto.EnName?.Trim();
        entity.ParentId = dto.ParentId;
        entity.Level = dto.Level;
        entity.Type = dto.Type;
        entity.Nature = dto.Nature;
        entity.IsPermanent = dto.IsPermanent;
        entity.IsPostable = dto.IsPostable;
        entity.RequiresParty = dto.RequiresParty;
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Description = dto.Description;

        await _db.SaveChangesAsync();

        // والد نمی‌تواند قابل ثبت بماند
        if (entity.ParentId is not null)
        {
            var parent = await _db.AccAccounts.FindAsync(entity.ParentId.Value);
            if (parent is not null && parent.IsPostable &&
                !await _db.AccVoucherLines.AnyAsync(l => l.AccountId == parent.Id))
            {
                parent.IsPostable = false;
                await _db.SaveChangesAsync();
            }
        }

        dto.Id = entity.Id;
        return dto;
    }

    public async Task MoveAccountAsync(AccAccountMove cmd)
    {
        var acc = await _db.AccAccounts.FindAsync(cmd.Id)
                  ?? throw new InvalidOperationException("حساب یافت نشد.");
        if (cmd.NewParentId is not null && await IsDescendantAsync(cmd.NewParentId.Value, cmd.Id))
            throw new InvalidOperationException("انتقال به زیرمجموعه‌ی خود حساب ممکن نیست.");

        acc.ParentId = cmd.NewParentId;
        acc.SortOrder = cmd.SortOrder;
        await _db.SaveChangesAsync();
    }

    private async Task<bool> IsDescendantAsync(int candidateId, int ancestorId)
    {
        var cur = await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == candidateId);
        var guard = 0;
        while (cur?.ParentId is not null && guard++ < 50)
        {
            if (cur.ParentId == ancestorId) return true;
            cur = await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cur.ParentId);
        }
        return false;
    }

    public async Task DeleteAccountAsync(int id)
    {
        var acc = await _db.AccAccounts.FindAsync(id)
                  ?? throw new InvalidOperationException("حساب یافت نشد.");
        if (acc.IsSystem)
            throw new InvalidOperationException("این حساب سیستمی است و قابل حذف نیست.");
        if (await _db.AccAccounts.AnyAsync(a => a.ParentId == id))
            throw new InvalidOperationException("ابتدا زیرمجموعه‌های این حساب را حذف یا منتقل کنید.");
        if (await _db.AccVoucherLines.AnyAsync(l => l.AccountId == id))
            throw new InvalidOperationException("این حساب در اسناد استفاده شده است؛ به‌جای حذف، آن را غیرفعال کنید.");
        if (await _db.AccInvRules.AnyAsync(r => r.InventoryAccountId == id || r.CounterAccountId == id))
            throw new InvalidOperationException("این حساب در قواعد سند خودکار انبار استفاده شده است.");

        _db.AccAccounts.Remove(acc);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // ۳) سند حسابداری
    // =====================================================================

    public async Task<PagedResult<AccVoucher>> GetVouchersAsync(int? fiscalYearId, VoucherStatus? status,
        VoucherSource? source, string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.AccVouchers.AsNoTracking().Include(v => v.FiscalYear).AsQueryable();

        if (fiscalYearId is > 0) q = q.Where(v => v.FiscalYearId == fiscalYearId);
        if (status is not null) q = q.Where(v => v.Status == status);
        if (source is not null) q = q.Where(v => v.Source == source);
        if (from is not null) q = q.Where(v => v.Date >= from.Value.Date);
        if (to is not null) q = q.Where(v => v.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(v => (v.Description ?? "").Contains(s)
                          || (v.RefNumber ?? "").Contains(s)
                          || (v.SourceTitle ?? "").Contains(s)
                          || v.Number.ToString().Contains(s));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.Date).ThenByDescending(v => v.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(v => new AccVoucher
            {
                Id = v.Id,
                Number = v.Number,
                RefNumber = v.RefNumber,
                FiscalYearId = v.FiscalYearId,
                FiscalYearTitle = v.FiscalYear!.Title,
                Date = v.Date,
                Description = v.Description,
                Status = v.Status,
                Source = v.Source,
                SourceId = v.SourceId,
                SourceTitle = v.SourceTitle,
                TotalDebit = v.TotalDebit,
                TotalCredit = v.TotalCredit,
                LineCount = v.Lines.Count,
                CreatedBy = v.CreatedBy,
                CreatedAt = v.CreatedAt,
                ConfirmedBy = v.ConfirmedBy,
                ConfirmedAt = v.ConfirmedAt
            }).ToListAsync();

        return new PagedResult<AccVoucher> { Items = items, TotalCount = total };
    }

    public async Task<AccVoucher?> GetVoucherAsync(int id)
    {
        var v = await _db.AccVouchers.AsNoTracking()
            .Include(x => x.FiscalYear)
            .Include(x => x.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return null;

        var partyIds = v.Lines.Where(l => l.PartyId is not null).Select(l => l.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var dimIds = v.Lines.Where(l => l.DimensionValueId is not null).Select(l => l.DimensionValueId!.Value).Distinct().ToList();
        var dims = dimIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.AccDimensionValues.AsNoTracking().Where(d => dimIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => $"{d.Code} — {d.Name}");

        return new AccVoucher
        {
            Id = v.Id,
            Number = v.Number,
            RefNumber = v.RefNumber,
            FiscalYearId = v.FiscalYearId,
            FiscalYearTitle = v.FiscalYear?.Title,
            Date = v.Date,
            Description = v.Description,
            Status = v.Status,
            Source = v.Source,
            SourceId = v.SourceId,
            SourceTitle = v.SourceTitle,
            TotalDebit = v.TotalDebit,
            TotalCredit = v.TotalCredit,
            LineCount = v.Lines.Count,
            CreatedBy = v.CreatedBy,
            CreatedAt = v.CreatedAt,
            ConfirmedBy = v.ConfirmedBy,
            ConfirmedAt = v.ConfirmedAt,
            Lines = v.Lines.OrderBy(l => l.RowNo).Select(l => new AccVoucherLine
            {
                Id = l.Id,
                RowNo = l.RowNo,
                AccountId = l.AccountId,
                AccountCode = l.Account?.Code ?? "",
                AccountName = l.Account?.Name ?? "",
                PartyId = l.PartyId,
                PartyName = l.PartyId is not null && parties.TryGetValue(l.PartyId.Value, out var pn) ? pn : null,
                ProjectId = l.ProjectId,
                DimensionValueId = l.DimensionValueId,
                DimensionValueName = l.DimensionValueId is not null && dims.TryGetValue(l.DimensionValueId.Value, out var dn) ? dn : null,
                Description = l.Description,
                RefNumber = l.RefNumber,
                Debit = l.Debit,
                Credit = l.Credit
            }).ToList()
        };
    }

    public async Task<AccVoucher> NewVoucherAsync()
    {
        var year = await CurrentYearAsync();
        return new AccVoucher
        {
            FiscalYearId = year.Id,
            FiscalYearTitle = year.Title,
            Number = await NextVoucherNumberAsync(year.Id),
            Date = DateTime.Now,
            Status = VoucherStatus.Draft,
            Source = VoucherSource.Manual,
            Lines = new List<AccVoucherLine>
            {
                new() { RowNo = 1 },
                new() { RowNo = 2 }
            }
        };
    }

    private async Task<int> NextVoucherNumberAsync(int fiscalYearId)
    {
        var max = await _db.AccVouchers.Where(v => v.FiscalYearId == fiscalYearId)
            .Select(v => (int?)v.Number).MaxAsync();
        return (max ?? 0) + 1;
    }

    public async Task<AccVoucher> SaveVoucherAsync(AccVoucher dto, string? user)
    {
        var year = dto.FiscalYearId > 0
            ? await _db.AccFiscalYears.FindAsync(dto.FiscalYearId) ?? await CurrentYearAsync()
            : await CurrentYearAsync();

        if (year.IsClosed)
            throw new InvalidOperationException($"سال مالی «{year.Title}» بسته شده است؛ امکان ثبت سند وجود ندارد.");
        if (dto.Date.Date < year.StartDate.Date || dto.Date.Date > year.EndDate.Date)
            throw new InvalidOperationException("تاریخ سند خارج از بازه‌ی سال مالی انتخاب‌شده است.");

        var lines = dto.Lines
            .Where(l => l.AccountId > 0 && (l.Debit != 0 || l.Credit != 0))
            .ToList();

        if (lines.Count == 0)
            throw new InvalidOperationException("سند باید حداقل یک آرتیکل داشته باشد.");
        if (lines.Any(l => l.Debit != 0 && l.Credit != 0))
            throw new InvalidOperationException("در هر آرتیکل فقط یکی از مبالغ بدهکار یا بستانکار باید پر شود.");
        if (lines.Any(l => l.Debit < 0 || l.Credit < 0))
            throw new InvalidOperationException("مبلغ آرتیکل نمی‌تواند منفی باشد.");

        var accIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.AccAccounts.Where(a => accIds.Contains(a.Id)).ToListAsync();

        foreach (var l in lines)
        {
            var acc = accounts.FirstOrDefault(a => a.Id == l.AccountId)
                      ?? throw new InvalidOperationException("یکی از حساب‌های انتخاب‌شده یافت نشد.");
            if (!acc.IsPostable)
                throw new InvalidOperationException($"حساب «{acc.Code} — {acc.Name}» قابل ثبت سند نیست؛ یک حساب سطح پایین‌تر انتخاب کنید.");
            if (!acc.IsActive)
                throw new InvalidOperationException($"حساب «{acc.Code} — {acc.Name}» غیرفعال است.");
            if (acc.RequiresParty && (l.PartyId is null or 0))
                throw new InvalidOperationException($"برای حساب «{acc.Name}» ثبت طرف حساب الزامی است.");
            if (l.DimensionValueId is > 0)
            {
                var dimOk = await _db.AccDimensionValues.AnyAsync(v => v.Id == l.DimensionValueId && v.IsActive);
                if (!dimOk)
                    throw new InvalidOperationException("بُعد تحلیلی انتخاب‌شده نامعتبر یا غیرفعال است.");
            }
        }

        Db.AccVoucher entity;
        if (dto.Id == 0)
        {
            entity = new Db.AccVoucher
            {
                FiscalYearId = year.Id,
                Number = dto.Number > 0 ? dto.Number : await NextVoucherNumberAsync(year.Id),
                CreatedBy = user,
                CreatedAt = DateTime.Now,
                Source = dto.Source,
                SourceId = dto.SourceId,
                SourceTitle = dto.SourceTitle
            };
            _db.AccVouchers.Add(entity);
        }
        else
        {
            entity = await _db.AccVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.Id == dto.Id)
                     ?? throw new InvalidOperationException("سند یافت نشد.");
            if (entity.Status != VoucherStatus.Draft)
                throw new InvalidOperationException("فقط سند پیش‌نویس قابل ویرایش است.");

            _db.AccVoucherLines.RemoveRange(entity.Lines);
            entity.Lines.Clear();
        }

        // شماره تکراری در همان سال مالی
        var dupNumber = await _db.AccVouchers
            .AnyAsync(v => v.FiscalYearId == year.Id && v.Number == entity.Number && v.Id != entity.Id);
        if (dupNumber) entity.Number = await NextVoucherNumberAsync(year.Id);

        entity.FiscalYearId = year.Id;
        entity.Date = dto.Date.Date;
        entity.Description = dto.Description;
        entity.RefNumber = dto.RefNumber;

        var rowNo = 1;
        foreach (var l in lines)
        {
            entity.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = rowNo++,
                AccountId = l.AccountId,
                PartyId = l.PartyId is > 0 ? l.PartyId : null,
                ProjectId = l.ProjectId is > 0 ? l.ProjectId : null,
                DimensionValueId = l.DimensionValueId is > 0 ? l.DimensionValueId : null,
                Description = string.IsNullOrWhiteSpace(l.Description) ? dto.Description : l.Description,
                RefNumber = l.RefNumber,
                Debit = l.Debit,
                Credit = l.Credit
            });
        }

        entity.TotalDebit = entity.Lines.Sum(l => l.Debit);
        entity.TotalCredit = entity.Lines.Sum(l => l.Credit);

        await _db.SaveChangesAsync();
        return (await GetVoucherAsync(entity.Id))!;
    }

    public async Task<AccVoucher> ConfirmVoucherAsync(int id, string? user)
    {
        var v = await _db.AccVouchers.Include(x => x.Lines).Include(x => x.FiscalYear)
                    .FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("سند یافت نشد.");

        if (v.Status == VoucherStatus.Confirmed) return (await GetVoucherAsync(id))!;
        if (v.Status == VoucherStatus.Cancelled)
            throw new InvalidOperationException("سند ابطال‌شده قابل قطعی کردن نیست.");
        if (v.FiscalYear?.IsClosed == true)
            throw new InvalidOperationException("سال مالی بسته است.");
        if (v.Lines.Count < 2)
            throw new InvalidOperationException("سند حسابداری باید حداقل دو آرتیکل داشته باشد.");
        if (v.TotalDebit != v.TotalCredit)
            throw new InvalidOperationException(
                $"سند تراز نیست. جمع بدهکار {v.TotalDebit:N0} و جمع بستانکار {v.TotalCredit:N0} است.");
        if (v.TotalDebit == 0)
            throw new InvalidOperationException("مبلغ سند نمی‌تواند صفر باشد.");

        v.Status = VoucherStatus.Confirmed;
        v.ConfirmedBy = user;
        v.ConfirmedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return (await GetVoucherAsync(id))!;
    }

    public async Task<AccVoucher> UnconfirmVoucherAsync(int id, string? user)
    {
        var v = await _db.AccVouchers.Include(x => x.FiscalYear).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("سند یافت نشد.");
        if (v.FiscalYear?.IsClosed == true)
            throw new InvalidOperationException("سال مالی بسته است.");
        if (v.Source != VoucherSource.Manual)
            throw new InvalidOperationException("این سند به‌صورت خودکار صادر شده است؛ باید از مسیر سند مبدأ برگشت داده شود.");

        v.Status = VoucherStatus.Draft;
        v.ConfirmedBy = null;
        v.ConfirmedAt = null;
        await _db.SaveChangesAsync();
        return (await GetVoucherAsync(id))!;
    }

    public async Task<AccVoucher> CancelVoucherAsync(int id, string? user)
    {
        var v = await _db.AccVouchers.Include(x => x.FiscalYear).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("سند یافت نشد.");
        if (v.FiscalYear?.IsClosed == true)
            throw new InvalidOperationException("سال مالی بسته است.");

        v.Status = VoucherStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (await GetVoucherAsync(id))!;
    }

    public async Task DeleteVoucherAsync(int id)
    {
        var v = await _db.AccVouchers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("سند یافت نشد.");
        if (v.Status == VoucherStatus.Confirmed)
            throw new InvalidOperationException("سند قطعی قابل حذف نیست؛ ابتدا آن را برگشت یا ابطال کنید.");
        if (v.Source != VoucherSource.Manual)
            throw new InvalidOperationException("سند خودکار از مسیر سند مبدأ حذف می‌شود.");

        _db.AccVoucherLines.RemoveRange(v.Lines);
        _db.AccVouchers.Remove(v);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // ۴) دفاتر و تراز
    // =====================================================================

    public async Task<AccLedgerResult> GetLedgerAsync(int accountId, DateTime? from, DateTime? to, bool includeChildren)
    {
        var acc = await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId)
                  ?? throw new InvalidOperationException("حساب یافت نشد.");

        var ids = includeChildren ? await DescendantIdsAsync(accountId) : new List<int> { accountId };

        var baseQ = _db.AccVoucherLines.AsNoTracking()
            .Include(l => l.Voucher)
            .Where(l => ids.Contains(l.AccountId) && l.Voucher!.Status == VoucherStatus.Confirmed);

        // ---------- مانده ابتدای بازه ----------
        decimal opening = 0;
        if (from is not null)
        {
            var before = await baseQ.Where(l => l.Voucher!.Date < from.Value.Date)
                .Select(l => new { l.Debit, l.Credit })
                .ToListAsync();
            opening = before.Sum(x => x.Debit) - before.Sum(x => x.Credit);
        }

        var q = baseQ.AsQueryable();
        if (from is not null) q = q.Where(l => l.Voucher!.Date >= from.Value.Date);
        if (to is not null) q = q.Where(l => l.Voucher!.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        var raw = await q
            .OrderBy(l => l.Voucher!.Date).ThenBy(l => l.Voucher!.Number).ThenBy(l => l.RowNo)
            .Select(l => new
            {
                l.Voucher!.Date,
                VoucherId = l.VoucherId,
                VoucherNumber = l.Voucher.Number,
                l.RowNo,
                Code = l.Account!.Code,
                AccName = l.Account.Name,
                l.PartyId,
                l.DimensionValueId,
                l.Description,
                l.Debit,
                l.Credit
            }).ToListAsync();

        var partyIds = raw.Where(r => r.PartyId is not null).Select(r => r.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var dimIds = raw.Where(r => r.DimensionValueId is not null).Select(r => r.DimensionValueId!.Value).Distinct().ToList();
        var dims = dimIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.AccDimensionValues.AsNoTracking().Where(d => dimIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => $"{d.Code} — {d.Name}");

        var rows = new List<AccLedgerRow>();
        var balance = opening;
        foreach (var r in raw)
        {
            balance += r.Debit - r.Credit;
            rows.Add(new AccLedgerRow
            {
                Date = r.Date,
                VoucherId = r.VoucherId,
                VoucherNumber = r.VoucherNumber,
                RowNo = r.RowNo,
                AccountCode = r.Code,
                AccountName = r.AccName,
                PartyName = r.PartyId is not null && parties.TryGetValue(r.PartyId.Value, out var pn) ? pn : null,
                DimensionValueName = r.DimensionValueId is not null && dims.TryGetValue(r.DimensionValueId.Value, out var dn) ? dn : null,
                Description = r.Description,
                Debit = r.Debit,
                Credit = r.Credit,
                Balance = balance
            });
        }

        return new AccLedgerResult
        {
            AccountId = acc.Id,
            AccountCode = acc.Code,
            AccountName = acc.Name,
            LevelTitle = acc.Level switch
            {
                AccountLevel.Group => "گروه",
                AccountLevel.General => "کل",
                AccountLevel.Subsidiary => "معین",
                _ => "تفصیلی"
            },
            OpeningBalance = opening,
            Rows = rows,
            TotalDebit = rows.Sum(r => r.Debit),
            TotalCredit = rows.Sum(r => r.Credit),
            ClosingBalance = balance
        };
    }

    private async Task<List<int>> DescendantIdsAsync(int rootId)
    {
        var all = await _db.AccAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.ParentId }).ToListAsync();

        var result = new List<int> { rootId };
        var frontier = new List<int> { rootId };
        var guard = 0;
        while (frontier.Count > 0 && guard++ < 20)
        {
            var next = all.Where(a => a.ParentId is not null && frontier.Contains(a.ParentId.Value))
                .Select(a => a.Id).ToList();
            result.AddRange(next);
            frontier = next;
        }
        return result.Distinct().ToList();
    }

    public async Task<PagedResult<AccLedgerRow>> GetJournalAsync(DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.AccVoucherLines.AsNoTracking()
            .Include(l => l.Voucher).Include(l => l.Account).Include(l => l.DimensionValue)
            .Where(l => l.Voucher!.Status == VoucherStatus.Confirmed);

        if (from is not null) q = q.Where(l => l.Voucher!.Date >= from.Value.Date);
        if (to is not null) q = q.Where(l => l.Voucher!.Date <= to.Value.Date.AddDays(1).AddTicks(-1));

        var total = await q.CountAsync();
        var rows = await q
            .OrderBy(l => l.Voucher!.Date).ThenBy(l => l.Voucher!.Number).ThenBy(l => l.RowNo)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new AccLedgerRow
            {
                Date = l.Voucher!.Date,
                VoucherId = l.VoucherId,
                VoucherNumber = l.Voucher.Number,
                RowNo = l.RowNo,
                AccountCode = l.Account!.Code,
                AccountName = l.Account.Name,
                DimensionValueName = l.DimensionValue != null
                    ? $"{l.DimensionValue.Code} — {l.DimensionValue.Name}" : null,
                Description = l.Description,
                Debit = l.Debit,
                Credit = l.Credit
            }).ToListAsync();

        return new PagedResult<AccLedgerRow> { Items = rows, TotalCount = total };
    }

    public async Task<AccTrialBalanceResult> GetTrialBalanceAsync(AccountLevel level, DateTime? from, DateTime? to, bool hideZero)
    {
        var accounts = await _db.AccAccounts.AsNoTracking().ToListAsync();
        var byId = accounts.ToDictionary(a => a.Id);

        // نگاشت هر حساب قابل ثبت به جدِ آن در سطح موردنظر
        int? AncestorAt(int accId, AccountLevel lvl)
        {
            var cur = byId.TryGetValue(accId, out var a) ? a : null;
            var guard = 0;
            while (cur is not null && guard++ < 20)
            {
                if (cur.Level == lvl) return cur.Id;
                if (cur.ParentId is null) return null;
                cur = byId.TryGetValue(cur.ParentId.Value, out var p) ? p : null;
            }
            return null;
        }

        var linesQ = _db.AccVoucherLines.AsNoTracking()
            .Include(l => l.Voucher)
            .Where(l => l.Voucher!.Status == VoucherStatus.Confirmed);

        var all = await linesQ.Select(l => new { l.AccountId, l.Debit, l.Credit, l.Voucher!.Date }).ToListAsync();

        var rows = new Dictionary<int, AccTrialBalanceRow>();
        foreach (var l in all)
        {
            var anc = AncestorAt(l.AccountId, level);
            if (anc is null) continue;
            if (!rows.TryGetValue(anc.Value, out var row))
            {
                var a = byId[anc.Value];
                row = new AccTrialBalanceRow
                {
                    AccountId = a.Id,
                    AccountCode = a.Code,
                    AccountName = a.Name,
                    Level = a.Level
                };
                rows[anc.Value] = row;
            }

            var isBefore = from is not null && l.Date < from.Value.Date;
            var isAfter = to is not null && l.Date > to.Value.Date.AddDays(1).AddTicks(-1);
            if (isAfter) continue;

            if (isBefore)
            {
                row.OpeningDebit += l.Debit;
                row.OpeningCredit += l.Credit;
            }
            else
            {
                row.PeriodDebit += l.Debit;
                row.PeriodCredit += l.Credit;
            }
        }

        foreach (var row in rows.Values)
        {
            // مانده ابتدای دوره را خالص می‌کنیم
            var open = row.OpeningDebit - row.OpeningCredit;
            row.OpeningDebit = open > 0 ? open : 0;
            row.OpeningCredit = open < 0 ? -open : 0;

            var close = row.TotalDebit - row.TotalCredit;
            row.ClosingDebit = close > 0 ? close : 0;
            row.ClosingCredit = close < 0 ? -close : 0;
        }

        var list = rows.Values
            .Where(r => !hideZero || r.PeriodDebit != 0 || r.PeriodCredit != 0 || r.ClosingDebit != 0 || r.ClosingCredit != 0)
            .OrderBy(r => r.AccountCode, StringComparer.Ordinal)
            .ToList();

        return new AccTrialBalanceResult
        {
            Level = level,
            From = from,
            To = to,
            Rows = list,
            SumOpeningDebit = list.Sum(r => r.OpeningDebit),
            SumOpeningCredit = list.Sum(r => r.OpeningCredit),
            SumPeriodDebit = list.Sum(r => r.PeriodDebit),
            SumPeriodCredit = list.Sum(r => r.PeriodCredit),
            SumClosingDebit = list.Sum(r => r.ClosingDebit),
            SumClosingCredit = list.Sum(r => r.ClosingCredit)
        };
    }

    public async Task<AccDashboard> GetDashboardAsync()
    {
        var year = await _db.AccFiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.IsCurrent);
        var dash = new AccDashboard { FiscalYearTitle = year?.Title };

        var q = _db.AccVouchers.AsNoTracking().AsQueryable();
        if (year is not null) q = q.Where(v => v.FiscalYearId == year.Id);

        dash.VoucherCount = await q.CountAsync(v => v.Status == VoucherStatus.Confirmed);
        dash.DraftCount = await q.CountAsync(v => v.Status == VoucherStatus.Draft);
        var confirmedTotals = await q.Where(v => v.Status == VoucherStatus.Confirmed)
            .Select(v => new { v.TotalDebit, v.TotalCredit }).ToListAsync();
        dash.TotalDebit = confirmedTotals.Sum(v => v.TotalDebit);
        dash.TotalCredit = confirmedTotals.Sum(v => v.TotalCredit);

        var accounts = await _db.AccAccounts.AsNoTracking().Select(a => new { a.Id, a.Type }).ToListAsync();
        var typeOf = accounts.ToDictionary(a => a.Id, a => a.Type);

        var lines = await _db.AccVoucherLines.AsNoTracking()
            .Include(l => l.Voucher)
            .Where(l => l.Voucher!.Status == VoucherStatus.Confirmed
                        && (year == null || l.Voucher.FiscalYearId == year.Id))
            .Select(l => new { l.AccountId, l.Debit, l.Credit })
            .ToListAsync();

        foreach (var l in lines)
        {
            if (!typeOf.TryGetValue(l.AccountId, out var t)) continue;
            var net = l.Debit - l.Credit;
            switch (t)
            {
                case AccountType.Asset: dash.AssetTotal += net; break;
                case AccountType.Liability: dash.LiabilityTotal += -net; break;
                case AccountType.Equity: dash.EquityTotal += -net; break;
                case AccountType.Income: dash.IncomeTotal += -net; break;
                case AccountType.Expense: dash.ExpenseTotal += net; break;
            }
        }
        return dash;
    }

    // =====================================================================
    // ۵) سند خودکار از روی اسناد انبار
    // =====================================================================

    public async Task<List<AccInvRule>> GetInvRulesAsync(Paging.Request? pagination = null)
    {
        var types = await _db.InvDocTypes.AsNoTracking().OrderBy(t => t.Nature).ThenBy(t => t.SortOrder).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var rules = await _db.AccInvRules.AsNoTracking().ToListAsync();
        var accounts = await _db.AccAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code + " — " + a.Name);

        return types.Select(t =>
        {
            var r = rules.FirstOrDefault(x => x.DocTypeId == t.Id);
            return new AccInvRule
            {
                Id = r?.Id ?? 0,
                DocTypeId = t.Id,
                DocTypeName = t.Name,
                Nature = t.Nature,
                InventoryAccountId = r?.InventoryAccountId,
                InventoryAccountName = r?.InventoryAccountId is not null && accounts.TryGetValue(r.InventoryAccountId.Value, out var ia) ? ia : null,
                CounterAccountId = r?.CounterAccountId,
                CounterAccountName = r?.CounterAccountId is not null && accounts.TryGetValue(r.CounterAccountId.Value, out var ca) ? ca : null,
                UseCostValue = r?.UseCostValue ?? true,
                IsActive = r?.IsActive ?? false,
                Description = r?.Description
            };
        }).ToList();
    }

    public async Task<AccInvRule> SaveInvRuleAsync(AccInvRule dto)
    {
        var entity = await _db.AccInvRules.FirstOrDefaultAsync(r => r.DocTypeId == dto.DocTypeId);
        if (entity is null)
        {
            entity = new Db.AccInvRule { DocTypeId = dto.DocTypeId };
            _db.AccInvRules.Add(entity);
        }

        if (dto.IsActive && (dto.InventoryAccountId is null or 0 || dto.CounterAccountId is null or 0))
            throw new InvalidOperationException("برای فعال کردن صدور خودکار، هر دو حساب موجودی و طرف مقابل باید انتخاب شوند.");

        entity.InventoryAccountId = dto.InventoryAccountId is > 0 ? dto.InventoryAccountId : null;
        entity.CounterAccountId = dto.CounterAccountId is > 0 ? dto.CounterAccountId : null;
        entity.UseCostValue = dto.UseCostValue;
        entity.IsActive = dto.IsActive;
        entity.Description = dto.Description;

        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteInvRuleAsync(int id)
    {
        var r = await _db.AccInvRules.FindAsync(id);
        if (r is null) return;
        _db.AccInvRules.Remove(r);
        await _db.SaveChangesAsync();
    }

    public async Task<AccVoucher?> PostInventoryDocAsync(int invDocId, string? user)
    {
        var doc = await _db.InvDocs.AsNoTracking()
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == invDocId);
        if (doc is null) return null;

        var rule = await _db.AccInvRules.AsNoTracking().FirstOrDefaultAsync(r => r.DocTypeId == doc.DocTypeId);
        if (rule is null || !rule.IsActive) return null;
        if (rule.InventoryAccountId is null || rule.CounterAccountId is null) return null;

        var type = await _db.InvDocTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == doc.DocTypeId);
        if (type is null || type.Nature == StockNature.Neutral) return null;

        // ---------- مبلغ سند ----------
        decimal amount = rule.UseCostValue
            ? doc.Lines.Sum(l => l.OutCost ?? (l.Quantity * l.UnitPrice - l.Discount))
            : doc.Lines.Sum(l => l.Quantity * l.UnitPrice - l.Discount);

        if (amount <= 0) return null;

        // سند قبلی همین سند انبار را حذف می‌کنیم تا دوباره‌کاری نشود
        await UnpostInventoryDocAsync(invDocId);

        var year = await CurrentYearAsync();
        var title = $"{type.Name} {doc.Number}";

        var voucher = new Db.AccVoucher
        {
            FiscalYearId = year.Id,
            Number = await NextVoucherNumberAsync(year.Id),
            Date = doc.Date.Date,
            Description = $"سند خودکار — {title}",
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.InventoryDoc,
            SourceId = doc.Id,
            SourceTitle = title,
            RefNumber = doc.Number,
            CreatedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedBy = user,
            ConfirmedAt = DateTime.Now
        };

        // رسید (افزایشی): موجودی کالا بدهکار / طرف مقابل بستانکار
        // حواله (کاهشی): طرف مقابل (بهای تمام‌شده) بدهکار / موجودی کالا بستانکار
        var invDebit = type.Nature == StockNature.Increase;

        voucher.Lines.Add(new Db.AccVoucherLine
        {
            RowNo = 1,
            AccountId = rule.InventoryAccountId.Value,
            PartyId = doc.PartyId,
            Description = title,
            RefNumber = doc.Number,
            Debit = invDebit ? amount : 0,
            Credit = invDebit ? 0 : amount
        });
        voucher.Lines.Add(new Db.AccVoucherLine
        {
            RowNo = 2,
            AccountId = rule.CounterAccountId.Value,
            PartyId = doc.PartyId,
            Description = title,
            RefNumber = doc.Number,
            Debit = invDebit ? 0 : amount,
            Credit = invDebit ? amount : 0
        });

        voucher.TotalDebit = voucher.Lines.Sum(l => l.Debit);
        voucher.TotalCredit = voucher.Lines.Sum(l => l.Credit);

        _db.AccVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        return await GetVoucherAsync(voucher.Id);
    }

    public async Task UnpostInventoryDocAsync(int invDocId)
    {
        var old = await _db.AccVouchers.Include(v => v.Lines)
            .Where(v => v.Source == VoucherSource.InventoryDoc && v.SourceId == invDocId)
            .ToListAsync();
        if (old.Count == 0) return;

        foreach (var v in old) _db.AccVoucherLines.RemoveRange(v.Lines);
        _db.AccVouchers.RemoveRange(old);
        await _db.SaveChangesAsync();
    }
}
