using System.Globalization;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>پروژه کاری — پوشه سبک روی دستورهای کار (مدت‌زمان، تمدید، اعضا، پیشرفت وزنی خودکار).</summary>
[ApiController]
[Route("api/workprojects")]
[Authorize]
public class WorkProjectsController : ControllerBase
{
    private const string Module = "WorkProjects";
    private const int MaxExtension = 5;
    private static readonly HashSet<string> MindFlowColors = new(StringComparer.OrdinalIgnoreCase)
    { "#596cf3", "#e28b4d", "#32a783", "#ae6fc7", "#d85d75", "#318eae" };

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    public WorkProjectsController(AppDbContext db, INotifyService notify)
    { _db = db; _notify = notify; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsLegacyAdmin => User.IsInRole("Admin");

    private async Task<int?> ActiveCompanyIdAsync()
    {
        if (!int.TryParse(Request.Headers["X-Company-Id"].FirstOrDefault(), out var id) || id <= 0) return null;
        return await _db.UserCompanyAccesses.AnyAsync(x => x.UserId == MyUserId && x.CompanyId == id) ? id : null;
    }

    private async Task<string> MyDisplayNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var n = UserDisplay.Name(me);
        return string.IsNullOrWhiteSpace(n) ? MyUsername : n;
    }

