using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrMain;

public interface IHrMainService
{
    // پروفایل شرکت
    Task<HrMainCompanyDto> GetCompanyAsync();
    Task<HrMainCompanyDto> SaveCompanyAsync(HrMainCompanySaveDto dto);
    Task<HrMainCompanyDto> SaveLogoAsync(Stream stream, string fileName);
    Task DeleteLogoAsync();

    // شعب و دفاتر
    Task<List<HrMainBranchDto>> ListBranchesAsync(Paging.Request? pagination = null);
    Task<HrMainBranchDto> SaveBranchAsync(int? id, HrMainBranchSaveDto dto);
    Task DeleteBranchAsync(int id);

    // ساختار سازمانی
    Task<List<HrMainOrgNodeDto>> GetTreeAsync(Paging.Request? pagination = null);
    Task<List<HrMainOrgNodeDto>> ListNodesAsync(Paging.Request? pagination = null, bool tree = false);
    Task<HrMainOrgNodeDto> SaveNodeAsync(int? id, HrMainOrgNodeSaveDto dto, int? byUserId = null, string? byUsername = null);
    Task DeleteNodeAsync(int id, int? byUserId = null, string? byUsername = null);

    // پست‌های سازمانی
    Task<List<HrMainPositionDto>> ListPositionsAsync(int? orgNodeId, Paging.Request? pagination = null);
    Task<HrMainPositionDto> SavePositionAsync(int? id, HrMainPositionSaveDto dto, int? byUserId = null, string? byUsername = null);
    Task DeletePositionAsync(int id, int? byUserId = null, string? byUsername = null);

    // تاریخچه تغییرات ساختار سازمانی (گره‌ها و پست‌ها)
    Task<HrMainChangeLogListResult> SearchChangeLogAsync(string? entity, string? q, DateTime? from, DateTime? to, int skip, int take);

    // مقایسه ساختار سازمانی بین دو تاریخ
    Task<HrMainCompareResultDto> CompareStructureAsync(DateTime from, DateTime to);

    // زبان و تقویم
    Task<HrMainLocaleDto> GetLocaleAsync();
    Task<HrMainLocaleDto> SaveLocaleAsync(HrMainLocaleDto dto);

    // قوانین پیش‌فرض
    Task<HrMainRulesDto> GetRulesAsync();
    Task<HrMainRulesDto> SaveRulesAsync(HrMainRulesDto dto);

    // تعطیلات رسمی/شرکتی (جدول اختصاصی HrMainHolidays)
    Task<List<HrMainHolidayDto>> ListHolidaysAsync(int? jalaliYear, Paging.Request? pagination = null);
    Task<HrMainHolidayDto> SaveHolidayAsync(HrMainHolidaySaveDto dto, string createdBy);
    Task DeleteHolidayAsync(int id);

    // نمای کلی
    Task<HrMainOverviewDto> OverviewAsync();
}

public class HrMainService : IHrMainService
{
    private readonly AppDbContext _db;
    private readonly FileStore _files;

    public HrMainService(AppDbContext db, FileStore files)
    {
        _db = db;
        _files = files;
    }

    // ==================== پروفایل شرکت ====================

    public async Task<HrMainCompanyDto> GetCompanyAsync()
        => MapCompany(await GetOrCreateCompanyAsync());

    public async Task<HrMainCompanyDto> SaveCompanyAsync(HrMainCompanySaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام شرکت الزامی است.");
        var c = await GetOrCreateCompanyAsync();
        c.Name = dto.Name.Trim();
        c.Address = dto.Address?.Trim();
        c.EconomicCode = dto.EconomicCode?.Trim();
        c.RegistrationNo = dto.RegistrationNo?.Trim();
        c.NationalId = dto.NationalId?.Trim();
        c.Phone = dto.Phone?.Trim();
        c.Email = dto.Email?.Trim();
        c.Website = dto.Website?.Trim();
        c.ManagerName = dto.ManagerName?.Trim();
        c.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return MapCompany(c);
    }

    public async Task<HrMainCompanyDto> SaveLogoAsync(Stream stream, string fileName)
    {
        // محدودیت نوع/پسوند و حجم لوگو برداشته شد — هر فایلی مجاز است
        var c = await GetOrCreateCompanyAsync();
        var rel = await _files.SaveAsync("hr/logo", 1, stream, fileName);
        // حذف لوگوی قبلی (در صورت خطا، ذخیره جدید حفظ می‌شود)
        if (!string.IsNullOrWhiteSpace(c.LogoPath) && c.LogoPath != rel)
        {
            try { _files.Delete(c.LogoPath); } catch { /* نادیده */ }
        }
        c.LogoPath = rel;
        c.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return MapCompany(c);
    }

