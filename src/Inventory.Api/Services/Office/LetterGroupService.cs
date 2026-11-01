using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس گروه‌های گیرندگان — پورت GroupService طرح کارفرما
//  (CheckGroupIdAsync / GetUserByGroupIdAsync) به‌صورت کاربرمحور؛
//  در فاز چارت سازمانی به عضویت سمت‌محور (SematGroups) ارتقا می‌یابد.
// ============================================================

public interface ILetterGroupService
{
    Task<List<LetterGroupDto>> GetAllAsync(bool includeMembers, Paging.Request? pagination = null);
    Task<LetterGroupDto?> GetAsync(int groupId);
    Task<int> SaveAsync(SaveLetterGroupDto dto, int userId);
    Task DeleteAsync(int groupId, int userId, bool isAdmin);

    /// <summary>معادل CheckGroupIdAsync طرح کارفرما — همه شناسه‌ها باید گروه فعال باشند</summary>
    Task<bool> CheckGroupIdsAsync(List<int> groupIds);

    /// <summary>معادل GetUserByGroupIdAsync — باز کردن گروه‌ها به شناسه کاربران عضو (بدون تکرار)</summary>
    Task<List<int>> ExpandToUserIdsAsync(List<int> groupIds);
}

public class LetterGroupService : ILetterGroupService
{
    private readonly AppDbContext _db;
    public LetterGroupService(AppDbContext db) => _db = db;

    private static string FullName(User? u) =>
        u == null ? "" :
        string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
            ? u.Username
            : $"{u.FirstName} {u.LastName}".Trim();

    public async Task<List<LetterGroupDto>> GetAllAsync(bool includeMembers, Paging.Request? pagination = null)
    {
        var q = _db.LetterGroups.AsNoTracking()
            .Where(g => !g.IsDelete && g.Condition)
            .OrderBy(g => g.NameGroup).ThenBy(g => g.GroupId);

        if (!includeMembers)
            return await q.Select(g => new LetterGroupDto
            {
                GroupId = g.GroupId,
                NameGroup = g.NameGroup,
                Condition = g.Condition,
                MemberCount = g.Members.Count
            }).ToPageListAsync(pagination);

        var groups = await q.Include(g => g.Members).ThenInclude(m => m.User).ToPageListAsync(pagination);
        return groups.Select(g => new LetterGroupDto
        {
            GroupId = g.GroupId,
            NameGroup = g.NameGroup,
            Condition = g.Condition,
            MemberCount = g.Members.Count,
            Members = g.Members
                .Where(m => m.User != null && m.User.IsActive)
                .Select(m => new LetterReciverDto { UserId = m.UserId, FullName = FullName(m.User) })
                .ToList()
        }).ToList();
    }

    public async Task<LetterGroupDto?> GetAsync(int groupId)
    {
        var g = await _db.LetterGroups.AsNoTracking()
            .Include(x => x.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(x => x.GroupId == groupId && !x.IsDelete);
        if (g == null) return null;

        return new LetterGroupDto
        {
            GroupId = g.GroupId,
            NameGroup = g.NameGroup,
            Condition = g.Condition,
            MemberCount = g.Members.Count,
            Members = g.Members
                .Select(m => new LetterReciverDto { UserId = m.UserId, FullName = FullName(m.User) })
                .ToList()
        };
    }

    public async Task<int> SaveAsync(SaveLetterGroupDto dto, int userId)
    {
        if (string.IsNullOrWhiteSpace(dto.NameGroup))
            throw new Exception("نام گروه الزامی است.");

        var memberIds = dto.MemberUserIds.Distinct().ToList();
        if (memberIds.Count == 0)
            throw new Exception("حداقل یک عضو برای گروه انتخاب کنید.");

        var validCount = await _db.Users.CountAsync(u => memberIds.Contains(u.Id) && u.IsActive);
        if (validCount != memberIds.Count)
            throw new Exception("برخی از اعضای انتخاب‌شده معتبر یا فعال نیستند.");

        var name = dto.NameGroup.Trim();
        var dup = await _db.LetterGroups.AnyAsync(g =>
            g.GroupId != dto.GroupId && !g.IsDelete && g.NameGroup == name);
        if (dup) throw new Exception("گروهی با این نام قبلاً ثبت شده است.");

        LetterGroup group;
        if (dto.GroupId > 0)
        {
            group = await _db.LetterGroups.Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.GroupId == dto.GroupId && !g.IsDelete)
                ?? throw new Exception("گروه پیدا نشد.");
            group.NameGroup = name;
            _db.LetterGroupMembers.RemoveRange(group.Members);
        }
        else
        {
            group = new LetterGroup { NameGroup = name, Condition = true, CreatorUserId = userId };
            _db.LetterGroups.Add(group);
        }

        foreach (var uid in memberIds)
            group.Members.Add(new LetterGroupMember { UserId = uid });

        await _db.SaveChangesAsync();
        return group.GroupId;
    }

    public async Task DeleteAsync(int groupId, int userId, bool isAdmin)
    {
        var group = await _db.LetterGroups
            .FirstOrDefaultAsync(g => g.GroupId == groupId && !g.IsDelete)
            ?? throw new Exception("گروه پیدا نشد.");

        if (group.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط سازنده گروه یا مدیر می‌تواند گروه را حذف کند.");

        group.IsDelete = true; // حذف نرم — مطابق الگوی IsDelete طرح کارفرما
        await _db.SaveChangesAsync();
    }

    public async Task<bool> CheckGroupIdsAsync(List<int> groupIds)
    {
        if (groupIds == null || groupIds.Count == 0) return true;
        var ids = groupIds.Distinct().ToList();
        var existing = await _db.LetterGroups
            .CountAsync(g => ids.Contains(g.GroupId) && g.Condition && !g.IsDelete);
        return existing == ids.Count;
    }

    public async Task<List<int>> ExpandToUserIdsAsync(List<int> groupIds)
    {
        if (groupIds == null || groupIds.Count == 0) return new List<int>();
        return await _db.LetterGroupMembers
            .Where(m => groupIds.Contains(m.GroupId)
                        && !m.Group!.IsDelete && m.Group.Condition
                        && m.User!.IsActive)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();
    }
}
