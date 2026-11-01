using Inventory.Api.Services;
using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office;

// ============================================================
//  صورتجلسه — سرویس اصلی (ماژول «فرم‌های متفرقه»)
//
//  قاعدهٔ وضعیت:
//   • وضعیت عادی: «در حال بررسی» (InReview)
//   • وضعیت بند: «انجام شد» فقط وقتی هم مسئول اجرا و هم مسئول پیگیری تایید کرده باشند.
//   • وضعیت کلی: «اتمام نهایی» (Closed) فقط وقتی همهٔ بندها «انجام شد» باشند.
//     اگر بعداً هر بندی ویرایش/رد شود، صورتجلسه به «در حال بررسی» بازمی‌گردد.
// ============================================================

public interface IMeetingMinutesService
{
    Task<List<MinutesListItemDto>> GetListAsync(string? search, string? status, int userId, int companyId, Paging.Request? pagination = null);
    Task<MinutesDetailDto?> GetDetailAsync(int id, int userId, int companyId);

    /// <summary>کاربران فعال به شکل سبک (شناسه/نام) برای کمبوهای فرم — بدون نیاز به مجوز SystemUsers.</summary>
    Task<List<UserLookupItemDto>> GetUserOptionsAsync();

    /// <summary>
    /// آیا این صورتجلسه «در گردش» کاربر است؟ (ایجادکننده، حاضر/غایبِ ارسال‌شده،
    /// یا مسئول اجرا/پیگیریِ بند — پس از ارسال صورتجلسه به گردش).
    /// </summary>
    Task<bool> CanAccessAsync(int minutesId, int userId, int companyId);
    Task<int> SaveAsync(SaveMinutesDto dto, int userId, string userName, int companyId);
    Task SubmitAsync(int id, bool includeAbsentees, int userId, string userName);
    Task DeleteAsync(int id, int userId);
    Task RemoveSignatureAsync(int minutesId, int participantUserId, int creatorUserId);
    Task<MinutesItemDto> AddItemAsync(int minutesId, SaveMinutesItemDto dto, int userId);
    Task<MinutesItemDto> UpdateItemAsync(int itemId, SaveMinutesItemDto dto, int userId);
    Task DeleteItemAsync(int itemId, int userId);
    Task<MinutesItemDto> RespDecisionAsync(int itemId, bool approved, string? note, int userId);
    Task<MinutesItemDto> FollowUpDecisionAsync(int itemId, string decision, string? note, int userId);
    Task SignAsync(int minutesId, int userId, string signatureBase64);
    /// <summary>خلاصهٔ متنی صورتجلسه برای نامه داخلی/صادره/ایمیل (itemId=0: کل)</summary>
    Task<string> BuildSummaryTextAsync(int minutesId, int itemId = 0);
}