    /// <summary>بررسی مجوز با ماژول دلخواه (مثلاً «دستور کار» برای کمبوی پروژه در فیلتر).</summary>
    private async Task<bool> HasAsyncIn(string module, string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "View";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action);
    }

    /// <summary>مثل دستور کار: ادمین قدیمی همه‌چیز، اپراتور قدیمی مشاهده/ساخت، بقیه از RBAC.</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            // اپراتور قدیمی: مثل قبل ساخت/مشاهده — به‌علاوهٔ همهٔ «بخش»های پروژه،
            // وگرنه بعد از افزودن مجوزهای بخشی، تب‌های پروژه برای این نقش‌ها غیب می‌شدند.
            if (legacy == "Operator") return action is "Create" or "View"
                or "Summary" or "Orders" or "MindFlow" or "Members" or "Activity";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == Module && p.Action == action);
    }

    /// <summary>
    /// دسترسی به یکی از «قسمت»های صفحهٔ پروژه: Summary / Orders / MindFlow / Members / Activity.
    /// این مجوزها در «تنظیمات ← نقش‌ها ← دستور کار، پروژه‌ها و نگار ذهنی» جداگانه اعطا می‌شوند.
    /// </summary>
    private Task<bool> CanSeeSectionAsync(string action) => HasAsync(action);

    /// <summary>ویرایش/تمدید/بستن/اعضا: مالک (مدیر پروژه) همیشه مجاز است، بقیه با مجوز Update.</summary>
    private async Task<bool> CanManageAsync(WorkProject p)
        => p.OwnerUserId == MyUserId || IsLegacyAdmin || await HasAsync("Update");

    /// <summary>پروژه‌گردان‌ها، مدیر پروژه یا دعوت‌شدهٔ مستقیم به همین MindFlow.</summary>
    private async Task<bool> CanViewMindFlowAsync(WorkProject p)
        => p.OwnerUserId == MyUserId
            || await _db.WorkProjectMembers.AnyAsync(m => m.ProjectId == p.Id && m.UserId == MyUserId)
            || await _db.WorkProjectMindFlowMembers.AnyAsync(m => m.ProjectId == p.Id && m.UserId == MyUserId);

    private async Task<bool> CanManageMindFlowAsync(WorkProject p)
    {
        var inProject = p.OwnerUserId == MyUserId
            || await _db.WorkProjectMembers.AnyAsync(m => m.ProjectId == p.Id && m.UserId == MyUserId);
        return inProject && await CanManageAsync(p);
    }

    private void Log(int projectId, string action, string? text) =>
        _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = projectId, ActorName = MyUsername, Action = action, Text = text });

    private static string ToFa(DateTime d)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(d)}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00}";
    }

    private static string? NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null) return null;
        var list = tags.Select(t => (t ?? "").Trim().Replace(",", "،"))
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 30 ? t[..30] : t)
            .Distinct().Take(5).ToList();
        return list.Count == 0 ? null : "," + string.Join(",", list) + ",";
    }

    private static List<string> TagsToList(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>پروژه بازِ حذف‌نشدهٔ شرکت فعال (رکورد حذف‌شده با فیلتر سراسری دیده نمی‌شود).</summary>
    private Task<WorkProject?> GetProjectAsync(int id, int companyId)
        => _db.WorkProjects.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

    // ================== دسترسی‌ها ==================

    [HttpGet("my-access")]
    public async Task<IActionResult> MyAccess() => Ok(new
    {
        userId = MyUserId,
        canView = await HasAsync("View"),
        canCreate = await HasAsync("Create"),
        canUpdate = await HasAsync("Update"),
        canDelete = await HasAsync("Delete")
    });

    /// <summary>کاربران فعال — برای انتخاب اعضای پروژه.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        if (!await HasAsync("View")) return Forbid();
        // نام نمایشی در حافظه ساخته می‌شود (متد سی‌شارپی قابل ترجمه به SQL نیست)
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ToListAsync();
        return Ok(users.Select(u => new LookupItem { Id = u.Id, Name = UserDisplay.Name(u) }).ToList());
    }

    /// <summary>جستجوی سبک پروژه‌های باز — برای کمبوی «پروژه» در فرم دستور کار.</summary>
    /// <summary>
    /// جستجوی سمت سرور پروژه‌های کاری برای کمبوها.
    /// <para>
    /// نکته‌های دسترسی: پیش‌تر فقط با مجوز «پروژه‌های کاری/مشاهده» جواب می‌داد؛ کاربری که
    /// تنها مجوز «دستور کار» داشت، فیلتر پروژهٔ همان صفحه را خالی می‌دید (۴۰۳ و بلعیده‌شدن
    /// خطا در کلاینت) ⇒ «فیلتر پروژه کار نمی‌کند». اکنون مجوز دستور کار هم کافی است.
    /// </para>
    /// <param name="openOnly">true (پیش‌فرض): فقط پروژه‌های باز — برای فرم اتصال دستور کار.
    /// برای «فیلتر» باید false باشد، وگرنه دستورهای متصل به پروژهٔ بسته‌شده قابل فیلتر نیستند.</param>
    /// </summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string? q, [FromQuery] int? take, [FromQuery] bool openOnly = true)
    {
        if (!await HasAsync("View") && !await HasAsyncIn("WorkOrders", "View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var query = _db.WorkProjects.AsNoTracking().Where(p => p.CompanyId == companyId.Value);
        if (openOnly) query = query.Where(p => p.Status == WorkProjectStatus.Open);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = Inventory.Shared.Fa.ToEn(q.Trim()).Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%") || EF.Functions.Like(p.Number, $"%{term}%"));
        }
        var n = Math.Clamp(take is > 0 ? take.Value : 20, 1, 50);
        var list = await query.OrderBy(p => p.DueAt).ThenByDescending(p => p.Id).Take(n)
            .Select(p => new WorkProjectLookupDto { Id = p.Id, Code = p.Number, Name = p.Title, DueAt = p.DueAt })
            .ToListAsync();
        return Ok(list);
    }

    // ================== لیست + جزئیات ==================

    private record ProjStats(int OrderCount, int Open, int Closed, int Overdue, int? Progress);

    /// <summary>آمار دستورهای چند پروژه در ۲ کوئری (دستورها + چک‌لیست‌ها).</summary>
    private async Task<Dictionary<int, ProjStats>> StatsForAsync(List<int> ids, DateTime now)
    {
        var empty = ids.ToDictionary(id => id, _ => new ProjStats(0, 0, 0, 0, null));
        if (ids.Count == 0) return empty;
        var orders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ProjectId != null && ids.Contains(w.ProjectId.Value))
            .Select(w => new { w.Id, Pid = w.ProjectId!.Value, w.Status, w.Weight, w.DueAt })
            .ToListAsync();
        if (orders.Count == 0) return empty;
        var orderIds = orders.Select(o => o.Id).ToList();
        var cl = await _db.WorkOrderChecklistItems.AsNoTracking()
            .Where(c => orderIds.Contains(c.OrderId))
            .GroupBy(c => c.OrderId)
            .Select(g => new { Id = g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) })
            .ToDictionaryAsync(x => x.Id);
        return ids.ToDictionary(id => id, id =>
        {
            var mine = orders.Where(o => o.Pid == id).ToList();
            var progresses = mine.Select(o =>
            {
                cl.TryGetValue(o.Id, out var s);
                return (o.Weight, WorkProjectProgress.OrderProgress(o.Status == "Closed", s?.Total ?? 0, s?.Done ?? 0));
            }).ToList();
            return new ProjStats(
                mine.Count,
                mine.Count(o => o.Status == "Open"),
                mine.Count(o => o.Status == "Closed"),
                mine.Count(o => o.Status == "Open" && o.DueAt < now),
                WorkProjectProgress.ProjectProgress(progresses));
        });
    }

    private static WorkProjectListItemDto ToListItem(
        WorkProject p, ProjStats s, List<WorkProjectMember> members,
        HashSet<int> myMemberProjectIds, WorkProjectLog? lastLog, int myUserId, DateTime today)
    {
        var mine = members.Where(m => m.ProjectId == p.Id).OrderBy(m => m.Name).ToList();
        var isOverdue = p.Status == WorkProjectStatus.Open && p.DueAt.Date < today;
        return new WorkProjectListItemDto
        {
            Id = p.Id, Number = p.Number, Title = p.Title,
            OwnerUserId = p.OwnerUserId, OwnerName = p.OwnerName,
            StartAt = p.StartAt, DueAt = p.DueAt, Status = p.Status,
            Priority = p.Priority, ExtensionCount = p.ExtensionCount, CreatedAt = p.CreatedAt,
            Progress = s.Progress,
            OrderCount = s.OrderCount, OpenOrderCount = s.Open,
            ClosedOrderCount = s.Closed, OverdueOrderCount = s.Overdue,
            MemberCount = mine.Count,
            MemberNames = mine.Take(4).Select(m => m.Name).ToList(),
            DaysLeft = (p.DueAt.Date - today).Days,
            IsOverdue = isOverdue,
            IsMine = p.OwnerUserId == myUserId,
            IsMember = myMemberProjectIds.Contains(p.Id),
            LastActivityAt = lastLog?.CreatedAt,
            LastActivityText = lastLog is null ? null : $"{WorkProjectLogDto.ToFa(lastLog.Action)} — {lastLog.ActorName}"
        };
    }

    /// <summary>لیست پروژه‌های شرکت فعال + پیشرفت/شمارش/آخرین فعالیت هر سطر.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? status, [FromQuery] string? mine,
        [FromQuery] bool? overdue, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });

        var today = DateTime.Today;
        var query = _db.WorkProjects.AsNoTracking().Where(p => p.CompanyId == companyId.Value);
        if (status == "open") query = query.Where(p => p.Status == WorkProjectStatus.Open);
        else if (status == "closed") query = query.Where(p => p.Status == WorkProjectStatus.Closed);
        if (mine == "owner") query = query.Where(p => p.OwnerUserId == MyUserId);
        else if (mine == "member")
        {
            var myProjects = _db.WorkProjectMembers.Where(m => m.UserId == MyUserId).Select(m => m.ProjectId);
            query = query.Where(p => myProjects.Contains(p.Id));
        }
        if (overdue == true) query = query.Where(p => p.Status == WorkProjectStatus.Open && p.DueAt < today);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%")
                || EF.Functions.Like(p.Number, $"%{term}%")
                || EF.Functions.Like(p.OwnerName, $"%{term}%")
                || (p.Tags != null && EF.Functions.Like(p.Tags, $"%{term}%")));
        }

        var projects = await query.ToListAsync();
        var ids = projects.Select(p => p.Id).ToList();
        var now = DateTime.Now;
        var stats = await StatsForAsync(ids, now);
        var members = ids.Count == 0 ? new() : await _db.WorkProjectMembers.AsNoTracking()
            .Where(m => ids.Contains(m.ProjectId)).ToListAsync();
        var myMemberIds = members.Where(m => m.UserId == MyUserId).Select(m => m.ProjectId).ToHashSet();
        var myMindFlowMemberIds = ids.Count == 0 ? new HashSet<int>() : (await _db.WorkProjectMindFlowMembers.AsNoTracking()
            .Where(m => m.UserId == MyUserId && ids.Contains(m.ProjectId)).Select(m => m.ProjectId).ToListAsync()).ToHashSet();
        var lastLogs = ids.Count == 0 ? new() : await _db.WorkProjectLogs.AsNoTracking()
            .Where(l => ids.Contains(l.ProjectId))
            .GroupBy(l => l.ProjectId)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault())
            .ToListAsync();
        var lastByProject = lastLogs.Where(l => l != null).ToDictionary(l => l!.ProjectId);

        // مجوز «بخش نگار ذهنی» جدا از عضویت پروژه است؛ دکمهٔ میان‌بر نگار ذهنی در فهرست
        // هم فقط با همین مجوز دیده می‌شود.
        var canMindFlowSection = await CanSeeSectionAsync("MindFlow");

        var items = projects.Select(p => ToListItem(p, stats[p.Id], members, myMemberIds,
            lastByProject.TryGetValue(p.Id, out var l) ? l : null, MyUserId, today)).ToList();
        foreach (var item in items)
            item.CanViewMindFlow = canMindFlowSection
                && (item.OwnerUserId == MyUserId || item.IsMember || myMindFlowMemberIds.Contains(item.Id));

        items = (sort ?? "due") switch
        {
            "newest" => items.OrderByDescending(i => i.Id).ToList(),
            "progressAsc" => items.OrderBy(i => i.Progress is null).ThenBy(i => i.Progress).ToList(),
            "progressDesc" => items.OrderBy(i => i.Progress is null).ThenByDescending(i => i.Progress).ToList(),
            _ => items.OrderBy(i => i.Status == WorkProjectStatus.Closed).ThenBy(i => i.DueAt).ThenByDescending(i => i.Id).ToList()
        };

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        return Ok(new WorkProjectPageDto
        {
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = items.Count, Page = page, PageSize = pageSize
        });
    }

    /// <summary>جزئیات کامل یک پروژه (مشخصات + اعضا + آمار دستورها).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await _db.WorkProjects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var ids = new List<int> { p.Id };
        var stats = await StatsForAsync(ids, DateTime.Now);
        var members = await _db.WorkProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == p.Id).OrderBy(m => m.Name).ToListAsync();
        var lastLog = await _db.WorkProjectLogs.AsNoTracking()
            .Where(l => l.ProjectId == p.Id)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).FirstOrDefaultAsync();
        var myMemberIds = members.Any(m => m.UserId == MyUserId)
            ? new HashSet<int> { p.Id } : new HashSet<int>();

        var item = ToListItem(p, stats[p.Id], members, myMemberIds, lastLog, MyUserId, DateTime.Today);

        // ---------- دسترسی «هر قسمت» ----------
        // هر تب مجوز مستقل دارد؛ فهرست اعضا هم فقط با مجوز همان بخش برمی‌گردد تا
        // با پنهان‌کردن تب، داده‌اش در پاسخ API لو نرود.
        var canSummary = await CanSeeSectionAsync("Summary");
        var canOrders = await CanSeeSectionAsync("Orders");
        var canMindFlow = await CanSeeSectionAsync("MindFlow") && await CanViewMindFlowAsync(p);
        var canMembers = await CanSeeSectionAsync("Members");
        var canActivity = await CanSeeSectionAsync("Activity");

        return Ok(new WorkProjectDetailDto
        {
            Id = item.Id, Number = item.Number, Title = item.Title,
            OwnerUserId = item.OwnerUserId, OwnerName = item.OwnerName,
            StartAt = item.StartAt, DueAt = item.DueAt, Status = item.Status,
            Priority = item.Priority, ExtensionCount = item.ExtensionCount, CreatedAt = item.CreatedAt,
            Progress = item.Progress, OrderCount = item.OrderCount, OpenOrderCount = item.OpenOrderCount,
            ClosedOrderCount = item.ClosedOrderCount, OverdueOrderCount = item.OverdueOrderCount,
            MemberCount = item.MemberCount, MemberNames = item.MemberNames,
            DaysLeft = item.DaysLeft, IsOverdue = item.IsOverdue,
            IsMine = item.IsMine, IsMember = item.IsMember,
            CanViewMindFlow = canMindFlow,
            CanViewSummary = canSummary, CanViewOrders = canOrders,
            CanViewMembers = canMembers, CanViewActivity = canActivity,
            LastActivityAt = item.LastActivityAt, LastActivityText = item.LastActivityText,
            Description = p.Description, Tags = TagsToList(p.Tags),
            CloseNote = p.CloseNote, ClosedAt = p.ClosedAt,
            Members = canMembers
                ? members.Select(m => new WorkProjectMemberDto
                    { Id = m.Id, UserId = m.UserId, Name = m.Name, AddedAt = m.AddedAt }).ToList()
                : new List<WorkProjectMemberDto>(),
            CanManage = await CanManageAsync(p),
            CanManageMindFlow = await CanManageMindFlowAsync(p)
        });
    }

    // ================== MindFlow پروژه ==================

    /// <summary>پروژه‌هایی که کاربر به MindFlow آن‌ها دسترسی دارد؛ بدون افشای پروژه‌های دیگر شرکت.</summary>
    [HttpGet("mindflow/accessible")]
    public async Task<IActionResult> AccessibleMindFlows([FromQuery] string? q)
    {
        // فهرست نگار ذهنی هم به مجوز همان بخش گره خورده است؛ وگرنه کاربری که مجوز
        // «نگار ذهنی» را ندارد، از همین مسیر نام و شمارهٔ پروژه‌ها را می‌دید.
        if (!await CanSeeSectionAsync("MindFlow")) return Ok(Array.Empty<WorkProjectMindFlowAccessibleProjectDto>());
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var query = _db.WorkProjects.AsNoTracking().Where(p => p.CompanyId == companyId.Value
            && (p.OwnerUserId == MyUserId
                || _db.WorkProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == MyUserId)
                || _db.WorkProjectMindFlowMembers.Any(m => m.ProjectId == p.Id && m.UserId == MyUserId)));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%") || EF.Functions.Like(p.Number, $"%{term}%"));
        }
        var rows = await query.OrderBy(p => p.Status == WorkProjectStatus.Closed).ThenBy(p => p.DueAt)
            .Select(p => new { p.Id, p.Number, p.Title, p.Status, p.OwnerUserId,
                IsProjectMember = _db.WorkProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == MyUserId) }).ToListAsync();
        var canUpdate = await HasAsync("Update");
        return Ok(rows.Select(p => new WorkProjectMindFlowAccessibleProjectDto
        {
            ProjectId = p.Id, Number = p.Number, Title = p.Title, Status = p.Status,
            CanManage = p.OwnerUserId == MyUserId || (p.IsProjectMember && (IsLegacyAdmin || canUpdate))
        }).ToList());
    }

    /// <summary>گرفتن نقشهٔ پروژه؛ فقط مدیر، عضو پروژه یا دعوت‌شدهٔ مستقیم MindFlow.</summary>
    [HttpGet("{id:int}/mindflow")]
    public async Task<IActionResult> GetMindFlow(int id)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await _db.WorkProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanViewMindFlowAsync(project)) return Forbid();

        WorkProjectMindFlowDto? map = null;
        if (!string.IsNullOrWhiteSpace(project.MindFlowJson))
        {
            try { map = System.Text.Json.JsonSerializer.Deserialize<WorkProjectMindFlowDto>(project.MindFlowJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); }
            catch (System.Text.Json.JsonException) { return Conflict(new { message = "دادهٔ نقشهٔ این پروژه قابل خواندن نیست." }); }
        }
        map ??= new WorkProjectMindFlowDto { ProjectId = project.Id, Title = project.Title };
        map.Title = string.IsNullOrWhiteSpace(map.Title) ? project.Title : map.Title;
        NormalizeMindFlowMap(map, project.Id);
        return Ok(map);
    }

    /// <summary>خواندن زیرمایندفلو با مسیر شناسه‌های گرهٔ مالک؛ مجوز همان پروژه اعمال می‌شود.</summary>
    [HttpGet("{id:int}/mindflow/sub")]
    public async Task<IActionResult> GetSubMindFlow(int id, [FromQuery] string? path)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await _db.WorkProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanViewMindFlowAsync(project)) return Forbid();
        if (!TryParseSubMindFlowPath(path, out var pathIds)) return BadRequest(new { message = "مسیر زیرمایندفلو معتبر نیست." });

        var root = DeserializeMindFlow(project.MindFlowJson, project.Id, project.Title);
        if (root is null) return Conflict(new { message = "دادهٔ نگاشت اصلی قابل خواندن نیست." });
        var sub = FindSubMindFlow(root, pathIds);
        return sub is null ? NotFound(new { message = "زیرمایندفلو پیدا نشد." }) : Ok(sub);
    }

    /// <summary>ذخیرهٔ زیرمایندفلو بدون جایگزینی نگاشت اصلی یا بستن صفحهٔ والد.</summary>
    [HttpPut("{id:int}/mindflow/sub")]
    public async Task<IActionResult> SaveSubMindFlow(int id, [FromQuery] string? path, [FromBody] WorkProjectMindFlowDto dto)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        if (dto is null) return BadRequest(new { message = "دادهٔ زیرمایندفلو ارسال نشده است." });
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await GetProjectAsync(id, companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageMindFlowAsync(project)) return Forbid();
        if (project.Status == WorkProjectStatus.Closed) return BadRequest(new { message = "پروژه بسته شده است و زیرمایندفلو قابل ویرایش نیست." });
        if (!TryParseSubMindFlowPath(path, out var pathIds)) return BadRequest(new { message = "مسیر زیرمایندفلو معتبر نیست." });

        var root = DeserializeMindFlow(project.MindFlowJson, project.Id, project.Title);
        if (root is null) return Conflict(new { message = "دادهٔ نگاشت اصلی قابل خواندن نیست." });
        if (root.Nodes.Count == 0) return NotFound(new { message = "نگاشت اصلی هنوز ذخیره نشده است." });
        var current = root;
        WorkProjectMindFlowNodeDto? owner = null;
        for (var index = 0; index < pathIds.Count; index++)
        {
            owner = current.Nodes.FirstOrDefault(n => n.Id == pathIds[index]);
            if (owner is null) return NotFound(new { message = "گرهٔ والد زیرمایندفلو پیدا نشد." });
            if (index < pathIds.Count - 1)
            {
                if (owner.ChildMindFlow is null) return NotFound(new { message = "زیرمایندفلو در مسیر پیدا نشد." });
                current = owner.ChildMindFlow;
            }
        }
        if (owner is null) return BadRequest(new { message = "مسیر زیرمایندفلو خالی است." });
        var error = ValidateMindFlow(dto);
        if (error is not null) return BadRequest(new { message = error });
        if (owner.ChildMindFlow is not null) PreserveSavedSubMindFlows(owner.ChildMindFlow, dto);
        NormalizeMindFlowMap(dto, project.Id);
        owner.ChildMindFlow = dto;

        error = ValidateMindFlow(root);
        if (error is not null) return BadRequest(new { message = error });
        var linkError = await ValidateMindFlowLinksAsync(root, project);
        if (linkError is not null) return BadRequest(new { message = linkError });
        var json = System.Text.Json.JsonSerializer.Serialize(root, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (json.Length > 1_000_000) return BadRequest(new { message = "حجم نقشه از حد مجاز یک مگابایت بیشتر است." });
        project.MindFlowJson = json;
        Log(project.Id, "MindFlowUpdated", "زیرمایندفلو به‌روزرسانی شد.");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { projectId = project.Id, savedAt = DateTime.Now });
    }

    /// <summary>اعضای دعوت‌شدهٔ مستقیم (فقط مدیر پروژه/دارای مجوز Update).</summary>
    [HttpGet("{id:int}/mindflow/members")]
    public async Task<IActionResult> MindFlowMembers(int id)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await GetProjectAsync(id, companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageMindFlowAsync(project)) return Forbid();
        var rows = await _db.WorkProjectMindFlowMembers.AsNoTracking().Where(m => m.ProjectId == id)
            .Join(_db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { Member = m, User = u })
            .OrderBy(x => x.User.FirstName).ThenBy(x => x.User.LastName).ToListAsync();
        return Ok(rows.Select(x => new WorkProjectMindFlowMemberDto
        { UserId = x.User.Id, Name = UserDisplay.Name(x.User), AddedAt = x.Member.AddedAt }).ToList());
    }

    /// <summary>کاربران فعال شرکت برای دعوت فقط‌خواندنی به MindFlow.</summary>
    [HttpGet("{id:int}/mindflow/candidates")]
    public async Task<IActionResult> MindFlowCandidates(int id, [FromQuery] string? q, [FromQuery] int? take)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await GetProjectAsync(id, companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageMindFlowAsync(project)) return Forbid();
        var query = _db.Users.AsNoTracking().Where(u => u.IsActive
            && _db.UserCompanyAccesses.Any(a => a.UserId == u.Id && a.CompanyId == companyId.Value)
            && u.Id != project.OwnerUserId
            && !_db.WorkProjectMembers.Any(m => m.ProjectId == id && m.UserId == u.Id)
            && !_db.WorkProjectMindFlowMembers.Any(m => m.ProjectId == id && m.UserId == u.Id));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u => EF.Functions.Like(u.Username, $"%{term}%")
                || EF.Functions.Like(u.FirstName, $"%{term}%") || EF.Functions.Like(u.LastName, $"%{term}%"));
        }
        var count = Math.Clamp(take is > 0 ? take.Value : 50, 1, 100);
        var users = await query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).Take(count).ToListAsync();
        return Ok(users.Select(u => new WorkProjectMindFlowUserDto { UserId = u.Id, Name = UserDisplay.Name(u) }).ToList());
    }

    [HttpPost("{id:int}/mindflow/members")]
    public async Task<IActionResult> AddMindFlowMembers(int id, [FromBody] AddWorkProjectMindFlowMembersDto dto)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await GetProjectAsync(id, companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageMindFlowAsync(project)) return Forbid();
        if (project.Status == WorkProjectStatus.Closed) return BadRequest(new { message = "پروژه بسته شده است؛ تغییر دسترسی نگاشت ممکن نیست." });
        var userIds = (dto?.UserIds ?? new()).Where(x => x > 0).Distinct().ToList();
        if (userIds.Count == 0) return BadRequest(new { message = "حداقل یک کاربر را انتخاب کنید." });
        var validIds = await _db.Users.Where(u => userIds.Contains(u.Id) && u.IsActive
                && _db.UserCompanyAccesses.Any(a => a.UserId == u.Id && a.CompanyId == companyId.Value))
            .Select(u => u.Id).ToListAsync();
        if (validIds.Count != userIds.Count) return BadRequest(new { message = "همهٔ افراد انتخاب‌شده کاربر فعال همین شرکت نیستند." });
        var projectMemberIds = await _db.WorkProjectMembers.Where(m => m.ProjectId == id).Select(m => m.UserId).ToListAsync();
        if (userIds.Any(uid => uid == project.OwnerUserId || projectMemberIds.Contains(uid)))
            return BadRequest(new { message = "مدیر و اعضای پروژه از قبل به نگاشت دسترسی دارند." });
        var existing = await _db.WorkProjectMindFlowMembers.Where(m => m.ProjectId == id && userIds.Contains(m.UserId))
            .Select(m => m.UserId).ToListAsync();
        var now = DateTime.Now;
        _db.WorkProjectMindFlowMembers.AddRange(userIds.Except(existing).Select(uid => new WorkProjectMindFlowMember
        { ProjectId = id, UserId = uid, AddedByUserId = MyUserId, AddedAt = now }));
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { count = userIds.Except(existing).Count() });
    }

    [HttpDelete("{id:int}/mindflow/members/{userId:int}")]
    public async Task<IActionResult> RemoveMindFlowMember(int id, int userId)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await GetProjectAsync(id, companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageMindFlowAsync(project)) return Forbid();
        if (project.Status == WorkProjectStatus.Closed) return BadRequest(new { message = "پروژه بسته شده است؛ تغییر دسترسی نگاشت ممکن نیست." });
        var member = await _db.WorkProjectMindFlowMembers.FirstOrDefaultAsync(m => m.ProjectId == id && m.UserId == userId);
        if (member is null) return NotFound(new { message = "فرد در دسترسی‌های مستقیم این نگاشت نیست." });
        _db.WorkProjectMindFlowMembers.Remove(member);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    /// <summary>ذخیرهٔ ساختار MindFlow روی پروژهٔ کاری؛ فقط مدیر پروژه/دارای مجوز Update.</summary>
    [HttpPut("{id:int}/mindflow")]
    public async Task<IActionResult> SaveMindFlow(int id, [FromBody] WorkProjectMindFlowDto dto)
    {
        if (!await CanSeeSectionAsync("MindFlow")) return Forbid();
        if (dto is null) return BadRequest(new { message = "دادهٔ نقشه ارسال نشده است." });
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await GetProjectAsync(id, companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageMindFlowAsync(project)) return Forbid();
        if (project.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است؛ برای ویرایش نقشه ابتدا آن را بازگشایی کنید." });

        var error = ValidateMindFlow(dto);
        if (error is not null) return BadRequest(new { message = error });
        if (!string.IsNullOrWhiteSpace(project.MindFlowJson))
        {
            var savedMap = DeserializeMindFlow(project.MindFlowJson, project.Id, project.Title);
            if (savedMap is null) return Conflict(new { message = "دادهٔ نگاشت قبلی قابل خواندن نیست." });
            PreserveSavedSubMindFlows(savedMap, dto);
        }
        NormalizeMindFlowMap(dto, project.Id);
        error = ValidateMindFlow(dto);
        if (error is not null) return BadRequest(new { message = error });
        var linkError = await ValidateMindFlowLinksAsync(dto, project);
        if (linkError is not null) return BadRequest(new { message = linkError });
        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (json.Length > 1_000_000) return BadRequest(new { message = "حجم نقشه از حد مجاز یک مگابایت بیشتر است." });

        project.MindFlowJson = json;
        Log(project.Id, "MindFlowUpdated", "ساختار نقشهٔ ذهنی پروژه ذخیره شد.");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { projectId = project.Id, savedAt = DateTime.Now });
    }

    private async Task<string?> ValidateMindFlowLinksAsync(WorkProjectMindFlowDto dto, WorkProject project)
    {
        var nodes = EnumerateMindFlowNodes(dto).ToList();
        var orderIds = nodes.SelectMany(NodeWorkOrderIds).Distinct().ToList();
        if (orderIds.Count > 0)
        {
            var validOrders = await _db.WorkOrders.CountAsync(w => orderIds.Contains(w.Id) && w.CompanyId == project.CompanyId);
            if (validOrders != orderIds.Count) return "فقط دستورهای کاریِ همین شرکت قابل اتصال‌اند.";
        }

        var childMapIds = nodes.Where(n => n.LinkedMindFlowProjectId.HasValue)
            .Select(n => n.LinkedMindFlowProjectId!.Value).Distinct().ToList();
        if (childMapIds.Contains(project.Id)) return "نقشه نمی‌تواند به خودش وصل شود.";
        if (childMapIds.Count > 0)
        {
            var validMaps = await _db.WorkProjects.CountAsync(p => childMapIds.Contains(p.Id) && p.CompanyId == project.CompanyId);
            if (validMaps != childMapIds.Count) return "فقط نگاشت‌های پروژه‌های همین شرکت قابل اتصال‌اند.";
        }
        return null;
    }

    private static WorkProjectMindFlowDto? DeserializeMindFlow(string? json, int projectId, string title)
    {
        WorkProjectMindFlowDto map;
        if (string.IsNullOrWhiteSpace(json)) map = new WorkProjectMindFlowDto { ProjectId = projectId, Title = title };
        else
        {
            try { map = System.Text.Json.JsonSerializer.Deserialize<WorkProjectMindFlowDto>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!; }
            catch (System.Text.Json.JsonException) { return null; }
            if (map is null) return null;
        }
        map.Title = string.IsNullOrWhiteSpace(map.Title) ? title : map.Title;
        NormalizeMindFlowMap(map, projectId);
        return map;
    }

    internal static bool TryParseSubMindFlowPath(string? path, out List<Guid> ids)
    {
        ids = new();
        if (string.IsNullOrWhiteSpace(path)) return false;
        var pieces = path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length is 0 or > 8) return false;
        foreach (var piece in pieces)
        {
            if (!Guid.TryParse(piece, out var id) || id == Guid.Empty) return false;
            ids.Add(id);
        }
        return true;
    }

    private static WorkProjectMindFlowDto? FindSubMindFlow(WorkProjectMindFlowDto root, List<Guid> path)
    {
        var current = root;
        foreach (var ownerId in path)
        {
            var owner = current.Nodes.FirstOrDefault(n => n.Id == ownerId);
            if (owner?.ChildMindFlow is null) return null;
            current = owner.ChildMindFlow;
        }
        return current;
    }

    internal static void PreserveSavedSubMindFlows(WorkProjectMindFlowDto existing, WorkProjectMindFlowDto incoming)
    {
        foreach (var node in incoming.Nodes)
        {
            var saved = existing.Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (saved?.ChildMindFlow is not null) node.ChildMindFlow = saved.ChildMindFlow;
        }
    }

    internal static IEnumerable<WorkProjectMindFlowNodeDto> EnumerateMindFlowNodes(WorkProjectMindFlowDto map)
    {
        if (map.Nodes is null) yield break;
        foreach (var node in map.Nodes)
        {
            if (node is null) continue;
            yield return node;
            if (node.ChildMindFlow is not null)
                foreach (var child in EnumerateMindFlowNodes(node.ChildMindFlow)) yield return child;
        }
    }

    internal static IEnumerable<int> NodeWorkOrderIds(WorkProjectMindFlowNodeDto node)
    {
        var ids = node.WorkOrderIds ?? new List<int>();
        return ids.Concat(node.WorkOrderId is int legacy ? new[] { legacy } : Array.Empty<int>())
            .Where(id => id > 0).Distinct();
    }

    internal static void NormalizeMindFlowMap(WorkProjectMindFlowDto map, int projectId)
    {
        map.ProjectId = projectId;
        map.Nodes ??= new();
        foreach (var node in map.Nodes)
        {
            if (node is null) continue;
            node.Note ??= "";
            node.Color = MindFlowColors.Contains(node.Color ?? "") ? node.Color : "#596cf3";
            node.WorkOrderIds ??= new();
            if (node.WorkOrderId is int legacyOrderId && legacyOrderId > 0 && !node.WorkOrderIds.Contains(legacyOrderId))
                node.WorkOrderIds.Insert(0, legacyOrderId);
            node.WorkOrderIds = node.WorkOrderIds.Where(id => id > 0).Distinct().ToList();
            node.WorkOrderId = null;
            if (node.ChildMindFlow is not null)
            {
                node.ChildMindFlow.Title = string.IsNullOrWhiteSpace(node.ChildMindFlow.Title) ? node.Text : node.ChildMindFlow.Title;
                NormalizeMindFlowMap(node.ChildMindFlow, projectId);
            }
        }
    }

    internal static string? ValidateMindFlow(WorkProjectMindFlowDto dto)
    {
        var globalNodeIds = new HashSet<Guid>();
        var totalNodes = 0;
        return ValidateMindFlowTree(dto, 0, globalNodeIds, ref totalNodes);
    }

    internal static string? ValidateMindFlowTree(WorkProjectMindFlowDto dto, int depth, HashSet<Guid> globalNodeIds, ref int totalNodes)
    {
        if (depth > 8) return "زیرمایندفلو حداکثر ۸ سطح می‌تواند تو در تو باشد.";
        if (string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Length > 200) return "عنوان هر نگاشت باید بین ۱ تا ۲۰۰ نویسه باشد.";
        if (dto.Nodes is null || dto.Nodes.Count is 0 or > 1000) return "هر نگاشت باید بین ۱ تا ۱۰۰۰ گره داشته باشد.";
        if (dto.Nodes.Any(n => n is null)) return "ساختار گره‌های نقشه معتبر نیست.";
        totalNodes += dto.Nodes.Count;
        if (totalNodes > 5000) return "مجموع گره‌های اصلی و زیرمایندفلوها از ۵۰۰۰ بیشتر است.";
        if (dto.Nodes.Any(n => n.Id == Guid.Empty || string.IsNullOrWhiteSpace(n.Text) || n.Text.Length > 200 || (n.Note?.Length ?? 0) > 2000))
            return "عنوان هر گره حداکثر ۲۰۰ و یادداشت حداکثر ۲۰۰۰ نویسه باشد.";
        if (dto.Nodes.Any(n => !MindFlowColors.Contains(n.Color ?? ""))) return "رنگ گره معتبر نیست.";
        if (dto.Nodes.Any(n => (n.Width.HasValue && (n.Width.Value < 120 || n.Width.Value > 420))
            || (n.Height.HasValue && (n.Height.Value < 50 || n.Height.Value > 180))))
            return "اندازهٔ هر گره باید در محدودهٔ عرض ۱۲۰ تا ۴۲۰ و ارتفاع ۵۰ تا ۱۸۰ باشد.";
        if (dto.Nodes.Any(n => n.WorkOrderIds is not null && (n.WorkOrderIds.Any(id => id <= 0) || n.WorkOrderIds.Distinct().Count() != n.WorkOrderIds.Count)))
            return "فهرست دستورکارهای متصل معتبر نیست.";
        var mapOrderIds = dto.Nodes.SelectMany(NodeWorkOrderIds).ToList();
        if (mapOrderIds.Distinct().Count() != mapOrderIds.Count)
            return "هر دستورکار فقط می‌تواند به یک گره در همان نگاشت وصل باشد.";
        if (dto.Nodes.Any(n => n.ChildMindFlow is not null && n.LinkedMindFlowProjectId.HasValue))
            return "برای هر گره فقط یک زیرمایندفلو انتخاب یا ساخته شود.";
        foreach (var node in dto.Nodes)
            if (!globalNodeIds.Add(node.Id)) return "شناسهٔ گره‌ها در کل درخت مایندفلو باید یکتا باشد.";

        var localIds = dto.Nodes.Select(n => n.Id).ToHashSet();
        if (dto.Nodes.Count(n => n.ParentId is null) != 1 || dto.Nodes.Any(n => n.ParentId is Guid p && !localIds.Contains(p)))
            return "ساختار گره‌های نقشه معتبر نیست.";
        var byId = dto.Nodes.ToDictionary(n => n.Id);
        foreach (var node in dto.Nodes)
        {
            var seen = new HashSet<Guid> { node.Id };
            var parent = node.ParentId;
            while (parent is Guid parentId)
            {
                if (!seen.Add(parentId)) return "درخت نقشه نمی‌تواند حلقه داشته باشد.";
                parent = byId[parentId].ParentId;
            }
            if (node.ChildMindFlow is not null)
            {
                var childError = ValidateMindFlowTree(node.ChildMindFlow, depth + 1, globalNodeIds, ref totalNodes);
                if (childError is not null) return childError;
            }
        }
        return null;
    }

    // ================== ساخت/ویرایش ==================

    private async Task<IActionResult?> ValidateMembersAsync(List<int> userIds)
    {
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0) return null;
        var users = await _db.Users.Where(u => distinct.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count != distinct.Count)
            return BadRequest(new { message = "برخی اعضا وجود ندارند یا غیرفعال‌اند؛ فقط کاربران فعال را انتخاب کنید." });
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveWorkProjectDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var title = (dto.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان پروژه را وارد کنید." });
        if (title.Length > 200)
            return BadRequest(new { message = "عنوان پروژه حداکثر ۲۰۰ حرف است." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        var start = dto.StartAt.Date;
        var due = dto.DueAt.Date;
        if (due < start)
            return BadRequest(new { message = "مهلت پایان نمی‌تواند قبل از تاریخ شروع باشد." });
        if (due < DateTime.Today)
            return BadRequest(new { message = "مهلت پایان نمی‌تواند قبل از امروز باشد." });
        var memberIds = dto.MemberIds.Distinct().ToList();
        if (await ValidateMembersAsync(memberIds) is { } bad) return bad;

        var p = new WorkProject
        {
            CompanyId = companyId.Value, Title = title,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            OwnerUserId = MyUserId, OwnerName = await MyDisplayNameAsync(),
            StartAt = start, DueAt = due, Priority = dto.Priority,
            Tags = NormalizeTags(dto.Tags)
        };
        _db.WorkProjects.Add(p);
        await _db.SaveChangesAsync();
        p.Number = $"PRJ/{new PersianCalendar().GetYear(p.CreatedAt)}/{p.Id}";

        var users = await _db.Users.Where(u => memberIds.Contains(u.Id)).ToListAsync();
        foreach (var u in users)
            _db.WorkProjectMembers.Add(new WorkProjectMember
                { ProjectId = p.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        Log(p.Id, "Created", $"پروژه {p.Number} «{p.Title}» — مهلت: {ToFa(p.DueAt)} — اعضا: {users.Count} نفر");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        foreach (var u in users.Where(u => u.Id != MyUserId))
            await _notify.SendAsync(u.Id, "به پروژه کاری اضافه شدید 🏗",
                $"{p.Number} — «{p.Title}» — مهلت: {ToFa(p.DueAt)}",
                p.OwnerName, "پروژه کاری", $"/work-projects?open={p.Id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { id = p.Id, number = p.Number });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveWorkProjectDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده و قابل ویرایش نیست؛ اول آن را بازگشایی کنید." });

        var title = (dto.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان پروژه را وارد کنید." });
        if (title.Length > 200)
            return BadRequest(new { message = "عنوان پروژه حداکثر ۲۰۰ حرف است." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        var start = dto.StartAt.Date;
        var due = dto.DueAt.Date;
        if (due < start)
            return BadRequest(new { message = "مهلت پایان نمی‌تواند قبل از تاریخ شروع باشد." });

        p.Title = title;
        p.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        p.StartAt = start;
        p.DueAt = due;
        p.Priority = dto.Priority;
        p.Tags = NormalizeTags(dto.Tags);
        p.OwnerName = await MyDisplayNameAsync();

        Log(p.Id, "Edited", $"ویرایش مشخصات پروژه — مهلت: {ToFa(p.DueAt)}");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { id = p.Id });
    }

    // ================== تمدید مهلت ==================

    [HttpPost("{id:int}/extend")]
    public async Task<IActionResult> Extend(int id, [FromBody] ExtendWorkProjectDto dto)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است." });
        if (p.ExtensionCount >= MaxExtension)
            return BadRequest(new { message = $"حداکثر {MaxExtension} بار امکان تمدید وجود دارد." });
        if (string.IsNullOrWhiteSpace(dto.Note))
            return BadRequest(new { message = "دلیل تمدید را وارد کنید." });
        var newDue = dto.NewDueAt.Date;
        if (newDue <= DateTime.Today)
            return BadRequest(new { message = "مهلت جدید باید بعد از امروز باشد." });
        if (newDue <= p.DueAt.Date)
            return BadRequest(new { message = "مهلت جدید باید بعد از مهلت فعلی باشد." });

        var old = p.DueAt;
        p.DueAt = newDue;
        p.ExtensionCount++;

        Log(p.Id, "Extended", $"تمدید {p.ExtensionCount} از {MaxExtension} — از {ToFa(old)} به {ToFa(newDue)} — {dto.Note!.Trim()}");
        await _db.SaveChangesAsync();

        var members = await _db.WorkProjectMembers.Where(m => m.ProjectId == id).ToListAsync();
        foreach (var m in members.Where(m => m.UserId != MyUserId))
            await _notify.SendAsync(m.UserId, "تمدید مهلت پروژه ⏳",
                $"{p.Number} — مهلت جدید: {ToFa(newDue)}", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { extensionCount = p.ExtensionCount });
    }

    // ================== بستن / بازگشایی / حذف ==================

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, [FromBody] CloseWorkProjectDto dto)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "قبلاً بسته شده است." });

        var openOrders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ProjectId == id && w.Status == "Open")
            .Select(w => w.Number).ToListAsync();
        if (openOrders.Count > 0)
        {
            var shown = string.Join("، ", openOrders.Take(5));
            var more = openOrders.Count > 5 ? $" و {openOrders.Count - 5} مورد دیگر" : "";
            return BadRequest(new { message = $"اول دستورهای باز پروژه بسته یا جدا شوند: {shown}{more}" });
        }

        p.Status = WorkProjectStatus.Closed;
        p.ClosedAt = DateTime.Now;
        p.CloseNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        Log(p.Id, "Closed", $"پروژه توسط {MyUsername} نهایی و بسته شد{(p.CloseNote is null ? "" : " — " + p.CloseNote)}");
        await _db.SaveChangesAsync();

        var members = await _db.WorkProjectMembers.Where(m => m.ProjectId == id).ToListAsync();
        foreach (var m in members.Where(m => m.UserId != MyUserId))
            await _notify.SendAsync(m.UserId, "پروژه کاری بسته شد 🔒",
                $"{p.Number} — «{p.Title}»", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Open)
            return BadRequest(new { message = "پروژه باز است." });

        p.Status = WorkProjectStatus.Open;
        p.ClosedAt = null;
        Log(p.Id, "Reopened", $"پروژه توسط {MyUsername} بازگشایی شد.");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var openCount = await _db.WorkOrders.CountAsync(w => w.ProjectId == id && w.Status == "Open");
        if (openCount > 0)
            return BadRequest(new { message = $"این پروژه {openCount} دستور باز دارد؛ اول آن‌ها را ببندید یا از پروژه جدا کنید." });

        p.DeletedAt = DateTime.Now;
        p.DeletedByUserId = MyUserId;
        Log(p.Id, "Deleted", $"حذف پروژه {p.Number} توسط {MyUsername}؛ سوابق دستورها محفوظ است.");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        await _notify.BroadcastChangedAsync("workorders");
        return NoContent();
    }

    // ================== اعضا ==================

    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMembers(int id, [FromBody] AddWorkProjectMembersDto dto)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        // مثل قبل: مجوز Update (یا مالک/مدیر) برای مدیریت اعضا کافی است؛ مجوز مستقل
        // «Members» هم همین اجازه را می‌دهد تا نقش «مدیر اعضا» بدون حق ویرایش پروژه بسازیم.
        if (!await CanSeeSectionAsync("Members") && !await HasAsync("Update")) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است." });

        var userIds = dto.UserIds.Distinct().Where(u => u > 0).ToList();
        if (userIds.Count == 0)
            return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        if (await ValidateMembersAsync(userIds) is { } bad) return bad;

        var existing = await _db.WorkProjectMembers.Where(m => m.ProjectId == id)
            .Select(m => m.UserId).ToListAsync();
        var fresh = userIds.Where(u => !existing.Contains(u)).ToList();
        if (fresh.Count == 0)
            return BadRequest(new { message = "این افراد قبلاً عضو پروژه هستند." });

        var users = await _db.Users.Where(u => fresh.Contains(u.Id)).ToListAsync();
        foreach (var u in users)
            _db.WorkProjectMembers.Add(new WorkProjectMember
                { ProjectId = p.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        Log(p.Id, "MemberAdded", $"افزودن عضو: {string.Join("، ", users.Select(UserDisplay.Name))}");
        await _db.SaveChangesAsync();

        foreach (var u in users.Where(u => u.Id != MyUserId))
            await _notify.SendAsync(u.Id, "به پروژه کاری اضافه شدید 🏗",
                $"{p.Number} — «{p.Title}»", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { added = users.Count });
    }

    [HttpDelete("{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است." });

        var m = await _db.WorkProjectMembers.FirstOrDefaultAsync(x => x.ProjectId == id && x.UserId == userId);
        if (m is null) return NotFound(new { message = "این کاربر عضو پروژه نیست." });

        _db.WorkProjectMembers.Remove(m);
        Log(p.Id, "MemberRemoved", $"حذف عضو: {m.Name}");
        await _db.SaveChangesAsync();

        if (userId != MyUserId)
            await _notify.SendAsync(userId, "از پروژه کاری حذف شدید",
                $"{p.Number} — «{p.Title}»", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    // ================== تاریخچه فعالیت ==================

    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> Activity(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanSeeSectionAsync("Activity")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var exists = await _db.WorkProjects.AsNoTracking()
            .AnyAsync(p => p.Id == id && p.CompanyId == companyId.Value);
        if (!exists) return NotFound(new { message = "پروژه پیدا نشد." });

        var n = Math.Clamp(take is > 0 ? take.Value : 20, 1, 100);
        var logs = await _db.WorkProjectLogs.AsNoTracking()
            .Where(l => l.ProjectId == id)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip(Math.Max(0, skip)).Take(n)
            .Select(l => new WorkProjectLogDto
            {
                Id = l.Id, ActorName = l.ActorName, Action = l.Action,
                Text = l.Text, CreatedAt = l.CreatedAt
            }).ToListAsync();
        foreach (var l in logs) l.ActionFa = WorkProjectLogDto.ToFa(l.Action);
        return Ok(logs);
    }
}
