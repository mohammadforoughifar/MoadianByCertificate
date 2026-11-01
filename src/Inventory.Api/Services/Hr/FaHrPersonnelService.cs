using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// ================== پرسنل و چارت «منابع انسانی فروغ آریا» ==================
/// فقط روی جدول اختصاصی <see cref="FaHrPersonnel"/> کار می‌کند (هیچ ارجاعی به HrEmployees منابع انسانی اصلی ندارد).
/// <para>
/// قاعدهٔ نمایش: پرسنل فقط وقتی دیده می‌شود که از طریق HrUserLinks به یک کاربرِ <b>فعالِ</b> دارای
/// <b>دسترسی</b> به این ماژول پیوند داشته باشد (<see cref="ForoughAryaHrUsers"/>). به همین دلیل
/// ثبت پرسنل جدید بدون انتخاب «کاربر سیستم» ممکن نیست — پیوند همزمان با ثبت ایجاد می‌شود.
/// </para>
/// </summary>
public sealed class FaHrPersonnelService
{
    private readonly AppDbContext _db;
    private readonly ForoughAryaHrUsers _access;

    public FaHrPersonnelService(AppDbContext db, ForoughAryaHrUsers access) { _db = db; _access = access; }

    // ================= فهرست / مشاهده =================

    public async Task<List<FaHrPersonnelDto>> ListAsync(string? search, int? status, Paging.Request? pagination = null)
    {
        var map = await _access.PersonnelUserMapAsync();
        var ids = map.Keys.ToList();
        var q = _db.FaHrPersonnel.AsNoTracking().Where(e => ids.Contains(e.Id));
        if (status != null) q = q.Where(e => (int)e.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.FirstName.Contains(s) || e.LastName.Contains(s) || e.Code.Contains(s)
                             || e.NationalCode.Contains(s) || (e.PostTitle != null && e.PostTitle.Contains(s))
                             || (e.UnitTitle != null && e.UnitTitle.Contains(s)));
        }
        var rows = await q.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.Id).ToPageListAsync(pagination);
        return await ToDtosAsync(rows, map);
    }

    public async Task<FaHrPersonnelDto?> GetAsync(int id)
    {
        var map = await _access.PersonnelUserMapAsync();
        if (!map.ContainsKey(id)) return null;
        var e = await _db.FaHrPersonnel.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e == null ? null : (await ToDtosAsync(new List<FaHrPersonnel> { e }, map)).First();
    }

    private async Task<List<FaHrPersonnelDto>> ToDtosAsync(List<FaHrPersonnel> rows, Dictionary<int, int> map)
    {
        var mgrIds = rows.Where(r => r.ManagerId != null).Select(r => r.ManagerId!.Value).Distinct().ToList();
        // نام مدیر فقط اگر خودِ مدیر هم «مجاز» باشد نمایش داده می‌شود
        var mgrs = await _db.FaHrPersonnel.AsNoTracking().Where(m => mgrIds.Contains(m.Id))
            .Select(m => new { m.Id, m.FirstName, m.LastName }).ToListAsync();
        var mgrNames = mgrs.Where(m => map.ContainsKey(m.Id)).ToDictionary(m => m.Id, m => $"{m.FirstName} {m.LastName}".Trim());
        var userIds = rows.Select(r => map.TryGetValue(r.Id, out var u) ? u : 0).Where(u => u > 0).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username);
        return rows.Select(e =>
        {
            map.TryGetValue(e.Id, out var uid);
            return new FaHrPersonnelDto
            {
                Id = e.Id, Code = e.Code, FirstName = e.FirstName, LastName = e.LastName, NationalCode = e.NationalCode,
                BirthDate = e.BirthDate, Gender = e.Gender, MaritalStatus = e.MaritalStatus, Mobile = e.Mobile,
                Email = e.Email, Address = e.Address, HireDate = e.HireDate, UnitTitle = e.UnitTitle,
                PostTitle = e.PostTitle, ManagerId = e.ManagerId,
                ManagerName = e.ManagerId != null && mgrNames.TryGetValue(e.ManagerId.Value, out var mn) ? mn : null,
                EmploymentType = e.EmploymentType, Status = (int)e.Status, BaseSalary = e.BaseSalary,
                IsActive = e.IsActive, Sheba = e.Sheba, BankName = e.BankName, InsuranceNo = e.InsuranceNo,
                UserId = uid, Username = users.TryGetValue(uid, out var un) ? un : null,
            };
        }).ToList();
    }

    /// <summary>کمبوی «کاربر سیستم»: فقط کاربرانِ فعالِ دارای دسترسی به ماژول.</summary>
    public async Task<List<FaHrUserOptionDto>> UserOptionsAsync()
    {
        var allowed = (await _access.UserIdsAsync()).ToList();
        var links = await _db.HrUserLinks.AsNoTracking().Where(l => allowed.Contains(l.UserId))
            .Select(l => new { l.UserId, l.EmployeeId }).ToListAsync();
        var existing = (await _db.FaHrPersonnel.AsNoTracking().Select(e => e.Id).ToListAsync()).ToHashSet();
        var linkMap = new Dictionary<int, int>();
        foreach (var l in links.Where(l => existing.Contains(l.EmployeeId))) linkMap.TryAdd(l.UserId, l.EmployeeId);
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive && allowed.Contains(u.Id))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName }).ToListAsync();
        return users.Select(u => new FaHrUserOptionDto
        {
            Id = u.Id, Username = u.Username,
            FullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
            PersonnelId = linkMap.TryGetValue(u.Id, out var pid) ? pid : null,
        }).ToList();
    }

    // ================= پرسنل در انتظار پیوند (فقط مدیر) =================

    /// <summary>
    /// پرسنلی که به کاربرِ فعالِ دارای دسترسی پیوند ندارند — در هیچ بخش دیگری از ماژول دیده نمی‌شوند؛
    /// این فهرست فقط برای برقراری پیوند است.
    /// </summary>
    public async Task<List<FaHrPendingPersonnelDto>> PendingAsync()
    {
        var map = await _access.PersonnelUserMapAsync();
        var ids = map.Keys.ToList();
        var rows = await _db.FaHrPersonnel.AsNoTracking().Where(e => !ids.Contains(e.Id))
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync();
        var rowIds = rows.Select(r => r.Id).ToList();
        var links = await _db.HrUserLinks.AsNoTracking().Where(l => rowIds.Contains(l.EmployeeId))
            .Select(l => new { l.EmployeeId, l.UserId }).ToListAsync();
        var linkUserIds = links.Select(l => l.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => linkUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.Username, u.IsActive });
        return rows.Select(e =>
        {
            var l = links.FirstOrDefault(x => x.EmployeeId == e.Id);
            string? un = null; var reason = "بدون پیوند به کاربر";
            if (l != null && users.TryGetValue(l.UserId, out var u))
            {
                un = u.Username;
                reason = u.IsActive ? "کاربر به این ماژول دسترسی ندارد" : "کاربر غیرفعال است";
            }
            return new FaHrPendingPersonnelDto
            {
                Id = e.Id, Code = e.Code, FullName = $"{e.FirstName} {e.LastName}".Trim(),
                NationalCode = e.NationalCode, Status = (int)e.Status, Username = un, Reason = reason,
            };
        }).ToList();
    }

    /// <summary>پیوند پرسنل (حتی پرسنلِ فعلاً نامرئی) به یک کاربرِ فعالِ دارای دسترسی.</summary>
    public async Task<FaHrPersonnelDto> LinkUserAsync(int personnelId, int userId)
    {
        if (!await _db.FaHrPersonnel.AnyAsync(e => e.Id == personnelId))
            throw new InvalidOperationException("پرسنل پیدا نشد.");
        if (userId <= 0 || !await _access.IsAllowedUserAsync(userId))
            throw new InvalidOperationException("فقط کاربرانِ فعالِ دارای دسترسی به «منابع انسانی فروغ آریا» قابل پیوندند.");
        var otherLink = await _db.HrUserLinks.AsNoTracking()
            .Where(l => l.UserId == userId && l.EmployeeId != personnelId)
            .Select(l => (int?)l.EmployeeId).FirstOrDefaultAsync();
        if (otherLink != null && await _db.FaHrPersonnel.AnyAsync(e => e.Id == otherLink))
            throw new InvalidOperationException("این کاربر قبلاً به پرسنل دیگری پیوند خورده است.");
        await LinkAsync(personnelId, userId);
        return (await GetAsync(personnelId))!;
    }

    // ================= ثبت / ویرایش / حذف =================

    public async Task<FaHrPersonnelDto> CreateAsync(FaHrPersonnelSaveDto dto)
    {
        await ValidateAsync(null, dto);
        var e = new FaHrPersonnel { CreatedAt = DateTime.Now };
        Apply(e, dto);
        e.Code = string.IsNullOrWhiteSpace(dto.Code) ? await NextCodeAsync() : dto.Code.Trim();
        _db.FaHrPersonnel.Add(e);
        await _db.SaveChangesAsync();
        await LinkAsync(e.Id, dto.UserId);
        return (await GetAsync(e.Id))!;
    }

    public async Task<FaHrPersonnelDto> UpdateAsync(int id, FaHrPersonnelSaveDto dto)
    {
        if (!await _access.IsAllowedPersonnelAsync(id)) throw new InvalidOperationException("پرسنل پیدا نشد.");
        var e = await _db.FaHrPersonnel.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پرسنل پیدا نشد.");
        await ValidateAsync(id, dto);
        Apply(e, dto);
        if (!string.IsNullOrWhiteSpace(dto.Code)) e.Code = dto.Code.Trim();
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await LinkAsync(e.Id, dto.UserId);
        return (await GetAsync(e.Id))!;
    }

    public async Task DeleteAsync(int id)
    {
        if (!await _access.IsAllowedPersonnelAsync(id)) throw new InvalidOperationException("پرسنل پیدا نشد.");
        var e = await _db.FaHrPersonnel.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پرسنل پیدا نشد.");
        var used = await _db.HrPaySlips.AnyAsync(x => x.EmployeeId == id)
                   || await _db.HrPayLoans.AnyAsync(x => x.EmployeeId == id)
                   || await _db.HrPaySettlements.AnyAsync(x => x.EmployeeId == id)
                   || await _db.HrPerfScores.AnyAsync(x => x.EmployeeId == id)
                   || await _db.HrPerfResults.AnyAsync(x => x.EmployeeId == id);
        if (used)
            throw new InvalidOperationException("این پرسنل فیش حقوق، وام، تسویه یا ارزیابی دارد و قابل حذف نیست؛ وضعیت را «پایان همکاری» کنید.");
        await _db.FaHrPersonnel.Where(x => x.ManagerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ManagerId, (int?)null));
        _db.HrUserLinks.RemoveRange(_db.HrUserLinks.Where(l => l.EmployeeId == id));
        _db.HrPayProfiles.RemoveRange(_db.HrPayProfiles.Where(p => p.EmployeeId == id));
        _db.HrPayEmployeeItems.RemoveRange(_db.HrPayEmployeeItems.Where(p => p.EmployeeId == id));
        _db.FaHrPersonnel.Remove(e);
        await _db.SaveChangesAsync();
        _access.InvalidatePersonnel();
    }

    private static void Apply(FaHrPersonnel e, FaHrPersonnelSaveDto d)
    {
        e.FirstName = d.FirstName.Trim(); e.LastName = d.LastName.Trim(); e.NationalCode = d.NationalCode.Trim();
        e.BirthDate = d.BirthDate?.Date; e.Gender = d.Gender is 0 or 1 ? d.Gender : 0;
        e.MaritalStatus = d.MaritalStatus is 0 or 1 ? d.MaritalStatus : 0;
        e.Mobile = Clean(d.Mobile); e.Email = Clean(d.Email); e.Address = Clean(d.Address);
        e.HireDate = d.HireDate == default ? DateTime.Today : d.HireDate.Date;
        e.UnitTitle = Clean(d.UnitTitle); e.PostTitle = Clean(d.PostTitle);
        e.ManagerId = d.ManagerId is > 0 ? d.ManagerId : null;
        e.EmploymentType = Math.Clamp(d.EmploymentType, 0, 6);
        e.Status = Enum.IsDefined(typeof(FaHrPersonnelStatus), d.Status) ? (FaHrPersonnelStatus)d.Status : FaHrPersonnelStatus.Active;
        e.BaseSalary = Math.Max(0, d.BaseSalary); e.IsActive = d.IsActive;
        e.Sheba = Clean(d.Sheba)?.Replace(" ", "").ToUpperInvariant(); e.BankName = Clean(d.BankName); e.InsuranceNo = Clean(d.InsuranceNo);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task ValidateAsync(int? id, FaHrPersonnelSaveDto d)
    {
        if (string.IsNullOrWhiteSpace(d.FirstName) || string.IsNullOrWhiteSpace(d.LastName))
            throw new InvalidOperationException("نام و نام خانوادگی الزامی است.");
        if (!HrTimeService.IsValidIranianNationalCode(d.NationalCode))
            throw new InvalidOperationException("کد ملی معتبر نیست (رقم کنترل اشتباه است).");
        var nc = d.NationalCode.Trim();
        if (await _db.FaHrPersonnel.AnyAsync(e => e.NationalCode == nc && e.Id != (id ?? 0)))
            throw new InvalidOperationException("این کد ملی برای پرسنل دیگری ثبت شده است.");
        if (!string.IsNullOrWhiteSpace(d.Code))
        {
            var code = d.Code.Trim();
            if (code.Length > 20) throw new InvalidOperationException("کد پرسنلی حداکثر ۲۰ کاراکتر است.");
            if (await _db.FaHrPersonnel.AnyAsync(e => e.Code == code && e.Id != (id ?? 0)))
                throw new InvalidOperationException("این کد پرسنلی تکراری است.");
        }
        if (d.UserId <= 0 || !await _access.IsAllowedUserAsync(d.UserId))
            throw new InvalidOperationException("کاربر سیستم را انتخاب کنید؛ فقط کاربرانِ فعالِ دارای دسترسی به «منابع انسانی فروغ آریا» مجازند.");
        var otherLink = await _db.HrUserLinks.AsNoTracking()
            .Where(l => l.UserId == d.UserId && l.EmployeeId != (id ?? 0))
            .Select(l => (int?)l.EmployeeId).FirstOrDefaultAsync();
        if (otherLink != null && await _db.FaHrPersonnel.AnyAsync(e => e.Id == otherLink))
            throw new InvalidOperationException("این کاربر قبلاً به پرسنل دیگری پیوند خورده است.");
        if (d.ManagerId is > 0)
        {
            if (id != null && d.ManagerId == id) throw new InvalidOperationException("پرسنل نمی‌تواند مدیر خودش باشد.");
            if (!await _access.IsAllowedPersonnelAsync(d.ManagerId.Value))
                throw new InvalidOperationException("مدیر مستقیم معتبر نیست (باید پرسنلِ پیوندخورده به کاربرِ فعالِ دارای دسترسی باشد).");
            if (id != null)
            {
                // جلوگیری از چرخه در چارت
                var parents = await _db.FaHrPersonnel.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.ManagerId);
                var cur = d.ManagerId; var guard = 0;
                while (cur != null && guard++ < 1000)
                {
                    if (cur == id) throw new InvalidOperationException("این انتخاب مدیر، چرخه در چارت سازمانی ایجاد می‌کند.");
                    cur = parents.TryGetValue(cur.Value, out var p) ? p : null;
                }
            }
        }
    }

    private async Task LinkAsync(int personnelId, int userId)
    {
        var olds = await _db.HrUserLinks.Where(x => x.EmployeeId == personnelId || x.UserId == userId).ToListAsync();
        if (olds.Count == 1 && olds[0].EmployeeId == personnelId && olds[0].UserId == userId) { _access.InvalidatePersonnel(); return; }
        _db.HrUserLinks.RemoveRange(olds);
        _db.HrUserLinks.Add(new HrUserLink { EmployeeId = personnelId, UserId = userId });
        await _db.SaveChangesAsync();
        _access.InvalidatePersonnel();
    }

    private async Task<string> NextCodeAsync()
    {
        var codes = await _db.FaHrPersonnel.AsNoTracking().Select(e => e.Code).ToListAsync();
        var max = codes.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(1000).Max();
        return (Math.Max(max, 1000) + 1).ToString();
    }

    // ================= چارت سازمانی (بر اساس مدیر مستقیم) =================

    public async Task<List<FaHrOrgNodeDto>> OrgTreeAsync(bool includeInactive)
    {
        var ids = (await _access.PersonnelIdsAsync()).ToList();
        var q = _db.FaHrPersonnel.AsNoTracking().Where(e => ids.Contains(e.Id));
        if (!includeInactive) q = q.Where(e => e.Status != FaHrPersonnelStatus.Terminated && e.Status != FaHrPersonnelStatus.Retired);
        var rows = await q.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync();
        var nodes = rows.ToDictionary(e => e.Id, e => new FaHrOrgNodeDto
        {
            Id = e.Id, FullName = $"{e.FirstName} {e.LastName}".Trim(), Code = e.Code,
            PostTitle = e.PostTitle, UnitTitle = e.UnitTitle, ManagerId = e.ManagerId, Status = (int)e.Status,
        });
        var roots = new List<FaHrOrgNodeDto>();
        foreach (var n in nodes.Values)
        {
            // مدیرِ نامجاز/حذف‌شده → گره ریشه
            if (n.ManagerId != null && n.ManagerId != n.Id && nodes.TryGetValue(n.ManagerId.Value, out var parent) && !IsAncestor(n, parent, nodes))
                parent.Children.Add(n);
            else roots.Add(n);
        }
        return roots;
    }

    private static bool IsAncestor(FaHrOrgNodeDto node, FaHrOrgNodeDto parent, Dictionary<int, FaHrOrgNodeDto> all)
    {
        // آیا node جدِّ parent است؟ (برای ایمنی در برابر داده‌های چرخه‌دار قدیمی)
        var cur = parent.ManagerId; var guard = 0;
        while (cur != null && guard++ < 1000)
        {
            if (cur == node.Id) return true;
            cur = all.TryGetValue(cur.Value, out var p) ? p.ManagerId : null;
        }
        return false;
    }
}