    public async Task DeleteLogoAsync()
    {
        var c = await GetOrCreateCompanyAsync();
        if (!string.IsNullOrWhiteSpace(c.LogoPath))
        {
            try { _files.Delete(c.LogoPath); } catch { /* نادیده */ }
            c.LogoPath = null;
            c.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    private async Task<HrMainCompany> GetOrCreateCompanyAsync()
    {
        var c = await _db.HrMainCompanies.FirstOrDefaultAsync();
        if (c is not null) return c;
        c = new HrMainCompany { Name = "" };
        _db.HrMainCompanies.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    private static HrMainCompanyDto MapCompany(HrMainCompany c) => new()
    {
        Id = c.Id, Name = c.Name, LogoPath = c.LogoPath, Address = c.Address,
        EconomicCode = c.EconomicCode, RegistrationNo = c.RegistrationNo, NationalId = c.NationalId,
        Phone = c.Phone, Email = c.Email, Website = c.Website, ManagerName = c.ManagerName
    };

    // ==================== شعب و دفاتر ====================

    public async Task<List<HrMainBranchDto>> ListBranchesAsync(Paging.Request? pagination = null)
    {
        var rows = await _db.HrMainBranches.AsNoTracking()
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var counts = await _db.HrMainOrgNodes.AsNoTracking()
            .Where(n => n.BranchId != null)
            .GroupBy(n => n.BranchId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return rows.Select(b => new HrMainBranchDto
        {
            Id = b.Id, Code = b.Code, Name = b.Name, Type = (int)b.Type,
            City = b.City, Address = b.Address, Phone = b.Phone, ManagerName = b.ManagerName,
            IsActive = b.IsActive, SortOrder = b.SortOrder,
            NodeCount = counts.TryGetValue(b.Id, out var n) ? n : 0
        }).ToList();
    }

    public async Task<HrMainBranchDto> SaveBranchAsync(int? id, HrMainBranchSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام شعبه/دفتر الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidOperationException("کد شعبه/دفتر الزامی است.");
        if (dto.Type is < 0 or > 2)
            throw new InvalidOperationException("نوع شعبه/دفتر نامعتبر است.");
        if (await _db.HrMainBranches.AnyAsync(b => b.Id != (id ?? 0) && b.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد شعبه/دفتر تکراری است.");

        HrMainBranch b;
        if (id is > 0)
        {
            b = await _db.HrMainBranches.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("شعبه/دفتر یافت نشد.");
        }
        else
        {
            b = new HrMainBranch();
            _db.HrMainBranches.Add(b);
        }
        b.Code = dto.Code.Trim(); b.Name = dto.Name.Trim(); b.Type = (HrMainBranchType)dto.Type;
        b.City = dto.City?.Trim(); b.Address = dto.Address?.Trim(); b.Phone = dto.Phone?.Trim();
        b.ManagerName = dto.ManagerName?.Trim(); b.IsActive = dto.IsActive; b.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return (await ListBranchesAsync()).First(x => x.Id == b.Id);
    }

    public async Task DeleteBranchAsync(int id)
    {
        var b = await _db.HrMainBranches.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("شعبه/دفتر یافت نشد.");
        if (await _db.HrMainOrgNodes.AnyAsync(n => n.BranchId == id))
            throw new InvalidOperationException("این شعبه/دفتر در ساختار سازمانی استفاده شده و قابل حذف نیست.");
        _db.HrMainBranches.Remove(b);
        await _db.SaveChangesAsync();
    }

    // ==================== ساختار سازمانی ====================

    public async Task<List<HrMainOrgNodeDto>> GetTreeAsync(Paging.Request? pagination = null)
    {
        var flat = await ListNodesAsync(pagination, tree: true);
        var byId = flat.ToDictionary(n => n.Id);
        var roots = new List<HrMainOrgNodeDto>();
        foreach (var n in flat)
        {
            if (n.ParentId is > 0 && byId.TryGetValue(n.ParentId.Value, out var p)) p.Children.Add(n);
            else roots.Add(n);
        }
        return roots;
    }

    public async Task<List<HrMainOrgNodeDto>> ListNodesAsync(Paging.Request? pagination = null, bool tree = false)
    {
        var scope = _db.HrMainOrgNodes.AsNoTracking();
        IOrderedQueryable<HrMainOrgNode> Order(IQueryable<HrMainOrgNode> q) => q
            .OrderBy(n => n.Level).ThenBy(n => n.SortOrder).ThenBy(n => n.Name).ThenBy(n => n.Id);
        var rows = tree
            ? await Order(scope).ReadTreePageAsync(
                Order(scope.Where(n => n.ParentId == null || n.ParentId <= 0 || !scope.Any(p => p.Id == n.ParentId))),
                ids => Order(scope.Where(n => n.ParentId != null && ids.Contains(n.ParentId.Value))),
                n => n.Id, pagination)
            : await Order(scope).ToPageListAsync(pagination);
        var parentIds = rows.Where(n => n.ParentId.HasValue).Select(n => n.ParentId!.Value).ToList();
        var names = await _db.HrMainOrgNodes.AsNoTracking().Where(n => parentIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.Name);
        var rowIds = rows.Select(n => n.Id).ToList();
        var branchIds = rows.Where(n => n.BranchId.HasValue).Select(n => n.BranchId!.Value).ToList();
        var branches = await _db.HrMainBranches.AsNoTracking().Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name);
        var posCounts = await _db.HrMainPositions.AsNoTracking()
            .Where(p => p.OrgNodeId != null && rowIds.Contains(p.OrgNodeId.Value))
            .GroupBy(p => p.OrgNodeId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var empCounts = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.HrMainNodeId != null && rowIds.Contains(e.HrMainNodeId.Value))
            .GroupBy(e => e.HrMainNodeId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return rows.Select(n => new HrMainOrgNodeDto
        {
            Id = n.Id, ParentId = n.ParentId, Level = (int)n.Level,
            Code = n.Code, Name = n.Name,
            ParentName = n.ParentId is > 0 && names.TryGetValue(n.ParentId.Value, out var pn) ? pn : null,
            BranchId = n.BranchId,
            BranchName = n.BranchId is > 0 && branches.TryGetValue(n.BranchId.Value, out var bn) ? bn : null,
            ManagerTitle = n.ManagerTitle, Phone = n.Phone, Description = n.Description,
            SortOrder = n.SortOrder, IsActive = n.IsActive,
            PositionCount = posCounts.TryGetValue(n.Id, out var c) ? c : 0,
            EmployeeCount = empCounts.TryGetValue(n.Id, out var ec) ? ec : 0
        }).ToList();
    }

    public async Task<HrMainOrgNodeDto> SaveNodeAsync(int? id, HrMainOrgNodeSaveDto dto, int? byUserId = null, string? byUsername = null)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام گره سازمانی الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidOperationException("کد گره سازمانی الزامی است.");
        if (dto.Level is < 0 or > 3)
            throw new InvalidOperationException("سطح سازمانی نامعتبر است.");
        if (await _db.HrMainOrgNodes.AnyAsync(n => n.Id != (id ?? 0) && n.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد گره سازمانی تکراری است.");
        if (dto.BranchId is > 0 && !await _db.HrMainBranches.AnyAsync(b => b.Id == dto.BranchId.Value))
            throw new InvalidOperationException("شعبه/دفتر نامعتبر است.");

        // قانون سلسله‌مراتب: ریشه فقط «شرکت»؛ زیرمجموعه همیشه عمیق‌تر از والد
        HrMainOrgNode? parent = null;
        if (dto.ParentId is > 0)
        {
            if (id is > 0 && dto.ParentId.Value == id.Value)
                throw new InvalidOperationException("والد نمی‌تواند خود گره باشد.");
            parent = await _db.HrMainOrgNodes.FirstOrDefaultAsync(n => n.Id == dto.ParentId.Value)
                ?? throw new InvalidOperationException("گره والد یافت نشد.");
            if (dto.Level <= (int)parent.Level)
                throw new InvalidOperationException($"سطح «{LevelName(dto.Level)}» باید زیرمجموعه سطح «{LevelName((int)parent.Level)}» باشد (شرکت ← واحد ← دپارتمان ← تیم).");
            // جلوگیری از حلقه: والد نباید از نوادگان خود گره باشد
            if (id is > 0 && await IsDescendantAsync(id.Value, dto.ParentId.Value))
                throw new InvalidOperationException("والد انتخاب‌شده از زیرمجموعه‌های همین گره است (حلقه در درخت).");
        }
        else if (dto.Level != (int)HrMainOrgLevel.Company)
        {
            throw new InvalidOperationException("گره بدون والد (ریشه) باید در سطح «شرکت» باشد.");
        }

        var isNew = id is not > 0;
        HrMainOrgNode n;
        HrMainOrgNode? before = null;
        if (!isNew)
        {
            n = await _db.HrMainOrgNodes.FirstOrDefaultAsync(x => x.Id == id!.Value)
                ?? throw new InvalidOperationException("گره سازمانی یافت نشد.");
            before = new HrMainOrgNode
            {
                ParentId = n.ParentId, Level = n.Level, Code = n.Code, Name = n.Name,
                BranchId = n.BranchId, ManagerTitle = n.ManagerTitle, Phone = n.Phone,
                Description = n.Description, SortOrder = n.SortOrder, IsActive = n.IsActive
            };
        }
        else
        {
            n = new HrMainOrgNode();
            _db.HrMainOrgNodes.Add(n);
        }
        n.ParentId = dto.ParentId; n.Level = (HrMainOrgLevel)dto.Level;
        n.Code = dto.Code.Trim(); n.Name = dto.Name.Trim();
        n.BranchId = dto.BranchId; n.ManagerTitle = dto.ManagerTitle?.Trim();
        n.Phone = dto.Phone?.Trim(); n.Description = dto.Description?.Trim();
        n.SortOrder = dto.SortOrder; n.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        var branchName = n.BranchId is > 0 ? await _db.HrMainBranches.AsNoTracking()
            .Where(b => b.Id == n.BranchId.Value).Select(b => b.Name).FirstOrDefaultAsync() : null;
        if (isNew)
        {
            await LogChangeAsync(HrMainChangeEntity.OrgNode, HrMainChangeAction.Create, n.Id, n.Name, null, null, null, byUserId, byUsername);
        }
        else
        {
            var beforeParentName = before!.ParentId is > 0
                ? await _db.HrMainOrgNodes.AsNoTracking().Where(x => x.Id == before.ParentId!.Value).Select(x => x.Name).FirstOrDefaultAsync()
                : null;
            var afterParentName = n.ParentId is > 0
                ? await _db.HrMainOrgNodes.AsNoTracking().Where(x => x.Id == n.ParentId!.Value).Select(x => x.Name).FirstOrDefaultAsync()
                : null;
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "والد", beforeParentName ?? "— (ریشه)", afterParentName ?? "— (ریشه)", byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "سطح", LevelName((int)before.Level), LevelName((int)n.Level), byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "نام", before.Name, n.Name, byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "کد", before.Code, n.Code, byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "شعبه", before.BranchId is > 0 ? null : "—", branchName ?? "—", byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "عنوان مدیر", before.ManagerTitle, n.ManagerTitle, byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.OrgNode, n.Id, n.Name, "وضعیت فعال", before.IsActive ? "فعال" : "غیرفعال", n.IsActive ? "فعال" : "غیرفعال", byUserId, byUsername);
        }
        return (await ListNodesAsync()).First(x => x.Id == n.Id);
    }

    public async Task DeleteNodeAsync(int id, int? byUserId = null, string? byUsername = null)
    {
        var n = await _db.HrMainOrgNodes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("گره سازمانی یافت نشد.");
        if (await _db.HrMainOrgNodes.AnyAsync(x => x.ParentId == id))
            throw new InvalidOperationException("این گره زیرمجموعه دارد و قابل حذف نیست؛ ابتدا زیرمجموعه‌ها را حذف یا جابه‌جا کنید.");
        if (await _db.HrMainPositions.AnyAsync(p => p.OrgNodeId == id))
            throw new InvalidOperationException("روی این گره پست سازمانی تعریف شده و قابل حذف نیست.");
        if (await _db.HrEmployees.AnyAsync(e => e.HrMainNodeId == id))
            throw new InvalidOperationException("پرسنلی به این گره منتسب است و قابل حذف نیست.");
        _db.HrMainOrgNodes.Remove(n);
        await _db.SaveChangesAsync();
        await LogChangeAsync(HrMainChangeEntity.OrgNode, HrMainChangeAction.Delete, n.Id, n.Name, null, null, null, byUserId, byUsername);
    }

    /// <summary>آیا candidateId از نوادگان nodeId است؟ (پیمایش والدها به سمت بالا)</summary>
    private async Task<bool> IsDescendantAsync(int nodeId, int candidateId)
    {
        var cur = await _db.HrMainOrgNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == candidateId);
        var guard = 0;
        while (cur?.ParentId is > 0 && guard++ < 100)
        {
            if (cur.ParentId.Value == nodeId) return true;
            cur = await _db.HrMainOrgNodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == cur.ParentId.Value);
        }
        return false;
    }

    private static string LevelName(int l) => l switch
    {
        0 => "شرکت", 1 => "واحد", 2 => "دپارتمان", 3 => "تیم", _ => "نامشخص"
    };

    // ==================== تاریخچه تغییرات (گره‌ها و پست‌ها) ====================

    /// <summary>ثبت یک ردیف تاریخچه (ایجاد/حذف کل رکورد، یا تغییر یک فیلد مشخص)</summary>
    private async Task LogChangeAsync(HrMainChangeEntity entity, HrMainChangeAction action, int entityId, string entityName,
        string? field, string? oldValue, string? newValue, int? byUserId, string? byUsername)
    {
        _db.HrMainChangeLogs.Add(new HrMainChangeLog
        {
            At = DateTime.Now, Entity = entity, Action = action, EntityId = entityId,
            EntityName = entityName, FieldName = field, OldValue = oldValue, NewValue = newValue,
            ByUserId = byUserId, ByUsername = byUsername ?? ""
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>ثبت تغییر یک فیلد فقط در صورتی که مقدار واقعاً عوض شده باشد</summary>
    private async Task LogFieldChangeAsync(HrMainChangeEntity entity, int entityId, string entityName,
        string field, string? oldValue, string? newValue, int? byUserId, string? byUsername)
    {
        if (string.Equals(oldValue ?? "", newValue ?? "", StringComparison.Ordinal)) return;
        await LogChangeAsync(entity, HrMainChangeAction.Update, entityId, entityName, field, oldValue, newValue, byUserId, byUsername);
    }

    public async Task<HrMainChangeLogListResult> SearchChangeLogAsync(string? entity, string? q, DateTime? from, DateTime? to, int skip, int take)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _db.HrMainChangeLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity) && Enum.TryParse<HrMainChangeEntity>(entity, true, out var ent))
            query = query.Where(l => l.Entity == ent);
        if (from != null) query = query.Where(l => l.At >= from.Value.Date);
        if (to != null) query = query.Where(l => l.At < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(l =>
                l.EntityName.Contains(term) ||
                (l.ByUsername != null && l.ByUsername.Contains(term)) ||
                (l.FieldName != null && l.FieldName.Contains(term)));
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(l => l.At).ThenByDescending(l => l.Id)
            .Skip(Math.Max(0, skip)).Take(take)
            .Select(l => new HrMainChangeLogDto
            {
                Id = l.Id, At = l.At, Entity = l.Entity.ToString(), Action = l.Action.ToString(),
                EntityId = l.EntityId, EntityName = l.EntityName, FieldName = l.FieldName,
                OldValue = l.OldValue, NewValue = l.NewValue, ByUsername = l.ByUsername
            }).ToListAsync();
        return new HrMainChangeLogListResult { Total = total, Items = items };
    }

    // ==================== مقایسه ساختار سازمانی بین دو تاریخ ====================

    /// <summary>
    /// خلاصه و فهرست همه‌ی تغییرات ساختار سازمانی (گره‌ها و پست‌ها) که بین دو تاریخ رخ داده‌اند —
    /// دقیقاً بر پایه‌ی همان تاریخچه‌ی ثبت‌شده (نه عکس‌گیری کامل از ساختار، که سنگین و پرریسک است).
    /// </summary>
    public async Task<HrMainCompareResultDto> CompareStructureAsync(DateTime from, DateTime to)
    {
        if (to < from) (from, to) = (to, from);
        var rows = await _db.HrMainChangeLogs.AsNoTracking()
            .Where(l => l.At >= from.Date && l.At < to.Date.AddDays(1))
            .OrderBy(l => l.At)
            .ToListAsync();

        var result = new HrMainCompareResultDto { From = from.Date, To = to.Date };
        foreach (var l in rows)
        {
            result.Items.Add(new HrMainCompareItemDto
            {
                Entity = l.Entity.ToString(), Action = l.Action.ToString(), EntityId = l.EntityId,
                EntityName = l.EntityName, FieldName = l.FieldName, OldValue = l.OldValue,
                NewValue = l.NewValue, At = l.At, ByUsername = l.ByUsername
            });
            if (l.Entity == HrMainChangeEntity.OrgNode)
            {
                if (l.Action == HrMainChangeAction.Create) result.NodesCreated++;
                else if (l.Action == HrMainChangeAction.Delete) result.NodesDeleted++;
                else if (l.FieldName != null) result.NodesUpdated++;
            }
            else
            {
                if (l.Action == HrMainChangeAction.Create) result.PositionsCreated++;
                else if (l.Action == HrMainChangeAction.Delete) result.PositionsDeleted++;
                else if (l.FieldName != null) result.PositionsUpdated++;
            }
        }
        return result;
    }

    // ==================== پست‌های سازمانی ====================

    public async Task<List<HrMainPositionDto>> ListPositionsAsync(int? orgNodeId, Paging.Request? pagination = null)
    {
        var q = _db.HrMainPositions.AsNoTracking().AsQueryable();
        if (orgNodeId is > 0) q = q.Where(p => p.OrgNodeId == orgNodeId.Value);
        var rows = await q.OrderBy(p => p.Title).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var nodeNames = await _db.HrMainOrgNodes.AsNoTracking()
            .ToDictionaryAsync(n => n.Id, n => n.Name);
        // تعداد پرسنل فعالِ منصوب‌شده به هر پست — برای محاسبه ظرفیت خالی (Vacancy)
        var assignedCounts = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.HrMainPositionId != null)
            .GroupBy(e => e.HrMainPositionId!.Value)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PositionId, x => x.Count);
        return rows.Select(p => new HrMainPositionDto
        {
            Id = p.Id, Code = p.Code, Title = p.Title, OrgNodeId = p.OrgNodeId,
            OrgNodeName = p.OrgNodeId is > 0 && nodeNames.TryGetValue(p.OrgNodeId.Value, out var nm) ? nm : null,
            Grade = p.Grade, JobDescription = p.JobDescription, Requirements = p.Requirements,
            HeadCount = p.HeadCount, IsActive = p.IsActive,
            AssignedCount = assignedCounts.TryGetValue(p.Id, out var cnt) ? cnt : 0
        }).ToList();
    }

    public async Task<HrMainPositionDto> SavePositionAsync(int? id, HrMainPositionSaveDto dto, int? byUserId = null, string? byUsername = null)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان پست سازمانی الزامی است.");
        if (dto.OrgNodeId is > 0 && !await _db.HrMainOrgNodes.AnyAsync(n => n.Id == dto.OrgNodeId.Value))
            throw new InvalidOperationException("گره سازمانی نامعتبر است.");

        var isNew = id is not > 0;
        HrMainPosition p;
        HrMainPosition? before = null;
        if (!isNew)
        {
            p = await _db.HrMainPositions.FirstOrDefaultAsync(x => x.Id == id!.Value)
                ?? throw new InvalidOperationException("پست سازمانی یافت نشد.");
            before = new HrMainPosition
            {
                Code = p.Code, Title = p.Title, OrgNodeId = p.OrgNodeId, Grade = p.Grade,
                HeadCount = p.HeadCount, IsActive = p.IsActive
            };
        }
        else
        {
            p = new HrMainPosition();
            _db.HrMainPositions.Add(p);
        }
        p.Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim();
        p.Title = dto.Title.Trim(); p.OrgNodeId = dto.OrgNodeId;
        p.Grade = dto.Grade?.Trim(); p.JobDescription = dto.JobDescription?.Trim();
        p.Requirements = dto.Requirements?.Trim();
        p.HeadCount = Math.Max(1, dto.HeadCount); p.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        if (isNew)
        {
            await LogChangeAsync(HrMainChangeEntity.Position, HrMainChangeAction.Create, p.Id, p.Title, null, null, null, byUserId, byUsername);
        }
        else
        {
            var beforeNodeName = before!.OrgNodeId is > 0
                ? await _db.HrMainOrgNodes.AsNoTracking().Where(x => x.Id == before.OrgNodeId!.Value).Select(x => x.Name).FirstOrDefaultAsync()
                : null;
            var afterNodeName = p.OrgNodeId is > 0
                ? await _db.HrMainOrgNodes.AsNoTracking().Where(x => x.Id == p.OrgNodeId!.Value).Select(x => x.Name).FirstOrDefaultAsync()
                : null;
            await LogFieldChangeAsync(HrMainChangeEntity.Position, p.Id, p.Title, "عنوان", before.Title, p.Title, byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.Position, p.Id, p.Title, "گره سازمانی", beforeNodeName ?? "—", afterNodeName ?? "—", byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.Position, p.Id, p.Title, "رتبه/گرید", before.Grade, p.Grade, byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.Position, p.Id, p.Title, "تعداد مصوب", before.HeadCount.ToString(), p.HeadCount.ToString(), byUserId, byUsername);
            await LogFieldChangeAsync(HrMainChangeEntity.Position, p.Id, p.Title, "وضعیت فعال", before.IsActive ? "فعال" : "غیرفعال", p.IsActive ? "فعال" : "غیرفعال", byUserId, byUsername);
        }
        return (await ListPositionsAsync(null)).First(x => x.Id == p.Id);
    }

    public async Task DeletePositionAsync(int id, int? byUserId = null, string? byUsername = null)
    {
        var p = await _db.HrMainPositions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پست سازمانی یافت نشد.");
        if (await _db.HrEmployees.AnyAsync(e => e.HrMainPositionId == id))
            throw new InvalidOperationException("پرسنلی به این پست منتسب است و قابل حذف نیست.");
        _db.HrMainPositions.Remove(p);
        await _db.SaveChangesAsync();
        await LogChangeAsync(HrMainChangeEntity.Position, HrMainChangeAction.Delete, p.Id, p.Title, null, null, null, byUserId, byUsername);
    }

    // ==================== زبان و تقویم ====================

    public async Task<HrMainLocaleDto> GetLocaleAsync()
    {
        var s = await _db.HrMainLocales.FirstOrDefaultAsync();
        return s is null ? new HrMainLocaleDto() : new HrMainLocaleDto { Language = s.Language, Calendar = s.Calendar };
    }

    public async Task<HrMainLocaleDto> SaveLocaleAsync(HrMainLocaleDto dto)
    {
        var lang = (dto.Language ?? "").Trim().ToLowerInvariant();
        var cal = (dto.Calendar ?? "").Trim().ToLowerInvariant();
        if (lang is not ("fa" or "en"))
            throw new InvalidOperationException("زبان نامعتبر است (فارسی یا انگلیسی).");
        if (cal is not ("jalali" or "gregorian"))
            throw new InvalidOperationException("تقویم نامعتبر است (شمسی یا میلادی).");
        var s = await _db.HrMainLocales.FirstOrDefaultAsync();
        if (s is null) { s = new HrMainLocale(); _db.HrMainLocales.Add(s); }
        s.Language = lang; s.Calendar = cal; s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new HrMainLocaleDto { Language = s.Language, Calendar = s.Calendar };
    }

    // ==================== قوانین پیش‌فرض ====================

    public async Task<HrMainRulesDto> GetRulesAsync()
    {
        var r = await _db.HrMainRules.FirstOrDefaultAsync();
        return r is null ? new HrMainRulesDto() : MapRules(r);
    }

    public async Task<HrMainRulesDto> SaveRulesAsync(HrMainRulesDto dto)
    {
        if (dto.AnnualLeaveDays is < 0 or > 60)
            throw new InvalidOperationException("مرخصی استحقاقی سالانه باید بین ۰ تا ۶۰ روز باشد.");
        if (dto.MaxConsecutiveLeaveDays is < 1 or > 60)
            throw new InvalidOperationException("حداکثر مرخصی پیوسته باید بین ۱ تا ۶۰ روز باشد.");
        if (dto.MaxCarryOverDays is < 0 or > 60)
            throw new InvalidOperationException("سقف انتقال مرخصی باید بین ۰ تا ۶۰ روز باشد.");
        if (dto.LateGraceMinutes is < 0 or > 180)
            throw new InvalidOperationException("ارفاق تأخیر باید بین ۰ تا ۱۸۰ دقیقه باشد.");
        if (dto.MonthlyAllowedLateMinutes is < 0 or > 3000)
            throw new InvalidOperationException("سقف تأخیر ماهانه نامعتبر است.");
        if (dto.MaxLateWithoutLeaveMinutes is < 0 or > 1440)
            throw new InvalidOperationException("حداکثر تأخیر بدون برگه نامعتبر است.");
        if (dto.LateDeductionFactor is < 0 or > 10)
            throw new InvalidOperationException("ضریب کسرکار تأخیر باید بین ۰ تا ۱۰ باشد.");
        if (dto.AbsenceDailyDeductionFactor is < 0 or > 10)
            throw new InvalidOperationException("ضریب کسر غیبت باید بین ۰ تا ۱۰ باشد.");
        if (dto.UnexcusedAbsenceWarningAfter is < 1 or > 30)
            throw new InvalidOperationException("آستانه اخطار غیبت باید بین ۱ تا ۳۰ باشد.");
        if (dto.OvertimeFactor is < 1 or > 5)
            throw new InvalidOperationException("ضریب اضافه‌کاری باید بین ۱ تا ۵ باشد (قانون کار: ۱.۴).");
        if (dto.MaxMonthlyOvertimeHours is < 0 or > 300)
            throw new InvalidOperationException("سقف اضافه‌کاری ماهانه نامعتبر است.");
        if (dto.ContractAlertDays is < 1 or > 365)
            throw new InvalidOperationException("آستانه هشدار انقضای قرارداد باید بین ۱ تا ۳۶۵ روز باشد.");
        if (dto.DefaultWorkStartTime == dto.DefaultWorkEndTime)
            throw new InvalidOperationException("ساعت شروع و پایان کار پیش‌فرض نمی‌توانند یکسان باشند.");
        if (!string.IsNullOrWhiteSpace(dto.DefaultWeeklyOffDays))
        {
            var parts = dto.DefaultWeeklyOffDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Any(p => !int.TryParse(p, out var d) || d is < 0 or > 6))
                throw new InvalidOperationException("روزهای تعطیل هفتگی نامعتبر است (اعداد ۰ تا ۶ با کاما).");
        }

        var r = await _db.HrMainRules.FirstOrDefaultAsync();
        if (r is null) { r = new HrMainRules(); _db.HrMainRules.Add(r); }
        r.AnnualLeaveDays = dto.AnnualLeaveDays;
        r.MaxConsecutiveLeaveDays = dto.MaxConsecutiveLeaveDays;
        r.MaxCarryOverDays = dto.MaxCarryOverDays;
        r.UnpaidLeaveAllowed = dto.UnpaidLeaveAllowed;
        r.LateGraceMinutes = dto.LateGraceMinutes;
        r.MonthlyAllowedLateMinutes = dto.MonthlyAllowedLateMinutes;
        r.MaxLateWithoutLeaveMinutes = dto.MaxLateWithoutLeaveMinutes;
        r.LateDeductionFactor = dto.LateDeductionFactor;
        r.AbsenceDailyDeductionFactor = dto.AbsenceDailyDeductionFactor;
        r.UnexcusedAbsenceWarningAfter = dto.UnexcusedAbsenceWarningAfter;
        r.OvertimeFactor = dto.OvertimeFactor;
        r.MaxMonthlyOvertimeHours = dto.MaxMonthlyOvertimeHours;
        r.OvertimeNeedsApproval = dto.OvertimeNeedsApproval;
        r.ContractAlertDays = dto.ContractAlertDays;
        r.DefaultWorkStartTime = dto.DefaultWorkStartTime;
        r.DefaultWorkEndTime = dto.DefaultWorkEndTime;
        r.DefaultWeeklyOffDays = dto.DefaultWeeklyOffDays?.Trim();
        r.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return MapRules(r);
    }

    private static HrMainRulesDto MapRules(HrMainRules r) => new()
    {
        AnnualLeaveDays = r.AnnualLeaveDays, MaxConsecutiveLeaveDays = r.MaxConsecutiveLeaveDays,
        MaxCarryOverDays = r.MaxCarryOverDays, UnpaidLeaveAllowed = r.UnpaidLeaveAllowed,
        LateGraceMinutes = r.LateGraceMinutes, MonthlyAllowedLateMinutes = r.MonthlyAllowedLateMinutes,
        MaxLateWithoutLeaveMinutes = r.MaxLateWithoutLeaveMinutes, LateDeductionFactor = r.LateDeductionFactor,
        AbsenceDailyDeductionFactor = r.AbsenceDailyDeductionFactor,
        UnexcusedAbsenceWarningAfter = r.UnexcusedAbsenceWarningAfter,
        OvertimeFactor = r.OvertimeFactor, MaxMonthlyOvertimeHours = r.MaxMonthlyOvertimeHours,
        OvertimeNeedsApproval = r.OvertimeNeedsApproval,
        ContractAlertDays = r.ContractAlertDays,
        DefaultWorkStartTime = r.DefaultWorkStartTime,
        DefaultWorkEndTime = r.DefaultWorkEndTime,
        DefaultWeeklyOffDays = r.DefaultWeeklyOffDays
    };

    // ==================== تعطیلات (جدول اختصاصی HrMainHolidays — جدا از CompanyHolidays فروغ آریا) ====================

    public async Task<List<HrMainHolidayDto>> ListHolidaysAsync(int? jalaliYear, Paging.Request? pagination = null)
    {
        var jy = jalaliYear is >= 1300 and <= 1500
            ? jalaliYear.Value
            : PersianDate.FromGregorian(DateTime.Today).Year;
        var start = PersianDate.ToGregorian(jy, 1, 1);
        var end = PersianDate.ToGregorian(jy, 12, PersianDate.DaysInMonth(jy, 12)).Date.AddDays(1);
        if (start == DateTime.MinValue) start = new DateTime(DateTime.Today.Year, 1, 1);
        if (end == DateTime.MinValue) end = start.AddYears(1);
        return await _db.HrMainHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= start && h.HolidayDate < end)
            .OrderBy(h => h.HolidayDate).ThenBy(x => x.Id)
            .Select(h => new HrMainHolidayDto
            {
                Id = h.Id, Date = h.HolidayDate, Name = h.Name,
                IsOfficial = h.IsOfficial, CreatedByName = h.CreatedByName
            })
            .ToPageListAsync(pagination);
    }

