using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.FaCom;

public interface IFaComService
{
    // اطلاعیه‌ها
    Task<List<FaComAnnouncementDto>> GetFeedAsync(int userId, Paging.Request? pagination = null);
    Task<List<FaComAnnouncementDto>> ListAnnouncementsAsync(Paging.Request? pagination = null);
    Task<FaComAnnouncementDto> SaveAnnouncementAsync(int? id, FaComAnnouncementSaveDto dto, int byUserId, string byName);
    Task DeleteAnnouncementAsync(int id);
    Task<int> BroadcastAsync(int id, bool email, bool push, bool sms, int byUserId);
    Task<int> PublishNowAsync(int id, int byUserId);
    Task<int> CheckDueAnnouncementsAsync();

    // نظرسنجی‌ها
    Task<List<FaComPollDto>> ListPollsAsync(int userId, Paging.Request? pagination = null);
    Task<List<FaComPollDto>> ManagePollsAsync(Paging.Request? pagination = null);
    Task<FaComPollDto> GetPollAsync(int id, int userId);
    Task<FaComPollDto> SavePollAsync(int? id, FaComPollSaveDto dto, int byUserId, string byName);
    Task DeletePollAsync(int id);
    Task<FaComPollDto> VoteAsync(int pollId, int optionId, int userId);

    // تیکت‌ها
    Task<List<FaComTicketDto>> GetMyTicketsAsync(int userId, Paging.Request? pagination = null);
    Task<FaComTicketDto> GetMyTicketAsync(int id, int userId);
    Task<List<FaComTicketDto>> ListTicketsAsync(int? status, int? category, Paging.Request? pagination = null);
    Task<FaComTicketDto> GetTicketAsync(int id);
    Task<FaComTicketDto> CreateTicketAsync(int userId, FaComTicketSaveDto dto, bool asAdmin);
    Task<FaComTicketDto> ReplyMyAsync(int userId, string userName, FaComReplySaveDto dto);
    Task<FaComTicketDto> ReplyHrAsync(FaComReplySaveDto dto, int byUserId, string byName);
    Task<FaComTicketDto> SetStatusAsync(int id, int status, string byName);
    Task DeleteTicketAsync(int id);

    // توزیع چندکاناله + رویدادها (قابل استفاده ماژول‌های دیگر)
    Task<int> NotifyManyAsync(IEnumerable<int> userIds, string title, string? body, string? link,
        bool email, bool push, bool sms, int byUserId);
    Task<int> CheckBirthdaysAsync();
    Task<int> CheckWorkAnniversariesAsync();

    // صندوق پیشنهادها
    Task<List<FaComSuggestionDto>> MySuggestionsAsync(int userId, Paging.Request? pagination = null);
    Task<FaComSuggestionDto> CreateSuggestionAsync(int userId, FaComSuggestionSaveDto dto);
    Task<List<FaComSuggestionDto>> ListSuggestionsAsync(int? status, int? category, Paging.Request? pagination = null);
    Task<FaComSuggestionDto> RespondSuggestionAsync(int id, FaComSuggestionRespondDto dto, string byName);
    Task DeleteSuggestionAsync(int id);
}

public class FaComService : IFaComService
{
    private const string FromName = "منابع انسانی";
    private const string FormName = "FaCom";

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly IEmailService _email;
    private readonly IPushService _push;
    private readonly ISmsSender _sms;

    public FaComService(AppDbContext db, INotifyService notify, IEmailService email, IPushService push, ISmsSender sms)
    {
        _db = db; _notify = notify; _email = email; _push = push; _sms = sms;
    }

    // ==================== اطلاعیه‌ها ====================