public class MeetingMinutesService : IMeetingMinutesService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public MeetingMinutesService(AppDbContext db, INotifyService notify)
    {
        _db = db;
        _notify = notify;
    }

    // ------------------------------ ابزار ------------------------------

    private static string FaDate(DateTime? d)
    {
        if (d is null) return "—";
        var pc = new PersianCalendar();
        var v = d.Value;
        return $"{pc.GetYear(v)}/{pc.GetMonth(v):00}/{pc.GetDayOfMonth(v):00}";
    }

    private static string FullName(User? u) =>
        u == null ? "" :
        string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim();

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().Where(id => id > 0).ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => FullName(u));
    }

    /// <summary>محاسبهٔ وضعیت بند بر اساس تصمیمات</summary>
    private static void RecalcItem(MeetingMinutesItem item)
    {
        if (item.FollowUpDecision == "Rejected")
            item.ItemStatus = MinutesItemStatus.Rejected;
        else if (item.RespApproved && item.FollowUpDecision == "Approved")
            item.ItemStatus = MinutesItemStatus.Done;
        else
            item.ItemStatus = MinutesItemStatus.InProgress;
    }

    /// <summary>
    /// محاسبهٔ وضعیت کلی صورتجلسه — فقط وقتی همهٔ بندها «انجام شد» باشد
    /// «اتمام نهایی» می‌شود (حداقل یک بند لازم است).
    /// </summary>
    private bool RecalcMinutes(MeetingMinutes m, List<MeetingMinutesItem> items)
    {
        var wasClosed = m.Status == MeetingMinutesStatus.Closed;
        if (items.Count > 0 && items.All(i => i.ItemStatus == MinutesItemStatus.Done))
        {
            if (m.Status != MeetingMinutesStatus.Closed)
            {
                m.Status = MeetingMinutesStatus.Closed;
                m.ClosedAt = DateTime.Now;
            }
        }
        else
        {
            if (wasClosed)
            {
                m.Status = MeetingMinutesStatus.InReview;
                m.ClosedAt = null;
            }
        }
        return wasClosed != (m.Status == MeetingMinutesStatus.Closed); // آیا وضعیت کلی تغییر کرد؟
    }

    /// <summary>تاریخ پیگیری نمی‌تواند قبل از تاریخ انجام باشد.</summary>
    private static void ValidateItemDates(DateTime? dueDate, DateTime? followUpDate)
    {
        if (dueDate is null || followUpDate is null) return;
        if (followUpDate.Value.Date < dueDate.Value.Date)
            throw new Exception("تاریخ پیگیری نمی‌تواند قبل از تاریخ انجام باشد.");
    }

    private static MinutesItemDto ItemDto(MeetingMinutesItem i) => new()
    {
        Id = i.Id,
        RowNo = i.RowNo,
        Description = i.Description,
        DueDate = i.DueDate,
        ResponsibleUserId = i.ResponsibleUserId,
        ResponsibleName = i.ResponsibleName,
        FollowUpDate = i.FollowUpDate,
        FollowUpUserId = i.FollowUpUserId,
        FollowUpName = i.FollowUpName,
        ItemStatus = i.ItemStatus,
        RespApproved = i.RespApproved,
        RespApprovedAt = i.RespApprovedAt,
        RespNote = i.RespNote,
        FollowUpDecision = i.FollowUpDecision,
        FollowUpDecidedAt = i.FollowUpDecidedAt,
        FollowUpNote = i.FollowUpNote
    };

    private async Task<MeetingMinutes> MustGetAsync(int id)
    {
        var m = await _db.MeetingMinutes.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (m == null) throw new Exception("صورتجلسه پیدا نشد.");
        return m;
    }

    // ------------------------------ «در گردش» ------------------------------

    /// <summary>
    /// صورتجلسه‌هایی که برای این کاربر «در گردش» هستند — تنها همین‌ها در فهرست
    /// او دیده می‌شوند و فقط با حدس‌زدن شناسه (ID) در مرورگر هم قابل باز شدن نیستند:
    ///   • ایجادکننده (همیشه)،
    ///   • حاضر/غایبی که صورتجلسه برای او ارسال شده است (Notified)،
    ///   • مسئول اجرا یا مسئول پیگیریِ یکی از بندها — مشروط بر اینکه صورتجلسه
    ///     قبلاً «ارسال به گردش» شده باشد (نه پیش‌نویسِ در دستِ ثبت‌کننده).
    /// </summary>
    private IQueryable<MeetingMinutes> InCirculationQuery(int userId, int companyId) =>
        _db.MeetingMinutes.AsNoTracking().Where(m => !m.IsDeleted && m.CompanyId == companyId &&
            (m.CreatedByUserId == userId ||
             _db.MeetingMinutesParticipants.Any(p => p.MinutesId == m.Id && p.UserId == userId && p.Notified) ||
             (_db.MeetingMinutesParticipants.Any(p => p.MinutesId == m.Id && p.Notified) &&
              _db.MeetingMinutesItems.Any(i => i.MinutesId == m.Id &&
                  (i.ResponsibleUserId == userId || i.FollowUpUserId == userId)))));

    public Task<bool> CanAccessAsync(int minutesId, int userId, int companyId)
        => InCirculationQuery(userId, companyId).AnyAsync(m => m.Id == minutesId);

    private async Task<MeetingMinutesItem> MustGetItemAsync(int itemId, int userId)
    {
        var item = await _db.MeetingMinutesItems.FirstOrDefaultAsync(x => x.Id == itemId);
        if (item == null) throw new Exception("بند پیدا نشد.");
        var m = await _db.MeetingMinutes.FirstOrDefaultAsync(x => x.Id == item.MinutesId && !x.IsDeleted);
        if (m == null) throw new Exception("صورتجلسه پیدا نشد.");
        if (m.Status == MeetingMinutesStatus.Closed)
            throw new Exception("صورتجلسه «اتمام نهایی» شده — تغییرات دیگر ممکن نیست.");
        return item;
    }

    // ------------------------------ فهرست ------------------------------

    public async Task<List<MinutesListItemDto>> GetListAsync(string? search, string? status, int userId, int companyId, Paging.Request? pagination = null)
    {
        // فقط صورتجلسه‌هایی که «در گردش کاربر» هستند (ایجادکننده، حاضر/غایبِ
        // ارسال‌شده، یا مسئول اجرا/پیگیری بند). سایر موارد — از جمله پیش‌نویس‌هایی
        // که هنوز برای کاربر ارسال نشده‌اند — در فهرست او نمی‌آید.
        var q = InCirculationQuery(userId, companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(m => m.Title.Contains(s) || m.CreatedByName.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(m => m.Status == status);

        var rows = await q.OrderByDescending(m => m.Id).Select(m => new
        {
            m.Id, m.Title, m.MeetingDate, m.DateRegistered, m.Status, m.ClosedAt,
            m.CreatedByUserId, m.CreatedByName,
            Att = _db.MeetingMinutesParticipants.Count(p => p.MinutesId == m.Id && p.Kind == MinutesParticipantKind.Attendee),
            Abs = _db.MeetingMinutesParticipants.Count(p => p.MinutesId == m.Id && p.Kind == MinutesParticipantKind.Absent),
            Items = _db.MeetingMinutesItems.Count(i => i.MinutesId == m.Id),
            Done = _db.MeetingMinutesItems.Count(i => i.MinutesId == m.Id && i.ItemStatus == MinutesItemStatus.Done),
            Atts = _db.AppAttachments.Count(a => a.Module == "MeetingMinutes" && a.RefId == m.Id)
        }).ToPageListAsync(pagination, defaultCap: 500);

        return rows.Select(r => new MinutesListItemDto
        {
            Id = r.Id, Title = r.Title, MeetingDate = r.MeetingDate, DateRegistered = r.DateRegistered,
            Status = r.Status, ClosedAt = r.ClosedAt,
            CreatedByUserId = r.CreatedByUserId, CreatedByName = r.CreatedByName,
            AttendeeCount = r.Att, AbsentCount = r.Abs,
            ItemCount = r.Items, ItemDoneCount = r.Done, AttachmentCount = r.Atts
        }).ToList();
    }

    // ------------------------------ جزئیات ------------------------------

    public async Task<MinutesDetailDto?> GetDetailAsync(int id, int userId, int companyId)
    {
        // عدم گردش عمداً مانند «پیدا نشد» پاسخ داده می‌شود تا وجود صورتجلسه افشا نشود
        // (کاربر با حدس‌زدن شناسه در مرورگر به صورتجلسهٔ دیگران دسترسی پیدا نمی‌کند).
        var m = await InCirculationQuery(userId, companyId).FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return null;

        var parts = await _db.MeetingMinutesParticipants.AsNoTracking()
            .Where(p => p.MinutesId == id)
            .OrderBy(p => p.Kind).ThenBy(p => p.Name)
            .ToListAsync();
        var items = await _db.MeetingMinutesItems.AsNoTracking()
            .Where(i => i.MinutesId == id)
            .OrderBy(i => i.RowNo).ThenBy(i => i.Id)
            .ToListAsync();

        return new MinutesDetailDto
        {
            Id = m.Id,
            Title = m.Title,
            MeetingDate = m.MeetingDate,
            DateRegistered = m.DateRegistered,
            Status = m.Status,
            ClosedAt = m.ClosedAt,
            CreatedByUserId = m.CreatedByUserId,
            CreatedByName = m.CreatedByName,
            Participants = parts.Select(p => new MinutesParticipantDto
            {
                Id = p.Id, UserId = p.UserId, Name = p.Name, Kind = p.Kind,
                SignatureData = p.SignatureData, SignedAt = p.SignedAt
            }).ToList(),
            Items = items.Select(ItemDto).ToList()
        };
    }

    public async Task<List<UserLookupItemDto>> GetUserOptionsAsync()
    {
        // جایگزین امن برای GET api/users (مجوز SystemUsers) — کاربر عادی دفتر باید بتواند
        // حاضرین/مسئول/پیگیری را انتخاب کند بدون دسترسی به مدیریت کاربران.
        var rows = await _db.Users.AsNoTracking().Where(u => u.IsActive).ToListAsync();
        return rows
            .Select(u => new UserLookupItemDto
            {
                Id = u.Id,
                Name = string.IsNullOrWhiteSpace(u.FirstName + u.LastName)
                    ? u.Username
                    : (u.FirstName + " " + u.LastName).Trim()
            })
            .OrderBy(x => x.Name)
            .ToList();
    }
    // ------------------------------ ساخت/ویرایش ------------------------------

    public async Task<int> SaveAsync(SaveMinutesDto dto, int userId, string userName, int companyId)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان صورتجلسه الزامی است.");
        if (dto.Title.Trim().Length > 300)
            throw new Exception("طول عنوان نباید بیشتر از ۳۰۰ نویسه باشد.");

        var attendees = dto.AttendeeUserIds.Distinct().Where(id => id > 0).ToList();
        var absent = dto.AbsentUserIds.Distinct().Where(id => id > 0 && !attendees.Contains(id)).ToList();

        if (attendees.Count == 0)
            throw new Exception("حداقل یک نفر حاضر انتخاب کنید.");

        // حاضرین/غایبین باید کاربر فعال باشند و تکراری نباشند
        var all = attendees.Concat(absent).Distinct().ToList();
        var valid = await _db.Users.AsNoTracking()
            .Where(u => all.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id).ToListAsync();
        var invalid = all.Except(valid).ToList();
        if (invalid.Count > 0)
            throw new Exception("برخی از افراد انتخاب‌شده معتبر یا فعال نیستند.");

        var names = await NamesAsync(all);
        var now = DateTime.Now;
        MeetingMinutes m;

        if (dto.Id > 0)
        {
            m = await MustGetAsync(dto.Id);
            if (m.CompanyId != companyId) throw new Exception("صورتجلسه متعلق به شرکت فعال نیست.");
            if (m.CreatedByUserId != userId)
                throw new Exception("فقط ایجادکننده صورتجلسه مجاز به ویرایش آن است.");
            if (m.Status == MeetingMinutesStatus.Closed)
                throw new Exception("صورتجلسه «اتمام نهایی» شده — امکان ویرایش نیست.");
            m.Title = dto.Title.Trim();
            m.MeetingDate = dto.MeetingDate;
        }
        else
        {
            m = new MeetingMinutes
            {
                Title = dto.Title.Trim(),
                MeetingDate = dto.MeetingDate,
                DateRegistered = now,
                Status = MeetingMinutesStatus.InReview,
                CreatedByUserId = userId,
                CreatedByName = userName,
                CompanyId = companyId
            };
            _db.MeetingMinutes.Add(m);
            await _db.SaveChangesAsync();
        }

        // ---------- شرکت‌کنندگان: جایگزینی کامل (غیرفعال‌شدگان حذف، جدیدها با Notified=false) ----------
        var oldParts = await _db.MeetingMinutesParticipants.Where(p => p.MinutesId == m.Id).ToListAsync();
        _db.MeetingMinutesParticipants.RemoveRange(oldParts.Where(p => !all.Contains(p.UserId)));
        foreach (var p in oldParts.Where(p => all.Contains(p.UserId)))
        {
            var newKind = attendees.Contains(p.UserId) ? MinutesParticipantKind.Attendee : MinutesParticipantKind.Absent;
            if (p.Kind != newKind)
            {
                p.Kind = newKind;
                p.Notified = false; // نوع حضور تغییر کرد → دوباره اطلاع‌رسانی شود
                p.SignatureData = null;
                p.SignedAt = null;
            }
            p.Name = names.GetValueOrDefault(p.UserId, p.Name);
        }
        var existingIds = oldParts.Select(p => p.UserId).ToHashSet();
        foreach (var uid in all.Where(uid => !existingIds.Contains(uid)))
            _db.MeetingMinutesParticipants.Add(new MeetingMinutesParticipant
            {
                MinutesId = m.Id,
                UserId = uid,
                Name = names.GetValueOrDefault(uid, uid.ToString()),
                Kind = attendees.Contains(uid) ? MinutesParticipantKind.Attendee : MinutesParticipantKind.Absent,
                Notified = false
            });

        // ---------- بندها: ویرایش موجود + ساخت جدید + حذف حذف‌شده‌ها ----------
        var oldItems = await _db.MeetingMinutesItems.Where(i => i.MinutesId == m.Id).ToListAsync();
        var incoming = dto.Items ?? new List<SaveMinutesItemDto>();
        var incomingIds = incoming.Where(i => i.Id > 0).Select(i => i.Id).ToHashSet();

        foreach (var old in oldItems.Where(i => !incomingIds.Contains(i.Id)))
            _db.MeetingMinutesItems.Remove(old);

        var newRows = new List<MeetingMinutesItem>();
        foreach (var inc in incoming)
        {
            if (string.IsNullOrWhiteSpace(inc.Description))
                throw new Exception("شرح هر بند الزامی است.");

            MeetingMinutesItem item;
            if (inc.Id > 0)
            {
                item = oldItems.First(i => i.Id == inc.Id);
            }
            else
            {
                item = new MeetingMinutesItem { MinutesId = m.Id };
                _db.MeetingMinutesItems.Add(item);
                newRows.Add(item);
            }
            item.Description = inc.Description.Trim();
            item.DueDate = inc.DueDate;
            item.FollowUpDate = inc.FollowUpDate;
            ValidateItemDates(inc.DueDate, inc.FollowUpDate);
            item.ResponsibleUserId = inc.ResponsibleUserId;
            item.ResponsibleName = inc.ResponsibleUserId is > 0 ? names.GetValueOrDefault(inc.ResponsibleUserId.Value) : null;
            item.FollowUpUserId = inc.FollowUpUserId;
            item.FollowUpName = inc.FollowUpUserId is > 0 ? names.GetValueOrDefault(inc.FollowUpUserId.Value) : null;

            // اگر مسئول‌ها عوض شدند، تصمیمات قبلی دیگر معتبر نیست
            if (item.RespApproved && inc.ResponsibleUserId != item.ResponsibleUserId)
            {
                item.RespApproved = false;
                item.RespApprovedAt = null;
                item.RespNote = null;
            }
            if (item.FollowUpDecision != null && inc.FollowUpUserId != item.FollowUpUserId)
            {
                item.FollowUpDecision = null;
                item.FollowUpDecidedAt = null;
                item.FollowUpNote = null;
            }
            RecalcItem(item);
        }

        // شماره‌گذاری ترتیبی بندها
        var ordered = incoming.Select(x => oldItems.FirstOrDefault(i => i.Id == x.Id))
            .Where(x => x != null)
            .Cast<MeetingMinutesItem>()
            .Concat(newRows)
            .ToList();
        for (var idx = 0; idx < ordered.Count; idx++)
            ordered[idx].RowNo = idx + 1;

        var itemsNow = await _db.MeetingMinutesItems.Where(i => i.MinutesId == m.Id).ToListAsync();
        var statusChanged = RecalcMinutes(m, itemsNow);
        await _db.SaveChangesAsync();

        await _notify.BroadcastChangedAsync("meeting-minutes");
        return m.Id;
    }

    // ------------------------------ ارسال به گردش + نوتیف ------------------------------

    /// <summary>
    /// ارسال صورتجلسه به گردش:
    /// حاضرین همیشه (اگر هنوز اطلاع‌رسانی نشده‌اند) نوتیف می‌گیرند؛
    /// غایبین فقط با اذین کاربر (IncludeAbsentees).
    /// </summary>
    public async Task SubmitAsync(int id, bool includeAbsentees, int userId, string userName)
    {
        var m = await MustGetAsync(id);

        // آیا قبلاً هم به گردش ارسال شده؟ (برای اینکه مسئولان بندها
        // با هر بار ارسال مجدد نوتیف تکراری نگیرند)
        var alreadyCirculated = await _db.MeetingMinutesParticipants.AnyAsync(p => p.MinutesId == id && p.Notified);

        var parts = await _db.MeetingMinutesParticipants
            .Where(p => p.MinutesId == id && !p.Notified)
            .ToListAsync();

        var targets = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).ToList();
        if (includeAbsentees)
            targets = parts.ToList();

        var notifiedIds = new HashSet<int>();
        if (targets.Count > 0)
        {
            var userMap = await NamesAsync(targets.Select(t => t.UserId));
            foreach (var t in targets)
            {
                t.Notified = true;
                notifiedIds.Add(t.UserId);
                var myName = userMap.GetValueOrDefault(t.UserId, t.Name);
                if (t.UserId != userId)
                {
                    var kindTag = t.Kind == MinutesParticipantKind.Absent ? " (غایب)" : "";
                    await _notify.SendAsync(t.UserId,
                        "صورتجلسه جدید 📄",
                        $"{m.Title} — تاریخ جلسه: {FaDate(m.MeetingDate)}{kindTag} — ثبت‌کننده: {userName}",
                        userName, "صورتجلسه", $"misc/minutes/{m.Id}");
                }
            }
            await _db.SaveChangesAsync();
        }

        // مسئولان اجرا و پیگیری بندها هم «در گردش» این صورتجلسه‌اند؛ در اولین ارسال
        // به گردش — اگر در فهرست حاضرین/غایبین نبودند — به آن‌ها هم اطلاع می‌دهیم.
        if (!alreadyCirculated)
        {
            var itemOwners = await _db.MeetingMinutesItems.AsNoTracking()
                .Where(i => i.MinutesId == id)
                .Select(i => new { i.ResponsibleUserId, i.FollowUpUserId })
                .ToListAsync();
            var ownerIds = itemOwners
                .SelectMany(x => new[] { x.ResponsibleUserId, x.FollowUpUserId })
                .Where(x => x is > 0).Select(x => x!.Value)
                .Distinct()
                .Where(uid => uid != userId && !notifiedIds.Contains(uid))
                .ToList();
            if (ownerIds.Count > 0)
            {
                foreach (var uid in ownerIds)
                {
                    await _notify.SendAsync(uid, "صورتجلسه جدید 📄",
                        $"{m.Title} — تاریخ جلسه: {FaDate(m.MeetingDate)} — بندی از این صورتجلسه به شما سپرده شده است. ثبت‌کننده: {userName}",
                        userName, "صورتجلسه", $"misc/minutes/{m.Id}");
                }
            }
        }

        await _notify.BroadcastChangedAsync("meeting-minutes");
    }

    // ------------------------------ حذف ------------------------------

    public async Task DeleteAsync(int id, int userId)
    {
        var m = await MustGetAsync(id);
        if (m.CreatedByUserId != userId)
            throw new Exception("فقط ایجادکننده صورتجلسه مجاز به حذف آن است.");
        if (await _db.MeetingMinutesParticipants.AnyAsync(p => p.MinutesId == id && p.SignedAt != null))
            throw new Exception("حداقل یک شرکت‌کننده امضا کرده است. ایجادکننده باید ابتدا امضای شرکت‌کنندگان را بردارد، سپس صورتجلسه را حذف کند.");
        m.IsDeleted = true;
        m.DeletedAt = DateTime.Now;
        m.DeletedByUserId = userId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("meeting-minutes");
    }

    // ------------------------------ بندها ------------------------------

    private async Task<MeetingMinutesItem> SaveItemCoreAsync(int minutesId, SaveMinutesItemDto dto)
    {
        var m = await MustGetAsync(minutesId);
        if (string.IsNullOrWhiteSpace(dto.Description))
            throw new Exception("شرح بند الزامی است.");

        MeetingMinutesItem item;
        if (dto.Id > 0)
        {
            item = await MustGetItemAsync(dto.Id, 0);
            if (item.MinutesId != m.Id)
                throw new Exception("بند به این صورتجلسه تعلق ندارد.");
        }
        else
        {
            item = new MeetingMinutesItem { MinutesId = m.Id };
            _db.MeetingMinutesItems.Add(item);
        }

        item.Description = dto.Description.Trim();
        item.DueDate = dto.DueDate;
        item.FollowUpDate = dto.FollowUpDate;
        ValidateItemDates(dto.DueDate, dto.FollowUpDate);
        item.ResponsibleUserId = dto.ResponsibleUserId;
        item.FollowUpUserId = dto.FollowUpUserId;

        var ids = new List<int>();
        if (dto.ResponsibleUserId is > 0) ids.Add(dto.ResponsibleUserId.Value);
        if (dto.FollowUpUserId is > 0) ids.Add(dto.FollowUpUserId.Value);
        var names = await NamesAsync(ids);
        item.ResponsibleName = dto.ResponsibleUserId is > 0 ? names.GetValueOrDefault(dto.ResponsibleUserId.Value) : null;
        item.FollowUpName = dto.FollowUpUserId is > 0 ? names.GetValueOrDefault(dto.FollowUpUserId.Value) : null;

        // تغییر مسئول‌ها → باطل‌شدن تصمیمات قبلی
        if (dto.ResponsibleUserId != item.ResponsibleUserId)
        {
            item.RespApproved = false;
            item.RespApprovedAt = null;
            item.RespNote = null;
        }
        if (dto.FollowUpUserId != item.FollowUpUserId)
        {
            item.FollowUpDecision = null;
            item.FollowUpDecidedAt = null;
            item.FollowUpNote = null;
        }
        RecalcItem(item);
        await _db.SaveChangesAsync();

        // شماره ترتیب بر اساس Id (جایگاه زمانی ساخت)
        var siblings = await _db.MeetingMinutesItems
            .Where(i => i.MinutesId == m.Id)
            .OrderBy(i => i.Id)
            .ToListAsync();
        for (var idx = 0; idx < siblings.Count; idx++)
            siblings[idx].RowNo = idx + 1;
        await _db.SaveChangesAsync();

        var itemsNow = await _db.MeetingMinutesItems.Where(i => i.MinutesId == m.Id).ToListAsync();
        var statusChanged = RecalcMinutes(m, itemsNow);
        await _db.SaveChangesAsync();

        if (statusChanged && m.Status == MeetingMinutesStatus.Closed)
            await NotifyClosedAsync(m);

        await _notify.BroadcastChangedAsync("meeting-minutes");
        return item;
    }

    public Task<MinutesItemDto> AddItemAsync(int minutesId, SaveMinutesItemDto dto, int userId)
        => WithDtoAsync(SaveItemCoreAsync(minutesId, new SaveMinutesItemDto
        {
            Id = 0,
            Description = dto.Description,
            DueDate = dto.DueDate,
            ResponsibleUserId = dto.ResponsibleUserId,
            FollowUpDate = dto.FollowUpDate,
            FollowUpUserId = dto.FollowUpUserId
        }));

    public async Task<MinutesItemDto> UpdateItemAsync(int itemId, SaveMinutesItemDto dto, int userId)
    {
        var item = await _db.MeetingMinutesItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null) throw new Exception("بند پیدا نشد.");
        dto.Id = itemId;
        return ItemDto(await SaveItemCoreAsync(item.MinutesId, dto));
    }

    private static async Task<MinutesItemDto> WithDtoAsync(Task<MeetingMinutesItem> t)
    {
        var item = await t;
        return ItemDto(item);
    }

    public async Task DeleteItemAsync(int itemId, int userId)
    {
        var item = await MustGetItemAsync(itemId, 0);
        var minutesId = item.MinutesId;
        _db.MeetingMinutesItems.Remove(item);
        var m = await MustGetAsync(minutesId);
        var siblings = await _db.MeetingMinutesItems.Where(i => i.MinutesId == minutesId).OrderBy(i => i.Id).ToListAsync();
        for (var idx = 0; idx < siblings.Count; idx++)
            siblings[idx].RowNo = idx + 1;
        var itemsNow = siblings;
        RecalcMinutes(m, itemsNow);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("meeting-minutes");
    }

    // ------------------------------ تصمیمات ------------------------------

    /// <summary>تایید انجام توسط مسئول اجرا بند</summary>
    public async Task<MinutesItemDto> RespDecisionAsync(int itemId, bool approved, string? note, int userId)
    {
        var item = await _db.MeetingMinutesItems.Include(i => i.Minutes).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item?.Minutes is null || item.Minutes.IsDeleted)
            throw new Exception("بند پیدا نشد.");
        if (item.Minutes.Status == MeetingMinutesStatus.Closed)
            throw new Exception("صورتجلسه «اتمام نهایی» شده — تغییرات دیگر ممکن نیست.");
        if (item.ResponsibleUserId != userId)
            throw new Exception("فقط مسئول اجرای این بند می‌تواند آن را تایید کند.");

        item.RespApproved = approved;
        item.RespApprovedAt = approved ? DateTime.Now : null;
        item.RespNote = approved ? Truncate(note) : null;
        RecalcItem(item);

        var statusChanged = RecalcMinutes(item.Minutes,
            await _db.MeetingMinutesItems.Where(i => i.MinutesId == item.MinutesId).ToListAsync());
        await _db.SaveChangesAsync();

        if (approved && item.FollowUpUserId is > 0 && item.FollowUpUserId != userId)
        {
            await _notify.SendAsync(item.FollowUpUserId.Value, "تایید مسئول اجرای بند ✅",
                $"بند «{Shorten(item.Description)}» صورتجلسه «{item.Minutes.Title}» توسط مسئول اجرا تایید شد و منتظر تایید شماست.",
                item.ResponsibleName ?? "مسئول اجرا", "صورتجلسه", $"misc/minutes/{item.MinutesId}");
        }
        if (item.ItemStatus == MinutesItemStatus.Done)
            await NotifyItemDoneAsync(item);
        else if (!approved)
            await _notify.SendAsync(item.Minutes.CreatedByUserId, "بازگشت بند صورتجلسه ⏪",
                $"بند «{Shorten(item.Description)}» صورتجلسه «{item.Minutes.Title}» توسط مسئول اجرا بازمگشت.",
                item.Minutes.CreatedByName, "صورتجلسه", $"misc/minutes/{item.MinutesId}");

        if (statusChanged && item.Minutes.Status == MeetingMinutesStatus.Closed)
            await NotifyClosedAsync(item.Minutes);

        await _notify.BroadcastChangedAsync("meeting-minutes");
        return ItemDto(item);
    }

    /// <summary>تصمیم مسئول پیگیری — تایید یا رد + شرح</summary>
    public async Task<MinutesItemDto> FollowUpDecisionAsync(int itemId, string decision, string? note, int userId)
    {
        if (decision != "Approved" && decision != "Rejected")
            throw new Exception("تصمیم نامعتبر است.");

        var item = await _db.MeetingMinutesItems.Include(i => i.Minutes).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item?.Minutes is null || item.Minutes.IsDeleted)
            throw new Exception("بند پیدا نشد.");
        if (item.Minutes.Status == MeetingMinutesStatus.Closed)
            throw new Exception("صورتجلسه «اتمام نهایی» شده — تغییرات دیگر ممکن نیست.");
        if (item.FollowUpUserId != userId)
            throw new Exception("فقط مسئول پیگیری این بند می‌تواند تایید/رد کند.");
        if (!string.IsNullOrWhiteSpace(item.FollowUpDecision))
            throw new Exception("برای این بند قبلاً تصمیم ثبت شده است و تایید/رد مجدد امکان‌پذیر نیست.");

        item.FollowUpDecision = decision;
        item.FollowUpDecidedAt = DateTime.Now;
        item.FollowUpNote = Truncate(note);
        RecalcItem(item);

        var minutes = item.Minutes;
        var statusChanged = RecalcMinutes(minutes,
            await _db.MeetingMinutesItems.Where(i => i.MinutesId == minutes.Id).ToListAsync());
        await _db.SaveChangesAsync();

        // اطلاع به ثبت‌کننده + مسئول اجرا
        var notifyTargets = new List<int>();
        if (minutes.CreatedByUserId != userId) notifyTargets.Add(minutes.CreatedByUserId);
        if (item.ResponsibleUserId is > 0 && item.ResponsibleUserId != userId) notifyTargets.Add(item.ResponsibleUserId.Value);

        if (decision == "Approved" && item.ItemStatus == MinutesItemStatus.Done)
        {
            foreach (var uid in notifyTargets.Distinct())
                await _notify.SendAsync(uid, "بند صورتجلسه انجام شد ✅",
                    $"بند «{Shorten(item.Description)}» صورتجلسه «{minutes.Title}» تایید و انجام شد.{(string.IsNullOrWhiteSpace(note) ? "" : " — " + note)}",
                    item.FollowUpName ?? "مسئول پیگیری", "صورتجلسه", $"misc/minutes/{minutes.Id}");
        }
        else if (decision == "Rejected")
        {
            foreach (var uid in notifyTargets.Distinct())
                await _notify.SendAsync(uid, "بند صورتجلسه رد شد ⛔",
                    $"بند «{Shorten(item.Description)}» صورتجلسه «{minutes.Title}» توسط مسئول پیگیری رد شد.{(string.IsNullOrWhiteSpace(note) ? "" : " — " + note)}",
                    item.FollowUpName ?? "مسئول پیگیری", "صورتجلسه", $"misc/minutes/{minutes.Id}");
        }
        else
        {
            foreach (var uid in notifyTargets.Distinct())
                await _notify.SendAsync(uid, "تصمیم بند صورتجلسه",
                    $"بند «{Shorten(item.Description)}» صورتجلسه «{minutes.Title}» تایید شد؛ در انتظار تایید مسئول اجرا.",
                    item.FollowUpName ?? "مسئول پیگیری", "صورتجلسه", $"misc/minutes/{minutes.Id}");
        }

        if (statusChanged && minutes.Status == MeetingMinutesStatus.Closed)
            await NotifyClosedAsync(minutes);

        await _notify.BroadcastChangedAsync("meeting-minutes");
        return ItemDto(item);
    }

    // ------------------------------ امضای الکترونیکی ------------------------------

    public async Task RemoveSignatureAsync(int minutesId, int participantUserId, int creatorUserId)
    {
        var m = await MustGetAsync(minutesId);
        if (m.CreatedByUserId != creatorUserId)
            throw new Exception("فقط ایجادکننده صورتجلسه می‌تواند امضای شرکت‌کنندگان را بردارد.");
        if (m.Status == MeetingMinutesStatus.Closed)
            throw new Exception("صورتجلسه نهایی شده و امکان تغییر امضا ندارد.");

        var part = await _db.MeetingMinutesParticipants
            .FirstOrDefaultAsync(p => p.MinutesId == minutesId && p.UserId == participantUserId);
        if (part is null) throw new Exception("شرکت‌کننده پیدا نشد.");
        part.SignatureData = null;
        part.SignedAt = null;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("meeting-minutes");
    }

    public async Task SignAsync(int minutesId, int userId, string signatureBase64)
    {
        var m = await MustGetAsync(minutesId);
        if (m.Status == MeetingMinutesStatus.Closed)
            throw new Exception("صورتجلسه «اتمام نهایی» شده — امضا دیگر پذیرفته نمی‌شود.");

        var sig = signatureBase64.Trim();
        if (sig.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            sig = sig[(sig.IndexOf(',') + 1)..];
        if (string.IsNullOrWhiteSpace(sig) || sig.Length > 500_000)
            throw new Exception("تصویر امضا معتبر نیست.");

        var part = await _db.MeetingMinutesParticipants
            .FirstOrDefaultAsync(p => p.MinutesId == minutesId && p.UserId == userId);
        if (part == null)
            throw new Exception("شما در فهرست شرکت‌کنندگان این صورتجلسه نیستید.");
        if (part.Kind != MinutesParticipantKind.Attendee)
            throw new Exception("امضا فقط برای حاضرین جلسه است.");

        part.SignatureData = sig;
        part.SignedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        if (m.CreatedByUserId != userId)
        {
            var names = await NamesAsync(new[] { userId });
            var myName = names.GetValueOrDefault(userId, "کاربر");
            await _notify.SendAsync(m.CreatedByUserId, "امضای صورتجلسه ✍️",
                $"{myName} روی صورتجلسه «{m.Title}» امضا کرد.",
                myName, "صورتجلسه", $"misc/minutes/{m.Id}");
        }
        await _notify.BroadcastChangedAsync("meeting-minutes");
    }

    // ------------------------------ نوتیف‌های وضعیت ------------------------------

    private async Task NotifyItemDoneAsync(MeetingMinutesItem item)
    {
        var m = item.Minutes;
        if (m.CreatedByUserId == item.ResponsibleUserId) return;
        await _notify.SendAsync(m.CreatedByUserId, "بند صورتجلسه انجام شد ✅",
            $"بند «{Shorten(item.Description)}» صورتجلسه «{m.Title}» توسط مسئول اجرا و مسئول پیگیری تایید شد.",
            item.ResponsibleName ?? "مسئول اجرا", "صورتجلسه", $"misc/minutes/{m.Id}");
    }

    private async Task NotifyClosedAsync(MeetingMinutes m)
    {
        var targets = await _db.MeetingMinutesParticipants.AsNoTracking()
            .Where(p => p.MinutesId == m.Id)
            .Select(p => p.UserId).ToListAsync();
        foreach (var uid in targets.Distinct())
        {
            if (uid == m.CreatedByUserId) continue;
            await _notify.SendAsync(uid, "صورتجلسه بسته شد 🏁",
                $"صورتجلسه «{m.Title}» — همهٔ بندها انجام شد و وضعیت «اتمام نهایی» شد.",
                m.CreatedByName, "صورتجلسه", $"misc/minutes/{m.Id}");
        }
    }

    // ------------------------------ خلاصهٔ متنی (نامه/ایمیل) ------------------------------

    public async Task<string> BuildSummaryTextAsync(int minutesId, int itemId = 0)
    {
        var m = await _db.MeetingMinutes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == minutesId && !x.IsDeleted)
            ?? throw new Exception("صورتجلسه پیدا نشد.");
        var parts = await _db.MeetingMinutesParticipants.AsNoTracking()
            .Where(p => p.MinutesId == minutesId)
            .OrderBy(p => p.Kind).ThenBy(p => p.Name).ToListAsync();
        var items = await _db.MeetingMinutesItems.AsNoTracking()
            .Where(i => i.MinutesId == minutesId)
            .OrderBy(i => i.RowNo).ThenBy(i => i.Id).ToListAsync();

        if (itemId > 0)
        {
            var it = items.FirstOrDefault(i => i.Id == itemId);
            if (it == null) throw new Exception("بند پیدا نشد.");
            var sbItem = new System.Text.StringBuilder();
            sbItem.AppendLine($"صورتجلسه: {m.Title}");
            sbItem.AppendLine($"تاریخ جلسه: {FaDate(m.MeetingDate)}");
            sbItem.AppendLine($"بند {it.RowNo}: {it.Description}");
            if (it.ResponsibleName is not null) sbItem.AppendLine($"مسئول: {it.ResponsibleName} — تاریخ انجام: {FaDate(it.DueDate)}");
            if (it.FollowUpName is not null) sbItem.AppendLine($"مسئول پیگیری: {it.FollowUpName} — تاریخ پیگیری: {FaDate(it.FollowUpDate)}");
            sbItem.AppendLine($"وضعیت بند: {MinutesItemStatus.ToFa(it.ItemStatus)}");
            return sbItem.ToString();
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"صورتجلسه: {m.Title}");
        sb.AppendLine($"تاریخ جلسه: {FaDate(m.MeetingDate)}");
        sb.AppendLine($"تاریخ ثبت: {FaDate(m.DateRegistered)}");
        var att = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).Select(p => p.Name).ToList();
        var abs = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).Select(p => p.Name).ToList();
        sb.AppendLine($"حاضرین: {(att.Count > 0 ? string.Join("، ", att) : "—")}");
        sb.AppendLine($"غایبین: {(abs.Count > 0 ? string.Join("، ", abs) : "—")}");
        sb.AppendLine();
        sb.AppendLine("بندهای مصوبه:");
        foreach (var it in items)
        {
            sb.AppendLine($"بند {it.RowNo}: {it.Description}");
            sb.AppendLine($"  مسئول: {it.ResponsibleName ?? "—"} | تاریخ انجام: {FaDate(it.DueDate)}");
            sb.AppendLine($"  مسئول پیگیری: {it.FollowUpName ?? "—"} | تاریخ پیگیری: {FaDate(it.FollowUpDate)}");
            sb.AppendLine($"  وضعیت: {MinutesItemStatus.ToFa(it.ItemStatus)}");
        }
        sb.AppendLine();
        sb.AppendLine($"وضعیت کلی: {MeetingMinutesStatus.ToFa(m.Status)}");
        return sb.ToString();
    }

    // ------------------------------ ابزار کوچک ------------------------------

    private static string? Truncate(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return null;
        return s.Length > 500 ? s[..500] : s;
    }

    private static string Shorten(string s)
    {
        s = (s ?? "").Trim().Replace('\n', ' ');
        return s.Length > 60 ? s[..60] + "…" : s;
    }
}