    public async Task<HrMainHolidayDto> SaveHolidayAsync(HrMainHolidaySaveDto dto, string createdBy)
    {
        var date = dto.Date == default ? DateTime.Today : dto.Date.Date;
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("عنوان تعطیلی الزامی است.");
        if (await _db.HrMainHolidays.AnyAsync(h => h.HolidayDate == date))
            throw new InvalidOperationException("برای این تاریخ قبلاً تعطیلی ثبت شده است.");
        var h = new HrMainHoliday
        {
            HolidayDate = date, Name = dto.Name.Trim(),
            IsOfficial = dto.IsOfficial, CreatedByName = createdBy
        };
        _db.HrMainHolidays.Add(h);
        await _db.SaveChangesAsync();
        return new HrMainHolidayDto
        {
            Id = h.Id, Date = h.HolidayDate, Name = h.Name,
            IsOfficial = h.IsOfficial, CreatedByName = h.CreatedByName
        };
    }

    public async Task DeleteHolidayAsync(int id)
    {
        var h = await _db.HrMainHolidays.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تعطیلی یافت نشد.");
        _db.HrMainHolidays.Remove(h);
        await _db.SaveChangesAsync();
    }

    // ==================== نمای کلی ====================

    public async Task<HrMainOverviewDto> OverviewAsync()
    {
        var jy = PersianDate.FromGregorian(DateTime.Today).Year;
        var holidays = await ListHolidaysAsync(jy);
        var c = await _db.HrMainCompanies.AsNoTracking().FirstOrDefaultAsync();

        // مجموع ظرفیت خالی پست‌های فعال (مصوب منهای منصوب، هرگز منفی نیست)
        var activePositions = await _db.HrMainPositions.AsNoTracking()
            .Where(p => p.IsActive).Select(p => new { p.Id, p.HeadCount }).ToListAsync();
        var assignedCounts = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.HrMainPositionId != null)
            .GroupBy(e => e.HrMainPositionId!.Value)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PositionId, x => x.Count);
        var vacantTotal = activePositions.Sum(p =>
            Math.Max(0, p.HeadCount - (assignedCounts.TryGetValue(p.Id, out var cnt) ? cnt : 0)));

        return new HrMainOverviewDto
        {
            CompanyName = string.IsNullOrWhiteSpace(c?.Name) ? null : c!.Name,
            LogoPath = c?.LogoPath,
            ActiveBranches = await _db.HrMainBranches.CountAsync(b => b.IsActive),
            ActiveNodes = await _db.HrMainOrgNodes.CountAsync(n => n.IsActive),
            ActivePositions = await _db.HrMainPositions.CountAsync(p => p.IsActive),
            ActiveEmployees = await _db.HrEmployees.CountAsync(e => e.IsActive),
            VacantPositions = vacantTotal,
            HolidaysThisYear = holidays.Count,
            CurrentJalaliYear = jy
        };
    }
}