    public async Task<List<FaComAnnouncementDto>> GetFeedAsync(int userId, Paging.Request? pagination = null)
    {
        var now = DateTime.Now;
        var emp = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive);
        var q = _db.FaComAnnouncements.AsNoTracking()
            .Where(a => a.IsActive && (a.PublishFrom == null || a.PublishFrom <= now)
                     && (a.PublishTo == null || a.PublishTo >= now));
        var chain = new HashSet<int>();
        if (emp?.OrgUnitId is > 0)
            foreach (var x in await AncestorUnitIdsAsync(emp.OrgUnitId!.Value)) chain.Add(x);
        if (emp?.HrMainNodeId is > 0)
            foreach (var x in await AncestorMainNodeIdsAsync(emp.HrMainNodeId!.Value)) chain.Add(x);
        if (chain.Count > 0)
            q = q.Where(a => a.Audience == FaComAudience.All
                || (a.Audience == FaComAudience.Unit && a.OrgUnitId != null && chain.Contains(a.OrgUnitId.Value)));
        else
            q = q.Where(a => a.Audience == FaComAudience.All);
        var rows = await q.OrderByDescending(a => a.CreatedAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 50);
        var list = new List<FaComAnnouncementDto>();
        foreach (var a in rows) list.Add(await MapAnnouncementAsync(a));
        return list;
    }

    public async Task<List<FaComAnnouncementDto>> ListAnnouncementsAsync(Paging.Request? pagination = null)
    {
        var rows = await _db.FaComAnnouncements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 500);
        var list = new List<FaComAnnouncementDto>();
        foreach (var a in rows) list.Add(await MapAnnouncementAsync(a));
        return list;
    }

    public async Task<FaComAnnouncementDto> SaveAnnouncementAsync(int? id, FaComAnnouncementSaveDto dto, int byUserId, string byName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان اطلاعیه الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new InvalidOperationException("متن اطلاعیه الزامی است.");
        if (dto.Audience is < 0 or > 1) throw new InvalidOperationException("مخاطب نامعتبر است.");
        if (dto.Audience == 1)
        {
            if (dto.OrgUnitId is not > 0) throw new InvalidOperationException("برای اطلاعیه واحدی، انتخاب واحد الزامی است.");
            var ok = await _db.HrOrgUnits.AnyAsync(u => u.Id == dto.OrgUnitId!.Value)
                || await _db.HrMainOrgNodes.AnyAsync(u => u.Id == dto.OrgUnitId!.Value);
            if (!ok) throw new InvalidOperationException("واحد نامعتبر است.");
        }
        if (dto.PublishFrom != null && dto.PublishTo != null && dto.PublishTo < dto.PublishFrom)
            throw new InvalidOperationException("پایان انتشار نمی‌تواند قبل از شروع باشد.");
        FaComAnnouncement a;
        if (id is > 0)
        {
            a = await _db.FaComAnnouncements.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("اطلاعیه یافت نشد.");
        }
        else
        {
            a = new FaComAnnouncement { CreatedByUserId = byUserId, CreatedByName = byName };
            _db.FaComAnnouncements.Add(a);
        }
        a.Title = dto.Title.Trim(); a.Body = dto.Body.Trim();
        a.Audience = (FaComAudience)dto.Audience;
        a.OrgUnitId = dto.Audience == 1 ? dto.OrgUnitId : null;
        a.PublishFrom = dto.PublishFrom; a.PublishTo = dto.PublishTo; a.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return await MapAnnouncementAsync(a);
    }

    public async Task DeleteAnnouncementAsync(int id)
    {
        var a = await _db.FaComAnnouncements.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("اطلاعیه یافت نشد.");
        _db.FaComAnnouncements.Remove(a);
        await _db.SaveChangesAsync();
    }

    public async Task<int> BroadcastAsync(int id, bool email, bool push, bool sms, int byUserId)
    {
        var a = await _db.FaComAnnouncements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("اطلاعیه یافت نشد.");
        var ids = await AudienceUserIdsAsync(a);
        return await NotifyManyAsync(ids, a.Title, a.Body, "fa-com/announcements", email, push, sms, byUserId);
    }

    /// <summary>کاربران مخاطب اطلاعیه (واحدی = واحد هدف + همه زیرمجموعه‌ها).</summary>
    private async Task<List<int>> AudienceUserIdsAsync(FaComAnnouncement a)
    {
        var q = _db.HrEmployees.AsNoTracking().Where(e => e.IsActive && e.SystemUserId != null);
        if (a.Audience == FaComAudience.Unit && a.OrgUnitId is > 0)
        {
            var target = a.OrgUnitId.Value;
            var set = await SubtreeUnitIdsAsync(target);
            var mainSet = await SubtreeMainNodeIdsAsync(target);
            q = q.Where(e => (e.OrgUnitId != null && set.Contains(e.OrgUnitId.Value))
                || (e.HrMainNodeId != null && mainSet.Contains(e.HrMainNodeId.Value)));
        }
        return await q.Select(e => e.SystemUserId!.Value).ToListAsync();
    }

    private async Task<HashSet<int>> SubtreeMainNodeIdsAsync(int rootId)
    {
        var units = await _db.HrMainOrgNodes.AsNoTracking()
            .Select(u => new { u.Id, u.ParentId }).ToListAsync();
        var kids = units.Where(u => u.ParentId != null)
            .GroupBy(u => u.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var set = new HashSet<int> { rootId };
        var stack = new Stack<int>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!kids.TryGetValue(cur, out var list)) continue;
            foreach (var k in list)
                if (set.Add(k)) stack.Push(k);
        }
        return set;
    }

    private async Task<HashSet<int>> AncestorMainNodeIdsAsync(int nodeId)
    {
        var map = await _db.HrMainOrgNodes.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.ParentId);
        var set = new HashSet<int> { nodeId };
        var cur = nodeId;
        var guard = 0;
        while (guard++ < 50 && map.TryGetValue(cur, out var p) && p is > 0)
        {
            set.Add(p.Value);
            cur = p.Value;
        }
        return set;
    }

    private async Task<HashSet<int>> SubtreeUnitIdsAsync(int rootId)
    {
        var units = await _db.HrOrgUnits.AsNoTracking()
            .Select(u => new { u.Id, u.ParentId }).ToListAsync();
        var kids = units.Where(u => u.ParentId != null)
            .GroupBy(u => u.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var set = new HashSet<int> { rootId };
        var stack = new Stack<int>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!kids.TryGetValue(cur, out var list)) continue;
            foreach (var k in list)
                if (set.Add(k)) stack.Push(k);
        }
        return set;
    }

    private async Task<HashSet<int>> AncestorUnitIdsAsync(int unitId)
    {
        var map = await _db.HrOrgUnits.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.ParentId);
        var set = new HashSet<int> { unitId };
        var cur = unitId;
        var guard = 0;
        while (guard++ < 50 && map.TryGetValue(cur, out var p) && p is > 0)
        {
            set.Add(p.Value);
            cur = p.Value;
        }
        return set;
    }

    /// <summary>انتشار فوری: ارسال اعلان سیستمی + ثبت زمان انتشار.</summary>
    public async Task<int> PublishNowAsync(int id, int byUserId)
    {
        var a = await _db.FaComAnnouncements.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("اطلاعیه یافت نشد.");
        if (!a.IsActive) throw new InvalidOperationException("اطلاعیه غیرفعال است.");
        if (a.PublishTo != null && a.PublishTo < DateTime.Now)
            throw new InvalidOperationException("بازه انتشار این اطلاعیه به پایان رسیده است.");
        if (a.PublishFrom == null || a.PublishFrom > DateTime.Now) a.PublishFrom = DateTime.Now;
        var n = await BroadcastAsync(id, false, false, false, byUserId);
        a.NotifiedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return n;
    }

    /// <summary>بررسی دوره‌ای اطلاعیه‌های زمان‌بندی‌شده فرارسیده (توسط واچر، هر چند دقیقه).</summary>
    public async Task<int> CheckDueAnnouncementsAsync()
    {
        var now = DateTime.Now;
        var due = await _db.FaComAnnouncements
            .Where(a => a.IsActive && a.PublishFrom != null && a.PublishFrom <= now && a.NotifiedAt == null
                && (a.PublishTo == null || a.PublishTo >= now))
            .OrderBy(a => a.PublishFrom).Take(20).ToListAsync();
        var n = 0;
        foreach (var a in due)
        {
            try
            {
                var ids = await AudienceUserIdsAsync(a);
                await NotifyManyAsync(ids, a.Title, a.Body, "fa-com/announcements", false, false, false, 0);
                a.NotifiedAt = now;
                await _db.SaveChangesAsync();
                n++;
            }
            catch { }
        }
        return n;
    }

    private async Task<FaComAnnouncementDto> MapAnnouncementAsync(FaComAnnouncement a)
    {
        string? unitName = null;
        if (a.OrgUnitId is > 0)
        {
            unitName = await _db.HrOrgUnits.AsNoTracking().Where(u => u.Id == a.OrgUnitId!.Value)
                .Select(u => u.Name).FirstOrDefaultAsync()
                ?? await _db.HrMainOrgNodes.AsNoTracking().Where(u => u.Id == a.OrgUnitId!.Value)
                    .Select(u => u.Name).FirstOrDefaultAsync();
        }
        return new FaComAnnouncementDto
        {
            Id = a.Id, Title = a.Title, Body = a.Body, Audience = (int)a.Audience,
            OrgUnitId = a.OrgUnitId, OrgUnitName = unitName,
            PublishFrom = a.PublishFrom, PublishTo = a.PublishTo, IsActive = a.IsActive,
            NotifiedAt = a.NotifiedAt,
            CreatedByName = a.CreatedByName, CreatedAt = a.CreatedAt
        };
    }

    // ==================== نظرسنجی‌ها ====================

    public async Task<List<FaComPollDto>> ListPollsAsync(int userId, Paging.Request? pagination = null)
    {
        var emp = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive);
        var orgUnitId = emp?.OrgUnitId;
        var polls = await _db.FaComPolls.AsNoTracking()
            .Where(x => x.Audience == FaComAudience.All || (x.Audience == FaComAudience.Unit
                && orgUnitId > 0 && x.OrgUnitId == orgUnitId))
            .OrderByDescending(x => x.Id).ToPageListAsync(pagination, defaultCap: 100);
        var list = new List<FaComPollDto>();
        foreach (var x in polls)
        {
            list.Add(await MapPollAsync(x, userId));
        }
        return list;
    }

    public async Task<List<FaComPollDto>> ManagePollsAsync(Paging.Request? pagination = null)
    {
        var polls = await _db.FaComPolls.AsNoTracking().OrderByDescending(x => x.Id).ToPageListAsync(pagination, defaultCap: 200);
        var list = new List<FaComPollDto>();
        foreach (var x in polls) list.Add(await MapPollAsync(x, null));
        return list;
    }

    public async Task<FaComPollDto> GetPollAsync(int id, int userId)
    {
        var x = await _db.FaComPolls.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("نظرسنجی یافت نشد.");
        var emp = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive);
        if (!PollVisibleTo(x, emp?.OrgUnitId)) throw new InvalidOperationException("شما مخاطب این نظرسنجی نیستید.");
        return await MapPollAsync(x, userId);
    }

    private static bool PollVisibleTo(FaComPoll x, int? orgUnitId)
        => x.Audience == FaComAudience.All
            || (x.Audience == FaComAudience.Unit && orgUnitId is > 0 && x.OrgUnitId == orgUnitId);

    private async Task<FaComPollDto> MapPollAsync(FaComPoll x, int? userId)
    {
        var opts = await _db.FaComPollOptions.AsNoTracking().Where(o => o.PollId == x.Id)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToListAsync();
        var counts = await _db.FaComVotes.Where(v => v.PollId == x.Id)
            .GroupBy(v => v.OptionId).ToDictionaryAsync(g => g.Key, g => g.Count());
        var total = counts.Values.Sum();
        var myVote = userId != null
            ? await _db.FaComVotes.AsNoTracking().FirstOrDefaultAsync(v => v.PollId == x.Id && v.UserId == userId.Value)
            : null;
        var unitName = x.OrgUnitId is > 0
            ? await _db.HrOrgUnits.AsNoTracking().Where(u => u.Id == x.OrgUnitId!.Value)
                .Select(u => u.Name).FirstOrDefaultAsync()
            : null;
        return new FaComPollDto
        {
            Id = x.Id, Title = x.Title, Description = x.Description, Audience = (int)x.Audience,
            OrgUnitId = x.OrgUnitId, OrgUnitName = unitName, IsActive = x.IsActive, CloseAt = x.CloseAt,
            CreatedByName = x.CreatedByName, CreatedAt = x.CreatedAt,
            Options = opts.Select(o => new FaComPollOptionDto
            {
                Id = o.Id, Text = o.Text, SortOrder = o.SortOrder,
                VotesCount = counts.TryGetValue(o.Id, out var n) ? n : 0,
                Percent = total > 0 ? Math.Round((counts.TryGetValue(o.Id, out var m) ? m : 0) * 100.0 / total, 1) : 0
            }).ToList(),
            TotalVotes = total,
            MyOptionId = myVote?.OptionId,
            IsOpen = x.IsActive && (x.CloseAt == null || x.CloseAt >= DateTime.Now)
        };
    }

    public async Task<FaComPollDto> SavePollAsync(int? id, FaComPollSaveDto dto, int byUserId, string byName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان نظرسنجی الزامی است.");
        var opts = (dto.Options ?? new()).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Distinct().ToList();
        if (opts.Count < 2) throw new InvalidOperationException("حداقل دو گزینه لازم است.");
        if (opts.Count > 10) throw new InvalidOperationException("حداکثر ده گزینه مجاز است.");
        if (dto.Audience is < 0 or > 1) throw new InvalidOperationException("مخاطب نامعتبر است.");
        if (dto.Audience == 1)
        {
            if (dto.OrgUnitId is not > 0) throw new InvalidOperationException("برای نظرسنجی واحدی، انتخاب واحد الزامی است.");
            if (!await _db.HrOrgUnits.AnyAsync(u => u.Id == dto.OrgUnitId!.Value))
                throw new InvalidOperationException("واحد سازمانی نامعتبر است.");
        }
        FaComPoll x;
        if (id == null)
        {
            x = new FaComPoll { CreatedByUserId = byUserId, CreatedByName = byName };
            _db.FaComPolls.Add(x);
        }
        else
        {
            x = await _db.FaComPolls.FindAsync(id.Value)
                ?? throw new InvalidOperationException("نظرسنجی یافت نشد.");
        }
        x.Title = dto.Title.Trim();
        x.Description = dto.Description?.Trim();
        x.Audience = (FaComAudience)dto.Audience;
        x.OrgUnitId = dto.Audience == 1 ? dto.OrgUnitId : null;
        x.IsActive = dto.IsActive;
        x.CloseAt = dto.CloseAt;
        await _db.SaveChangesAsync();
        var existing = await _db.FaComPollOptions.Where(o => o.PollId == x.Id).OrderBy(o => o.SortOrder).ToListAsync();
        if (!existing.Select(o => o.Text).SequenceEqual(opts))
        {
            if (await _db.FaComVotes.AnyAsync(v => v.PollId == x.Id))
                throw new InvalidOperationException("پس از ثبت رأی، گزینه‌ها قابل تغییر نیستند.");
            _db.FaComPollOptions.RemoveRange(existing);
            for (var i = 0; i < opts.Count; i++)
                _db.FaComPollOptions.Add(new FaComPollOption { PollId = x.Id, Text = opts[i], SortOrder = i + 1 });
            await _db.SaveChangesAsync();
        }
        await _notify.BroadcastChangedAsync("facom-polls");
        return await MapPollAsync(x, null);
    }

    public async Task DeletePollAsync(int id)
    {
        var x = await _db.FaComPolls.FindAsync(id)
            ?? throw new InvalidOperationException("نظرسنجی یافت نشد.");
        _db.FaComVotes.RemoveRange(_db.FaComVotes.Where(v => v.PollId == id));
        _db.FaComPollOptions.RemoveRange(_db.FaComPollOptions.Where(o => o.PollId == id));
        _db.FaComPolls.Remove(x);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("facom-polls");
    }

    public async Task<FaComPollDto> VoteAsync(int pollId, int optionId, int userId)
    {
        var x = await _db.FaComPolls.FindAsync(pollId)
            ?? throw new InvalidOperationException("نظرسنجی یافت نشد.");
        if (!x.IsActive) throw new InvalidOperationException("این نظرسنجی فعال نیست.");
        if (x.CloseAt != null && x.CloseAt < DateTime.Now)
            throw new InvalidOperationException("مهلت این نظرسنجی به پایان رسیده است.");
        var emp = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive);
        if (!PollVisibleTo(x, emp?.OrgUnitId)) throw new InvalidOperationException("شما مخاطب این نظرسنجی نیستید.");
        if (!await _db.FaComPollOptions.AnyAsync(o => o.Id == optionId && o.PollId == pollId))
            throw new InvalidOperationException("گزینه نامعتبر است.");
        var v = await _db.FaComVotes.FirstOrDefaultAsync(a => a.PollId == pollId && a.UserId == userId);
        if (v == null)
        {
            v = new FaComVote { PollId = pollId, UserId = userId };
            _db.FaComVotes.Add(v);
        }
        v.OptionId = optionId;
        v.VotedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("facom-polls");
        return await MapPollAsync(x, userId);
    }

    // ==================== تیکت‌ها ====================

    private async Task<(int Id, string Name)> RequireMyEmployeeAsync(int userId)
    {
        var emp = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive)
            ?? throw new InvalidOperationException("کاربر شما به هیچ پرسنل فعالی متصل نیست؛ با مدیر سیستم هماهنگ کنید.");
        return (emp.Id, emp.FirstName + " " + emp.LastName);
    }

    public async Task<List<FaComTicketDto>> GetMyTicketsAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await RequireMyEmployeeAsync(userId);
        var rows = await _db.FaComTickets.AsNoTracking()
            .Where(t => t.EmployeeId == me.Id)
            .OrderByDescending(t => t.CreatedAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 500);
        var list = new List<FaComTicketDto>();
        foreach (var t in rows) list.Add(await MapTicketAsync(t, false));
        return list;
    }

    public async Task<FaComTicketDto> GetMyTicketAsync(int id, int userId)
    {
        var me = await RequireMyEmployeeAsync(userId);
        var t = await _db.FaComTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");
        if (t.EmployeeId != me.Id) throw new InvalidOperationException("دسترسی به این تیکت ندارید.");
        return await MapTicketAsync(t, true);
    }

    public async Task<List<FaComTicketDto>> ListTicketsAsync(int? status, int? category, Paging.Request? pagination = null)
    {
        var q = _db.FaComTickets.AsNoTracking().AsQueryable();
        if (status is >= 0) q = q.Where(t => (int)t.Status == status.Value);
        if (category is >= 0) q = q.Where(t => (int)t.Category == category.Value);
        var rows = await q.OrderBy(t => t.Status).ThenByDescending(t => t.CreatedAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 1000);
        var list = new List<FaComTicketDto>();
        foreach (var t in rows) list.Add(await MapTicketAsync(t, false));
        return list;
    }

    public async Task<FaComTicketDto> GetTicketAsync(int id)
    {
        var t = await _db.FaComTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");
        return await MapTicketAsync(t, true);
    }

    public async Task<FaComTicketDto> CreateTicketAsync(int userId, FaComTicketSaveDto dto, bool asAdmin)
    {
        if (string.IsNullOrWhiteSpace(dto.Subject)) throw new InvalidOperationException("موضوع تیکت الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new InvalidOperationException("متن تیکت الزامی است.");
        if (dto.Category is < 0 or > 5) throw new InvalidOperationException("دسته تیکت نامعتبر است.");
        int empId;
        string empName;
        if (asAdmin && dto.EmployeeId is > 0)
        {
            var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId!.Value)
                ?? throw new InvalidOperationException("پرسنل نامعتبر است.");
            empId = emp.Id; empName = emp.FirstName + " " + emp.LastName;
        }
        else
        {
            var me = await RequireMyEmployeeAsync(userId);
            empId = me.Id; empName = me.Name;
        }
        var t = new FaComTicket
        {
            EmployeeId = empId, Subject = dto.Subject.Trim(), Body = dto.Body.Trim(),
            Category = (FaComTicketCategory)dto.Category, Priority = Math.Clamp(dto.Priority, 0, 2)
        };
        _db.FaComTickets.Add(t);
        await _db.SaveChangesAsync();
        await _notify.SendToRoleAsync("HrManager", "تیکت جدید HR", $"{empName}: {t.Subject}",
            FromName, FormName, "fa-com/tickets");
        return await MapTicketAsync(t, false);
    }

    public async Task<FaComTicketDto> ReplyMyAsync(int userId, string userName, FaComReplySaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new InvalidOperationException("متن پاسخ الزامی است.");
        var t = await _db.FaComTickets.FirstOrDefaultAsync(x => x.Id == dto.TicketId)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");
        var me = await RequireMyEmployeeAsync(userId);
        if (t.EmployeeId != me.Id) throw new InvalidOperationException("دسترسی به این تیکت ندارید.");
        if (t.Status == FaComTicketStatus.Closed)
            throw new InvalidOperationException("تیکت بسته شده است؛ برای پیگیری مجدد تیکت جدید ثبت کنید.");
        _db.FaComReplies.Add(new FaComReply
        {
            TicketId = t.Id, UserId = userId, UserName = userName,
            Body = dto.Body.Trim(), IsHrReply = false
        });
        if (t.Status == FaComTicketStatus.Answered) t.Status = FaComTicketStatus.InProgress;
        await _db.SaveChangesAsync();
        await _notify.SendToRoleAsync("HrManager", "پاسخ جدید در تیکت", $"{me.Name}: {t.Subject}",
            FromName, FormName, "fa-com/tickets");
        return await MapTicketAsync(t, true);
    }

    public async Task<FaComTicketDto> ReplyHrAsync(FaComReplySaveDto dto, int byUserId, string byName)
    {
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new InvalidOperationException("متن پاسخ الزامی است.");
        var t = await _db.FaComTickets.FirstOrDefaultAsync(x => x.Id == dto.TicketId)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");
        if (t.Status == FaComTicketStatus.Closed)
            throw new InvalidOperationException("تیکت بسته است؛ ابتدا آن را بازگشایی کنید.");
        _db.FaComReplies.Add(new FaComReply
        {
            TicketId = t.Id, UserId = byUserId, UserName = byName,
            Body = dto.Body.Trim(), IsHrReply = true
        });
        t.Status = FaComTicketStatus.Answered;
        await _db.SaveChangesAsync();
        var ownerUser = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == t.EmployeeId)
            .Select(e => e.SystemUserId).FirstOrDefaultAsync();
        if (ownerUser is > 0)
            await _notify.SendAsync(ownerUser!.Value, "پاسخ HR به تیکت شما", t.Subject,
                FromName, FormName, "fa-com/my-tickets");
        return await MapTicketAsync(t, true);
    }

    public async Task<FaComTicketDto> SetStatusAsync(int id, int status, string byName)
    {
        if (status is < 0 or > 3) throw new InvalidOperationException("وضعیت نامعتبر است.");
        var t = await _db.FaComTickets.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");
        var wasClosed = t.Status == FaComTicketStatus.Closed;
        t.Status = (FaComTicketStatus)status;
        string? note = null;
        if (t.Status == FaComTicketStatus.Closed)
        {
            t.ClosedAt = DateTime.Now; t.ClosedByName = byName;
            note = "تیکت شما بسته شد.";
        }
        else if (wasClosed)
        {
            t.ClosedAt = null; t.ClosedByName = null;
            note = "تیکت شما بازگشایی شد.";
        }
        await _db.SaveChangesAsync();
        if (note != null)
        {
            var ownerUser = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == t.EmployeeId)
                .Select(e => e.SystemUserId).FirstOrDefaultAsync();
            if (ownerUser is > 0)
                await _notify.SendAsync(ownerUser!.Value, note, t.Subject, FromName, FormName, "fa-com/my-tickets");
        }
        return await MapTicketAsync(t, true);
    }

    public async Task DeleteTicketAsync(int id)
    {
        var t = await _db.FaComTickets.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");
        var replies = await _db.FaComReplies.Where(r => r.TicketId == id).ToListAsync();
        _db.FaComReplies.RemoveRange(replies);
        _db.FaComTickets.Remove(t);
        await _db.SaveChangesAsync();
    }

    private async Task<FaComTicketDto> MapTicketAsync(FaComTicket t, bool withReplies)
    {
        var empName = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == t.EmployeeId)
            .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
        var dto = new FaComTicketDto
        {
            Id = t.Id, EmployeeId = t.EmployeeId, EmployeeName = empName,
            Subject = t.Subject, Body = t.Body, Category = (int)t.Category, Priority = t.Priority,
            Status = (int)t.Status, CreatedAt = t.CreatedAt, ClosedAt = t.ClosedAt, ClosedByName = t.ClosedByName,
            RepliesCount = await _db.FaComReplies.CountAsync(r => r.TicketId == t.Id)
        };
        if (withReplies)
            dto.Replies = await _db.FaComReplies.AsNoTracking().Where(r => r.TicketId == t.Id)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new FaComReplyDto
                {
                    Id = r.Id, TicketId = r.TicketId, UserId = r.UserId, UserName = r.UserName,
                    Body = r.Body, IsHrReply = r.IsHrReply, CreatedAt = r.CreatedAt
                }).ToListAsync();
        return dto;
    }

    // ==================== توزیع چندکاناله ====================

    /// <summary>
    /// کانال درون‌سیستمی همیشه ارسال می‌شود (رد حسابرسی داخل اپ)؛ ایمیل/پوش/پیامک اختیاری‌اند.
    /// </summary>
    public async Task<int> NotifyManyAsync(IEnumerable<int> userIds, string title, string? body, string? link,
        bool email, bool push, bool sms, int byUserId)
    {
        var ids = userIds.Where(u => u > 0).Distinct().Take(5000).ToList();
        if (ids.Count == 0) return 0;
        await _notify.SendManyAsync(ids, title, body, FromName, FormName, link);
        if (push)
        {
            try { await _push.SendToUsersAsync(ids, title, body, link); }
            catch (Exception ex) { Console.WriteLine($"[FaCom] Push خطا: {ex.Message}"); }
        }
        if (email)
        {
            var emails = await _db.HrEmployees.AsNoTracking()
                .Where(e => e.SystemUserId != null && ids.Contains(e.SystemUserId!.Value)
                         && e.Email != null && e.Email != "")
                .Select(e => e.Email!).Distinct().Take(500).ToListAsync();
            if (emails.Count > 0)
            {
                var accounts = await _email.GetDabirkhaneAccountsAsync();
                var acc = accounts.FirstOrDefault()
                    ?? throw new InvalidOperationException("حساب ایمیل دبیرخانه برای ارسال سیستمی تعریف نشده است.");
                var html = System.Net.WebUtility.HtmlEncode(body ?? "");
                await _email.SendAsync(new EmailComposeDto
                {
                    EmailAccountId = acc.EmailId,
                    To = string.Join(",", emails),
                    Subject = title,
                    Body = $"<div dir=\"rtl\" style=\"font-family:Tahoma\">{html}</div>"
                }, byUserId, true, Array.Empty<(string, string, byte[])>());
            }
        }
        if (sms)
        {
            if (!_sms.IsConfigured)
                throw new InvalidOperationException("درگاه پیامک پیکربندی نشده است (بخش Sms در تنظیمات).");
            var mobiles = await _db.HrEmployees.AsNoTracking()
                .Where(e => e.SystemUserId != null && ids.Contains(e.SystemUserId!.Value)
                         && e.Mobile != null && e.Mobile != "")
                .Select(e => e.Mobile!).Distinct().Take(500).ToListAsync();
            foreach (var m in mobiles)
            {
                try { await _sms.SendAsync(m, $"{title}\n{body}"); }
                catch (Exception ex) { Console.WriteLine($"[FaCom] SMS خطا ({m}): {ex.Message}"); }
            }
        }
        return ids.Count;
    }

    // ==================== تولدها ====================

    public async Task<int> CheckBirthdaysAsync()
    {
        var today = DateTime.Today;
        var emps = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.BirthDate != null
                && e.BirthDate!.Value.Month == today.Month && e.BirthDate!.Value.Day == today.Day)
            .ToListAsync();
        foreach (var e in emps.Where(x => x.SystemUserId is > 0))
            await _notify.SendAsync(e.SystemUserId!.Value, "تولدت مبارک 🎉",
                $"{e.FirstName} عزیز، زادروزت را تبریک می‌گوییم. 🎂", FromName, FormName, null);
        if (emps.Count > 0)
        {
            var names = string.Join("، ", emps.Select(e => e.FirstName + " " + e.LastName));
            await _notify.SendToRoleAsync("HrManager", "تولدهای امروز 🎂", names, FromName, FormName, null);
        }
        return emps.Count;
    }

    /// <summary>سالگرد استخدام (سنوات) — روزی که HireDate ماه/روزش با امروز یکی است و حداقل یک سال گذشته باشد.</summary>
    public async Task<int> CheckWorkAnniversariesAsync()
    {
        var today = DateTime.Today;
        var emps = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive
                && e.HireDate.Month == today.Month && e.HireDate.Day == today.Day
                && e.HireDate.Year < today.Year)
            .ToListAsync();
        foreach (var e in emps.Where(x => x.SystemUserId is > 0))
        {
            var years = today.Year - e.HireDate.Year;
            var yearsFa = Inventory.Shared.Fa.Digits(years.ToString());
            await _notify.SendAsync(e.SystemUserId!.Value, "سالگرد همکاری 🎊",
                $"{e.FirstName} عزیز، امروز {yearsFa} سال از همکاری شما با مجموعه می‌گذرد. از همراهی‌تان سپاسگزاریم. 🌟", FromName, FormName, null);
        }
        if (emps.Count > 0)
        {
            var names = string.Join("، ", emps.Select(e =>
                $"{e.FirstName} {e.LastName} ({Inventory.Shared.Fa.Digits((today.Year - e.HireDate.Year).ToString())} سال)"));
            await _notify.SendToRoleAsync("HrManager", "سالگردهای همکاری امروز 🎊", names, FromName, FormName, null);
        }
        return emps.Count;
    }

    // ==================== صندوق پیشنهادها ====================

    private static FaComSuggestionDto MapSuggestion(FaComSuggestion s, string? name) => new()
    {
        Id = s.Id, Title = s.Title, Body = s.Body, Category = s.Category, Status = (int)s.Status,
        IsAnonymous = s.IsAnonymous,
        EmployeeName = s.IsAnonymous ? "ناشناس" : (name ?? "—"),
        Response = s.Response, RespondedByName = s.RespondedByName, RespondedAt = s.RespondedAt,
        CreatedAt = s.CreatedAt
    };

    public async Task<List<FaComSuggestionDto>> MySuggestionsAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId);
        if (me == null) return new();
        var rows = await _db.FaComSuggestions.AsNoTracking()
            .Where(s => s.EmployeeId == me.Id).OrderByDescending(s => s.Id).ToPageListAsync(pagination, defaultCap: 200);
        var name = (me.FirstName + " " + me.LastName).Trim();
        return rows.Select(s => MapSuggestion(s, name)).ToList();
    }

    public async Task<FaComSuggestionDto> CreateSuggestionAsync(int userId, FaComSuggestionSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new InvalidOperationException("متن الزامی است.");
        var me = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SystemUserId == userId);
        var s = new FaComSuggestion
        {
            EmployeeId = me?.Id,
            Title = dto.Title.Trim(), Body = dto.Body.Trim(),
            Category = Math.Clamp(dto.Category, 0, 2), IsAnonymous = dto.IsAnonymous
        };
        _db.FaComSuggestions.Add(s);
        await _db.SaveChangesAsync();
        var name = me == null ? null : (me.FirstName + " " + me.LastName).Trim();
        return MapSuggestion(s, name);
    }

    public async Task<List<FaComSuggestionDto>> ListSuggestionsAsync(int? status, int? category, Paging.Request? pagination = null)
    {
        var q = _db.FaComSuggestions.AsNoTracking().AsQueryable();
        if (status is >= 0) q = q.Where(s => (int)s.Status == status.Value);
        if (category is >= 0) q = q.Where(s => s.Category == category.Value);
        var rows = await q.OrderBy(s => s.Status).ThenByDescending(s => s.Id).ToPageListAsync(pagination, defaultCap: 500);
        var empIds = rows.Where(s => s.EmployeeId != null).Select(s => s.EmployeeId!.Value).Distinct().ToList();
        var names = empIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.HrEmployees.AsNoTracking().Where(e => empIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
        return rows.Select(s => MapSuggestion(s,
            s.EmployeeId != null && names.TryGetValue(s.EmployeeId.Value, out var n) ? n : null)).ToList();
    }

    public async Task<FaComSuggestionDto> RespondSuggestionAsync(int id, FaComSuggestionRespondDto dto, string byName)
    {
        var s = await _db.FaComSuggestions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پیشنهاد یافت نشد.");
        s.Status = (FaComSuggestionStatus)Math.Clamp(dto.Status, 0, 4);
        s.Response = dto.Response?.Trim();
        s.RespondedByName = byName;
        s.RespondedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        if (s.EmployeeId is > 0)
        {
            var uid = await _db.HrEmployees.AsNoTracking()
                .Where(e => e.Id == s.EmployeeId!.Value).Select(e => e.SystemUserId).FirstOrDefaultAsync();
            if (uid is > 0)
            {
                var st = s.Status switch
                {
                    FaComSuggestionStatus.Reviewing => "در دست بررسی است",
                    FaComSuggestionStatus.Accepted => "پذیرفته شد",
                    FaComSuggestionStatus.Rejected => "پذیرفته نشد",
                    FaComSuggestionStatus.Done => "اجرا شد",
                    _ => "به‌روز شد"
                };
                try
                {
                    await _notify.SendAsync(uid.Value, "پاسخ صندوق پیشنهادها",
                        $"پیشنهاد «{s.Title}» {st}." +
                        (string.IsNullOrWhiteSpace(s.Response) ? "" : $" پاسخ: {s.Response}"),
                        FromName, FormName, "fa-com/my-suggestions");
                }
                catch { }
            }
        }
        var all = await ListSuggestionsAsync(null, null);
        return all.First(x => x.Id == id);
    }

    public async Task DeleteSuggestionAsync(int id)
    {
        var s = await _db.FaComSuggestions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پیشنهاد یافت نشد.");
        _db.FaComSuggestions.Remove(s);
        await _db.SaveChangesAsync();
    }
}
