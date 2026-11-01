using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// پیاده‌سازی سرویس اصلی مدیریت نامه‌های وارده (Incoming Letters Service)
/// </summary>
public interface IIncomingLetterService
{
    Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly, int page = 1, int pageSize = 15, bool? starredOnly = null,
        bool? attachOnly = null, bool? workOrderOnly = null);
    Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(int userId, string? search, int page = 1, int pageSize = 15);
    Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(int userId, string? search, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null);
    Task<IncomingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin, bool canDabirkhane = false);
    Task<IncomingLetterCartableStatsDto> GetStatsAsync(int userId);
    Task<int> AddAsync(AddIncomingLetterDto dto, int userId);
    Task<int> FinalizeAsync(int letterId, int userId, bool isAdmin);
    /// <summary>ویرایش نامهٔ وارده؛ خروجی = تعداد کسانی که نامه را دیده بودند و اعلانِ ویرایش گرفتند.</summary>
    Task<int> EditAsync(int letterId, EditIncomingLetterDto dto, int userId, bool isAdmin, bool canDabirkhane = false);
    Task DeleteAsync(int letterId, int userId, bool isAdmin);
    Task<bool> ToggleLetterNeshanAsync(int letterId, int userId);
    Task<PagedResult<IncomingLetterPickDto>> PickListAsync(int userId, string? search, int page = 1, int pageSize = 15);

    // رزرو شماره
    Task<List<LetterNumberReservationDto>> GetReservationsAsync(int userId, int typeForm = 3, Paging.Request? pagination = null);
    Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int userId, int typeForm = 3, int count = 1);
}

public class IncomingLetterService : IIncomingLetterService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly FileStore _store;
    private readonly ILetterStratureService _strature;

    public IncomingLetterService(AppDbContext db, INotifyService notify, FileStore store, ILetterStratureService strature)
    {
        _db = db;
        _notify = notify;
        _store = store;
        _strature = strature;
    }

    /// <summary>
    /// ساخت شمارهٔ نامهٔ وارده از ساختار پیکربندی‌شده (TypeForm=3) — قابل تنظیم از
    /// «تنظیمات ← ساختار شماره نامه». اگر ساختاری تعریف نشده باشد، فرمت قبلی
    /// «سال/V-شماره» (مثل 1404/V/12) حفظ می‌شود تا شمارهٔ نامه‌های موجود و
    /// نصب‌های پیکربندی‌نشده نشکنند — همان قراردادی که نامهٔ داخلی و صادره دارند.
    ///
    /// date: تاریخ ورود/ثبت نامه (DateErsal) — مبنای جزء «سال». پیش‌تر سال از
    /// DateTime.Now گرفته می‌شد و نامهٔ backdate‌شده شمارهٔ سالِ امروز را می‌گرفت.
    /// </summary>
    private async Task<string> BuildLetterNumberAsync(int numberSabt, DateTime date)
    {
        var s = await _strature.TotalNumberAsync(numberSabt, typeForm: 3, date: date);
        if (!string.IsNullOrEmpty(s)) return s;

        var pc = new PersianCalendar();
        return $"{pc.GetYear(date)}/V/{numberSabt}";
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly, int page = 1, int pageSize = 15, bool? starredOnly = null,
        bool? attachOnly = null, bool? workOrderOnly = null)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && e.IsBayegani != true);

        if (unreadOnly == true) q = q.Where(e => !e.IsRead);

        // باگ B1: پوشهٔ «نشان‌شده» در کارتابل تا پیش از این هیچ فیلتری اعمال نمی‌کرد و
        // دقیقاً همان لیست «دریافتی» را نشان می‌داد. فیلتر باید «سمت سرور» باشد چون
        // فیلتر سمت کلاینت فقط صفحهٔ جاری را محدود می‌کند و نامه‌های نشان‌شدهٔ صفحات
        // دیگر هرگز دیده نمی‌شدند. نشان روی Erja.IsNeshan است، یعنی به‌ازای هر کاربر —
        // درست همان موجودیتی که ToggleNeshanAsync تغییرش می‌دهد.
        //
        // پارامتر عمداً در «انتهای» امضا اضافه شد تا چهار فراخوان موجودِ AiTools و
        // AiBriefingService (که positionally صدا می‌زنند) نشکنند.
        if (starredOnly == true) q = q.Where(e => e.IsNeshan);
        // «پیوست‌دار» هم سمت سرور — وگرنه فقط نامه‌های پیوست‌دارِ صفحهٔ جاری دیده می‌شدند
        if (attachOnly == true)
            q = q.Where(e => _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId));
        // «دستورکارشده» هم سمت سرور — پیش‌تر فقط روی صفحهٔ جاری فیلتر می‌شد و
        // شمارش/صفحه‌بندی با نتیجهٔ نمایش هم‌خوان نبود.
        if (workOrderOnly == true)
            q = q.Where(e => _db.WorkOrders.Any(w => w.SourceModule == "IncomingLetter" && w.SourceId == e.SourceId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.IncomingLetter!.Title.Contains(s)
                             || (e.Source.IncomingLetter!.LetterNumber ?? "").Contains(s)
                             || (e.Source.IncomingLetter!.NumberLetterVarede ?? "").Contains(s)
                             || e.Source.IncomingLetter!.Ferestande.Contains(s)
                             || (e.UserSender!.FirstName + " " + e.UserSender.LastName).Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q
            // ترتیب بریدگی صفحه = ترتیب نمایش (تاریخ ارجاع، سپس شناسه)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.ErjaId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new IncomingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.IncomingLetter!.LetterNumber ?? "",
                NumberSabt = e.Source.IncomingLetter!.NumberSabt,
                NumberLetterVarede = e.Source.IncomingLetter!.NumberLetterVarede ?? "",
                Title = e.Source.IncomingLetter!.Title,
                Ferestande = e.Source.IncomingLetter!.Ferestande,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                Date = e.Source.IncomingLetter!.Date,
                DateErsal = e.Source.IncomingLetter!.DateErsal,
                TypeErsal = e.Source.IncomingLetter!.TypeErsal ?? "",
                DeliveryName = e.Source.IncomingLetter!.DeliveryName ?? "",
                Mahramanegi = e.Source.IncomingLetter!.Mahramanegi,
                Foriat = e.Source.IncomingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                IsBayegani = e.IsBayegani == true,
                HasAnswer = !string.IsNullOrEmpty(e.Answer),
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(int userId, string? search, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && e.IsBayegani == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.IncomingLetter!.Title.Contains(s)
                             || (e.Source.IncomingLetter!.LetterNumber ?? "").Contains(s)
                             || e.Source.IncomingLetter!.Ferestande.Contains(s));
        }

        // نامه‌ای که هم با پرچم ارجاع و هم با پرچم ثبت‌کننده بایگانی شده، دو بار در
        // فهرست نمی‌آید؛ ردیف «ثبت‌کننده» فقط وقتی نمایش داده می‌شود که ردیف ارجاعی
        // برای همین کاربر وجود نداشته باشد.
        var direct = _db.IncomingLetters.AsNoTracking()
            .Where(l => l.CreateUserId == userId && l.IsBayegani && !l.IsDelete && !l.Source.IsDelete
                        && !_db.Erjas.Any(e => e.SourceId == l.Id && e.ReciverUserId == userId
                                               && !e.IsDelete && e.IsBayegani == true));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            direct = direct.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.Ferestande.Contains(s));
        }
        // Page the UNION of lightweight keys in SQL; hydrate only those keys.
        var keys = q.Select(e => new { LetterId = e.SourceId, ErjaId = (int?)e.ErjaId,
                NumberSabt = e.Source.IncomingLetter!.NumberSabt })
            .Concat(direct.Select(l => new { LetterId = l.Id, ErjaId = (int?)null, NumberSabt = l.NumberSabt }));
        var totalCount = await keys.CountAsync();
        var pageKeys = await keys.OrderByDescending(k => k.NumberSabt)
            .ThenByDescending(k => k.LetterId).ThenBy(k => k.ErjaId)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        var erjaIds = pageKeys.Where(k => k.ErjaId.HasValue).Select(k => k.ErjaId!.Value).ToList();
        var directIds = pageKeys.Where(k => !k.ErjaId.HasValue).Select(k => k.LetterId).ToList();
        q = q.Where(e => erjaIds.Contains(e.ErjaId));
        direct = direct.Where(l => directIds.Contains(l.Id));

        var erjaItems = await q
            .Select(e => new IncomingLetterListItemDto
            {
                LetterId = e.SourceId, ErjaId = e.ErjaId,
                LetterNumber = e.Source.IncomingLetter!.LetterNumber ?? "", NumberSabt = e.Source.IncomingLetter!.NumberSabt,
                NumberLetterVarede = e.Source.IncomingLetter!.NumberLetterVarede ?? "", Title = e.Source.IncomingLetter!.Title,
                Ferestande = e.Source.IncomingLetter!.Ferestande,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName) ? e.UserSender.Username : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId, Date = e.Source.IncomingLetter!.Date, DateErsal = e.Source.IncomingLetter!.DateErsal,
                TypeErsal = e.Source.IncomingLetter!.TypeErsal ?? "", DeliveryName = e.Source.IncomingLetter!.DeliveryName ?? "",
                Mahramanegi = e.Source.IncomingLetter!.Mahramanegi, Foriat = e.Source.IncomingLetter!.Foriat,
                ErjaType = e.Type, MatnErja = e.MatnErja, MohlatPasokh = e.MohlatPasokh, IsNeshan = e.IsNeshan, IsRead = e.IsRead,
                IsBayegani = true, HasAnswer = !string.IsNullOrEmpty(e.Answer), ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId)
            }).ToListAsync();

        var directItems = await direct.Select(l => new IncomingLetterListItemDto
        {
            LetterId = l.Id, LetterNumber = l.LetterNumber ?? "", NumberSabt = l.NumberSabt,
            NumberLetterVarede = l.NumberLetterVarede ?? "", Title = l.Title, Ferestande = l.Ferestande,
            Sender = l.Ferestande, Date = l.Date, DateErsal = l.DateErsal, TypeErsal = l.TypeErsal ?? "",
            DeliveryName = l.DeliveryName ?? "", Mahramanegi = l.Mahramanegi, Foriat = l.Foriat,
            IsBayegani = true, IsRead = true, ReciverCount = _db.Erjas.Count(e => e.SourceId == l.Id && !e.IsDelete),
            HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == l.Id)
        }).ToListAsync();

        var items = erjaItems.Concat(directItems).OrderByDescending(x => x.NumberSabt)
            .ThenByDescending(x => x.LetterId).ThenBy(x => x.ErjaId).ToList();

        return new PagedResult<IncomingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(int userId, string? search, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.IncomingLetters.AsNoTracking()
            .Where(l => l.CreateUserId == userId && !l.IsDelete && !l.Source.IsDelete && !l.IsBayegani);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.Ferestande.Contains(s));
        }

        if (starredOnly == true) q = q.Where(l => l.IsNeshan);
        if (attachOnly == true)
            q = q.Where(l => _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == l.Id));

        var totalCount = await q.CountAsync();

        var items = await q
            // ترتیب بریدگی صفحه = ترتیب نمایش (تاریخ، سپس شناسه)
            .OrderByDescending(l => l.Date)
            .ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new IncomingLetterListItemDto
            {
                LetterId = l.Id,
                ErjaId = null,
                LetterNumber = l.LetterNumber ?? "",
                NumberSabt = l.NumberSabt,
                NumberLetterVarede = l.NumberLetterVarede ?? "",
                Title = l.Title,
                Ferestande = l.Ferestande,
                Sender = "شما",
                SenderUserId = userId,
                Date = l.Date,
                DateErsal = l.DateErsal,
                TypeErsal = l.TypeErsal ?? "",
                DeliveryName = l.DeliveryName ?? "",
                Mahramanegi = l.Mahramanegi,
                Foriat = l.Foriat,
                IsNeshan = l.IsNeshan,
                IsRead = true,
                IsBayegani = l.IsBayegani,
                ReciverCount = l.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == l.Id)
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IncomingLetterCartableStatsDto> GetStatsAsync(int userId)
    {
        var inboxQuery = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && e.IsBayegani != true);

        var totalInbox = await inboxQuery.CountAsync();
        var unreadCount = await inboxQuery.CountAsync(e => !e.IsRead);

        var totalSent = await _db.IncomingLetters.AsNoTracking()
            .CountAsync(l => l.CreateUserId == userId && !l.IsDelete && !l.Source.IsDelete);

        var totalArchive = await _db.Erjas.AsNoTracking()
            .CountAsync(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                             && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                             && e.IsBayegani == true);

        var totalStarred = await inboxQuery.CountAsync(e => e.IsNeshan);

        // عدد روی چیپ‌های «پیوست‌دار» و «دستورکارشده» — از همان کوئری «دریافتی» تا
        // با فهرست هم‌خوان باشد (فیلتر این دو چیپ سمت سرور اعمال می‌شود).
        var totalAttach = await inboxQuery.CountAsync(e =>
            _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId));
        var totalWorkOrder = await inboxQuery.CountAsync(e =>
            _db.WorkOrders.Any(w => w.SourceModule == "IncomingLetter" && w.SourceId == e.SourceId));

        return new IncomingLetterCartableStatsDto
        {
            TotalInbox = totalInbox,
            UnreadCount = unreadCount,
            TotalSent = totalSent,
            TotalArchive = totalArchive,
            TotalStarred = totalStarred,
            TotalAttach = totalAttach,
            TotalWorkOrder = totalWorkOrder
        };
    }

    public async Task<IncomingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin, bool canDabirkhane = false)
    {
        var letter = await _db.IncomingLetters
            .Include(l => l.Source)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);

        if (letter == null) return null;

        // ---------- بررسی دسترسی ----------
        // باگ قبلی: هیچ بررسی‌ای وجود نداشت و پارامتر isAdmin هرگز استفاده نمی‌شد؛
        // در نتیجه هر کاربر دارای مجوز عمومی «IncomingLetters.Read» می‌توانست با
        // حدس‌زدن letterId متن کامل و تمام گردش «هر» نامه‌ای را بخواند — از جمله
        // نامه‌های Mahramanegi=3 (سرّی).
        //
        // قاعدهٔ دسترسی دقیقاً هم‌راستا با ErjaService.GetGardeshTreeAsync است تا
        // «جزئیات نامه» و «درخت گردش» یک سطح محافظت داشته باشند:
        //   سازندهٔ نامه  |  حاضر در گردش (ارجاع/هامش)  |  مدیر
        //
        // نکته: این بررسی باید «پیش از» هر اثر جانبی انجام شود؛ وگرنه کاربر
        // غیرمجاز هم نامه را برای خودش خوانده‌شده علامت می‌زد.
        var myErjas = await _db.Erjas.Where(e => e.SourceId == letterId && e.ReciverUserId == userId && !e.IsDelete).ToListAsync();
        var inFlow = myErjas.Count > 0;
        if (!inFlow && letter.CreateUserId != userId && !isAdmin)
            throw new UnauthorizedAccessException("شما در گردش این نامه نیستید و اجازهٔ مشاهدهٔ آن را ندارید.");

        // علامت‌گذاری «رویت شد» برای ارجاع‌های همین کاربر
        // (باگ قبلی: شرط ذخیره پس از تغییر IsRead بررسی می‌شد و هرگز درست نبود)
        var hadUnread = false;
        foreach (var e in myErjas)
        {
            if (!e.IsRead)
            {
                e.IsRead = true;
                e.DateRead = DateTime.Now;
                hadUnread = true;
            }
        }
        if (hadUnread) await _db.SaveChangesAsync();

        var creator = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == letter.CreateUserId);
        var creatorName = creator != null
            ? (string.IsNullOrEmpty(creator.FirstName + creator.LastName) ? creator.Username : $"{creator.FirstName} {creator.LastName}".Trim())
            : "ثبت‌کننده سیستم";

        var allErjas = await _db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letterId && !e.IsDelete)
            .OrderBy(e => e.ErjaId)
            .Select(e => new ErjaDto
            {
                ErjaId = e.ErjaId,
                SenderUserId = e.SenderUserId,
                SenderName = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                ReciverUserId = e.ReciverUserId,
                ReciverName = string.IsNullOrEmpty(e.UserReciver!.FirstName + e.UserReciver.LastName)
                    ? e.UserReciver.Username
                    : (e.UserReciver.FirstName + " " + e.UserReciver.LastName).Trim(),
                Date = e.Date,
                Type = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsRead = e.IsRead,
                DateRead = e.DateRead,
                Answer = e.Answer,
                DateAnswer = e.DateAnswer,
                // تأیید/رد پاسخ و نام اقدام‌کننده — ریدر کارتابل بر همین‌ها نشان «تایید ✓/رد ✗» را
                // می‌سازد. پیش‌تر پر نمی‌شد و پاسخ‌ها همیشه بی‌نشان دیده می‌شدند.
                TypeTaeed = e.TypeTaeed,
                AmalgarId = e.AmalgarId,
                AmalgarTitle = e.Amalgar != null ? e.Amalgar.Title : null
            })
            .ToListAsync();

        var curErja = allErjas.LastOrDefault(e => e.ReciverUserId == userId);

        var dto = new IncomingLetterDetailDto
        {
            LetterId = letter.Id,
            SourceId = letter.Id,
            LetterNumber = letter.LetterNumber ?? "",
            Number = letter.Number,
            NumberSabt = letter.NumberSabt,
            Title = letter.Title,
            Description = letter.Description,
            Ferestande = letter.Ferestande,
            NumberLetterVarede = letter.NumberLetterVarede ?? "",
            Date = letter.Date,
            DateErsal = letter.DateErsal,
            TypeErsal = letter.TypeErsal ?? "",
            DeliveryName = letter.DeliveryName ?? "",
            CreatorUserId = letter.CreateUserId,
            CreatorName = creatorName,
            Mahramanegi = letter.Mahramanegi,
            Foriat = letter.Foriat,
            // نشان (ستاره) دو سطح دارد و قبلاً این‌جا اشتباه خوانده می‌شد:
            //   • فهرست کارتابل و فیلتر «نشان‌شده» روی Erja.IsNeshan کار می‌کنند (به‌ازای هر کاربر)
            //   • ستون IncomingLetter.IsNeshan فقط نشانِ ثبت‌کننده/نامه است
            // پیش‌تر جزئیات همیشه مقدار نامه را برمی‌گرداند؛ نتیجه: گیرنده در ریدر ستارهٔ
            // خاموش می‌دید (یا با هر بار باز شدن، ستاره‌اش «برمی‌گشت»). حالا اگر ارجاعی
            // برای کاربر جاری وجود دارد، همان ملاک است.
            IsNeshan = myErjas.Count > 0 ? myErjas.Any(e => e.IsNeshan) : letter.IsNeshan,
            IsMine = letter.CreateUserId == userId,
            CurrentErja = curErja,
            Erjas = allErjas,

            // ---------- باگ B11: این پنج فیلد تا پیش از این «هرگز» پر نمی‌شدند ----------
            // (فیلد ششم، ErjaHistory، فقط نام مستعار Erjas است و همان را برمی‌گرداند.)
            //
            // «ارجاعی که کاربر جاری باید به آن پاسخ دهد». دکمهٔ «ثبت پاسخ» در UI فقط وقتی
            // ActiveErjaId.HasValue باشد رندر می‌شود، پس تا الان هرگز دیده نمی‌شد.
            // قرارداد «هنوز پاسخ داده نشده» در کل کدبیس e.Answer == "" است
            // (ErjaService:179 هنگام ثبت پاسخ آن را پر می‌کند؛ AiDigestService:193 و
            // AiBriefingService:104 هم با همین شرط می‌شمارند).
            ActiveErjaId = myErjas
                .Where(e => string.IsNullOrEmpty(e.Answer))
                .OrderBy(e => e.Date)
                .LastOrDefault()?.ErjaId,

            // قابل ویرایش: ثبت‌کننده، مدیر، یا هر کاربرِ دبیرخانه (مجوز IncomingLetters.Dabirkhane).
            //
            // پیش‌تر شرطِ «هنوز هیچ گیرنده‌ای نامه را نخوانده باشد» هم داشت (هم‌قاعده با نامهٔ
            // داخلی) و نتیجه‌اش این بود که به‌محضِ رویت نامه توسط یک نفر، دکمهٔ «ویرایش» برای
            // همیشه از کارتابل دبیرخانه غیب می‌شد. حالا ویرایش در همهٔ مراحل ممکن است و
            // EditAsync خودش به کسانی که نامه را دیده‌اند اعلانِ «ویرایش نامه» می‌فرستد.
            CanEdit = letter.CreateUserId == userId || isAdmin || canDabirkhane,

            // «آیا نامه نزد کاربر جاری بایگانی شده» — پرچم روی ارجاعِ خود کاربر است،
            // نه روی نامه. (نامه می‌تواند برای یک کاربر بایگانی و برای دیگری فعال باشد.)
            IsBayegani = myErjas.Any(e => e.IsBayegani == true)
        };

        // پیوست‌های نامه — بدون این، بخش «پیوست‌های نامه» در پانل خواننده همیشه خالی بود.
        //
        // ⚠ کارایی: قبلاً ستون `Data` (خودِ بایت‌های فایل، از نوع varbinary(max)) هم
        // select می‌شد فقط برای اینکه `a.Data.Length` حساب شود. یعنی برای «باز کردن»
        // یک نامه، همهٔ فایل‌های پیوست از دیتابیس خوانده و در حافظهٔ سرور نگه داشته
        // می‌شدند (نامهٔ دارای چند پیوست چند ده مگابایت) و پاسخ هم کند می‌شد.
        // حالا فقط «طول» ستون در SQL محاسبه می‌شود (DATALENGTH در SQL Server و
        // length() در SQLite) و بایت‌ها هرگز به حافظه نمی‌آیند.
        var attRows = await _db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "IncomingLetters" && a.RefId == letterId)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id, a.FileName, a.ContentType, a.FilePath,
                DataLength = a.Data == null ? 0 : a.Data.Length,
                a.UploaderName, a.UploaderUserId, a.UploadedAt
            })
            .ToListAsync();

        dto.Attachments = attRows.Select(a => new LetterAttachmentDto
        {
            Id = a.Id,
            FileName = a.FileName,
            ContentType = a.ContentType,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.DataLength,
            UploaderName = a.UploaderName,
            UploaderUserId = a.UploaderUserId,
            UploadedAt = a.UploadedAt
        }).ToList();

        // نامه‌های مرتبط (عطف/پیرو) — هم‌راستا با InnerLetterService:492
        dto.RelatedLetters = await _db.RelatedLetters.AsNoTracking()
            .Where(r => r.LetterId == letterId && !r.IsDelete)
            .Select(r => new RelatedLetterDto
            {
                Id = r.Id,
                Related = r.Related,
                RelateLetterId = r.RelateLetterId,
                // نامهٔ مرتبط می‌تواند از هر سه دفتر باشد (وارده/داخلی/صادره)؛ پیش‌تر فقط
                // نامهٔ وارده خوانده می‌شد و برای بقیه، شماره و عنوان خالی برمی‌گشت.
                RelateLetterNumber = r.RelateLetter.IncomingLetter != null ? r.RelateLetter.IncomingLetter.LetterNumber
                                   : r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.LetterNumber
                                   : r.RelateLetter.OutgoingLetter != null ? r.RelateLetter.OutgoingLetter.LetterNumber
                                   : "",
                RelateLetterTitle = r.RelateLetter.IncomingLetter != null ? r.RelateLetter.IncomingLetter.Title
                                  : r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.Title
                                  : r.RelateLetter.OutgoingLetter != null ? r.RelateLetter.OutgoingLetter.Title
                                  : "",
                RelateSourceType = r.RelateLetter.SourceType
            })
            .ToListAsync();

        return dto;
    }

    public async Task<int> AddAsync(AddIncomingLetterDto dto, int userId)
    {
        var pc = new PersianCalendar();

        // مبنای سال، «تاریخ ورود/ثبت نامه» است نه امروز — وگرنه نامه‌ای که با تاریخ
        // سال قبل ثبت می‌شود شمارهٔ ترتیبی و جزء سالِ امسال را می‌گیرد.
        var entryDate = dto.DateErsal == default ? DateTime.Now : dto.DateErsal;
        var currentYear = pc.GetYear(entryDate);

        // تقریب شروع/پایان سال شمسی به میلادی — برای فیلتر در SQL.
        // مثل InnerLetterService.NextNumberAsync با try/catch تا تاریخ نامعتبر استارتاپ را نخواباند.
        DateTime startOfYear, endOfYear;
        try
        {
            startOfYear = new DateTime(currentYear, 1, 1, pc);
            endOfYear = new DateTime(currentYear + 1, 1, 1, pc);
        }
        catch
        {
            startOfYear = entryDate.AddDays(-400);
            endOfYear = entryDate.AddDays(400);
        }

        // ==================== باگ B4 (بند ۲): اتمیکیتی ====================
        // تراکنش عمداً «پیش از» خواندن‌های شماره‌گذاری شروع می‌شود، نه پیش از درج.
        // اگر بعد از آن‌ها شروع می‌شد، سطح Serializable هیچ اثری نداشت چون خواندن‌های
        // MAX() بیرون از تراکنش و بدون قفل انجام می‌شدند و دو درخواست همزمان باز هم
        // عدد یکسانی می‌خواندند.
        //
        // Serializable همان الگویی است که خودِ پروژه برای تخصیص شماره در جاهای
        // race‌پذیر به کار می‌برد (WorkOrdersController:180/314/798 و
        // DocEvolutionController:48) — پس رفتار و ریسک deadlock با بقیهٔ کدبیس یکی است.
        //
        // همچنین این متد «دو» بار SaveChanges می‌کند (اولی برای گرفتن source.Id، چون
        // IncomingLetter کلید مشترک با LetterSource دارد؛ دومی برای نامه و ارجاع‌ها).
        // یک باگ گزارش‌نشده: اگر دومی شکست می‌خورد، رکورد LetterSource «یتیم» در
        // دیتابیس می‌ماند. با تراکنش، هر دو یا با هم ثبت می‌شوند یا هیچ‌کدام.
        //
        // try/catch و تورفتگی دوبارهٔ بدنه لازم نیست: Dispose شدن تراکنشِ commit‌نشده
        // خودش rollback می‌کند، پس `await using` به‌تنهایی کافی است (همان HrDemoPurger:33).
        //
        // آوردن BuildLetterNumberAsync داخل تراکنش امن است: TotalNumberAsync فقط می‌خواند
        // (نه تراکنش جدا دارد نه SaveChanges) و AppDbContext هم Scoped است، پس همان
        // نمونهٔ مشترک در کل درخواست به کار می‌رود و تراکنش تودرتو ساخته نمی‌شود.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var maxNumber = await _db.IncomingLetters
            .Where(l => l.DateErsal >= startOfYear && l.DateErsal < endOfYear)
            .MaxAsync(l => (int?)l.Number) ?? 0;

        var nextNumber = maxNumber + 1;

        int numberSabt;
        if (dto.NumberSabt is > 0)
        {
            numberSabt = dto.NumberSabt.Value;

            // باگ B4 (بند ۳): مصرف رزرو دو شرط نداشت.
            //   !r.IsDelete  → رزروِ نرم‌حذف‌شده هم مصرف می‌شد
            //   r.UserId == userId → هر کاربری می‌توانست شمارهٔ رزروشدهٔ کاربر دیگر را بسوزاند
            var res = await _db.LetterNumberReservations.FirstOrDefaultAsync(r =>
                r.TypeForm == 3 && r.NumberSabt == numberSabt
                && !r.IsUsed && !r.IsDelete && r.UserId == userId);
            if (res != null) res.IsUsed = true;
        }
        else
        {
            // باگ B4 (بند ۱): این مسیر «فقط» جدول نامه‌ها را می‌دید و
            // LetterNumberReservations را نادیده می‌گرفت، در حالی که ReserveNumberAsync
            // هر دو را می‌بیند (Math.Max(lastUsedSabt, lastResSabt)). نتیجه: شماره‌ای که
            // کاربر A رزرو کرده ولی هنوز مصرف نشده بود، خودکار به نامهٔ کاربر B می‌رسید
            // و شمارهٔ اندیکاتور تکراری تولید می‌شد. حالا هر دو مسیر یک منطق دارند.
            var maxSabt = await _db.IncomingLetters.MaxAsync(l => (int?)l.NumberSabt) ?? 0;
            var maxReserved = await _db.LetterNumberReservations
                .Where(r => r.TypeForm == 3 && !r.IsDelete)
                .MaxAsync(r => (int?)r.NumberSabt) ?? 0;
            numberSabt = Math.Max(maxSabt, maxReserved) + 1;
        }

        // باگ B4 (بند ۲): تا اینجا همه‌چیز «MAX()+1» بدون تراکنش، بدون lock و بدون
        // محدودیت یکتایی بود. محافظت اصلی، ایندکس یکتای فیلترشده‌ای است که در
        // IncomingLetterSchemaV1 ساخته می‌شود؛ این بررسی صریح هم برای آن است که در
        // مسیر «شمارهٔ دستی» به‌جای خطای خامِ نقض یکتایی از دیتابیس، پیام فارسی
        // خوانا برگردانده شود (ApiExceptionFilter آن را به 400 تبدیل می‌کند).
        if (await _db.IncomingLetters.AnyAsync(l => l.NumberSabt == numberSabt && !l.IsDelete))
            throw new Exception($"شمارهٔ اندیکاتور {numberSabt} پیش‌تر برای نامهٔ واردهٔ دیگری ثبت شده است. لطفاً شمارهٔ دیگری وارد کنید یا فیلد شماره را خالی بگذارید تا خودکار تخصیص یابد.");

        // شمارهٔ اندیکاتور از ساختار پیکربندی‌شده در «تنظیمات ← ساختار شماره نامه»
        var letterNumber = await BuildLetterNumberAsync(numberSabt, entryDate);

        var source = new LetterSource
        {
            SourceType = 3, // 3 = نامه وارده
            IsDelete = false
        };
        _db.LetterSources.Add(source);
        await _db.SaveChangesAsync();

        var letter = new IncomingLetter
        {
            Id = source.Id,
            LetterNumber = letterNumber,
            Number = nextNumber,
            NumberSabt = numberSabt,
            Title = dto.Title.Trim(),
            Ferestande = dto.Ferestande.Trim(),
            NumberLetterVarede = dto.NumberLetterVarede?.Trim(),
            Date = dto.Date,
            DateErsal = dto.DateErsal,
            TypeErsal = dto.TypeErsal?.Trim(),
            DeliveryName = dto.DeliveryName?.Trim(),
            Description = dto.Description,
            Mahramanegi = dto.Mahramanegi,
            Foriat = dto.Foriat,
            CreateUserId = userId,
            Creator = 1,
            CreatorId = userId,
            IsNeshan = false,
            IsBayegani = false,
            IsDelete = false
        };

        _db.IncomingLetters.Add(letter);

        // اضافه کردن خود ثبت‌کننده یا گیرندگان اولیه در جدول Erja
        // گیرندگان اولیه می‌توانند نال برسند (کلاینت قدیمی یا JSON بدون این فیلد)؛
        // پیش‌تر Distinct() روی نال استثنا می‌داد و ثبت نامه با 400 مبهم شکست می‌خورد.
        var recivers = (dto.InitialReciverUserIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
        if (!recivers.Contains(userId)) recivers.Insert(0, userId);

        foreach (var rId in recivers)
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = source.Id,
                SenderUserId = userId,
                ReciverUserId = rId,
                Date = DateTime.Now,
                Type = "گیرنده",
                MatnErja = string.IsNullOrWhiteSpace(dto.InitialMatnErja) ? "ثبت نامه وارده" : dto.InitialMatnErja.Trim(),
                MohlatPasokh = dto.InitialMohlatPasokh,
                IsRead = rId == userId,
                DateRead = rId == userId ? DateTime.Now : null
            });
        }

        // ---------- عطف / پیرو (نامه‌های مرتبط) ----------
        // AddIncomingLetterDto.RelatedSourceIds از قبل وجود داشت ولی هیچ‌جا ذخیره نمی‌شد؛
        // نتیجه این بود که بخش «نامه‌های مرتبط» در ریدر کارتابل همیشه خالی می‌ماند.
        // حالا هم فهرست نوع‌دار (RelatedLetters) و هم فیلد قدیمی (RelatedSourceIds ⇒ پیرو)
        // پشتیبانی می‌شود.
        var relSeeds = new List<(int Id, int Type)>();
        foreach (var rel in dto.RelatedLetters ?? new List<RelatedLetterDto>())
            if (rel.RelateLetterId > 0) relSeeds.Add((rel.RelateLetterId, rel.Related == 2 ? 2 : 1));
        foreach (var relId in dto.RelatedSourceIds ?? new List<int>())
            if (relId > 0) relSeeds.Add((relId, 1));

        foreach (var seed in relSeeds
                     .Where(x => x.Id != source.Id)     // نامه به خودش عطف/پیرو نمی‌شود
                     .GroupBy(x => x.Id)                // یک رابطه به ازای هر نامه
                     .Select(g => g.First()))
        {
            // نامهٔ مقصد باید واقعاً وجود داشته باشد و نرم‌حذف نشده باشد.
            var exists = await _db.LetterSources.AnyAsync(x => x.Id == seed.Id && !x.IsDelete);
            if (!exists) continue;

            _db.RelatedLetters.Add(new RelatedLetter
            {
                Related = seed.Type,
                LetterId = source.Id,
                RelateLetterId = seed.Id,
                UserId = userId,
                IsDelete = false
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _notify.BroadcastChangedAsync("incoming-letters");
        return letter.Id;
    }

    /// <summary>
    /// نهایی‌سازی نامهٔ وارده و ارسال اعلان به «گیرندگان اولیه».
    ///
    /// ترتیب صحیح در مسیر «تبدیل ایمیل به نامه وارده» این است:
    ///   ثبت نامه (AddAsync، با Commit تراکنش)  →  ایجاد ارجاع‌ها (همان AddAsync)
    ///   →  آپلود کامل «همهٔ» پیوست‌ها  →  Finalize  →  اعلان گیرندگان
    /// پس وقتی این متد صدا زده می‌شود، نامه و ارجاع‌هایش از پیش Commit شده‌اند و
    /// اعلان هیچ‌گاه برای نامهٔ ناتمام (بدون پیوست) ارسال نمی‌شود.
    ///
    /// قواعد:
    ///   • فقط ثبت‌کننده یا مدیر؛
    ///   • گیرندگان اولیه = ارجاع‌های Type=«گیرنده» که هنگام ثبت ساخته شده‌اند؛
    ///   • خودِ ثبت‌کننده اعلان نمی‌گیرد؛
    ///   • جلوگیری از اعلان تکراری: به‌ازای هر گیرنده، اگر اعلان «نامه وارده» با
    ///     همین لینک از قبل وجود دارد، دوباره ساخته نمی‌شود (Finalize idempotent است)؛
    ///   • لینک اعلان «incoming-letters?open={id}» است چون در کلاینت Route‌ای به
    ///     شکل /incoming-letters/view/{id} وجود ندارد و کارتابل با ?open= همان نامه
    ///     را در Reader باز می‌کند.
    ///
    /// خروجی: تعداد گیرندگانِی که در «این فراخوان» اعلان گرفتند.
    /// </summary>
    public async Task<int> FinalizeAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.IncomingLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new InvalidOperationException("نامه وارده یافت نشد.");

        if (letter.CreateUserId != userId && !isAdmin)
            throw new UnauthorizedAccessException("فقط ثبت‌کننده یا مدیر می‌تواند نامه وارده را نهایی‌سازی کند.");

        // استخراج گیرندگان اولیه (به‌جز ثبت‌کننده)
        var targetIds = await _db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letterId && !e.IsDelete
                        && e.Type == "گیرنده" && e.SenderUserId == letter.CreateUserId
                        && e.ReciverUserId != letter.CreateUserId)
            .Select(e => e.ReciverUserId)
            .Distinct()
            .ToListAsync();
        if (targetIds.Count == 0) return 0;

        var link = $"incoming-letters?open={letterId}";

        // جلوگیری از اعلان تکراری — فراخوان دوبارهٔ Finalize (یا هر مسیر موازی)
        // نباید برای کاربری که قبلاً اعلان همین نامه را گرفته، اعلان تازه بسازد.
        var alreadyNotified = await _db.AppNotifications.AsNoTracking()
            .Where(n => targetIds.Contains(n.UserId)
                        && n.FormName == "نامه وارده" && n.Link == link)
            .Select(n => n.UserId)
            .Distinct()
            .ToListAsync();
        var pendingIds = targetIds.Except(alreadyNotified).ToList();
        if (pendingIds.Count == 0) return 0;

        var creator = await _db.Users.AsNoTracking()
            .Where(u => u.Id == letter.CreateUserId)
            .Select(u => new { u.FirstName, u.LastName, u.Username })
            .FirstOrDefaultAsync();
        var fromName = creator == null
            ? "سیستم"
            : (string.IsNullOrWhiteSpace((creator.FirstName ?? "") + (creator.LastName ?? ""))
                ? creator.Username
                : $"{creator.FirstName} {creator.LastName}".Trim());

        await _notify.SendManyAsync(pendingIds,
            $"نامه وارده جدید: {letter.Title}",
            $"شماره {letter.LetterNumber}",
            fromName, "نامه وارده", link);
        await _notify.BroadcastChangedAsync("incoming-letters");
        return pendingIds.Count;
    }

    public async Task<int> EditAsync(int letterId, EditIncomingLetterDto dto, int userId, bool isAdmin, bool canDabirkhane = false)
    {
        var letter = await _db.IncomingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) throw new InvalidOperationException("نامه یافت نشد.");
        if (letter.CreateUserId != userId && !isAdmin && !canDabirkhane)
            throw new UnauthorizedAccessException("فقط ثبت‌کننده، دبیرخانه یا مدیر می‌تواند نامه را ویرایش کند.");

        // ---------- چه کسانی باید از ویرایش باخبر شوند؟ ----------
        // فقط کسانی که نامه در گردششان هست «و آن را دیده‌اند» (Erja.IsRead). کسی که
        // هنوز نامه را باز نکرده، اعلانِ «نامهٔ جدید» خودش را دارد و اعلان تکراری
        // نمی‌گیرد. خودِ ویرایش‌کننده هم اعلان نمی‌گیرد.
        // این فهرست «قبل از» تغییر فیلدها خوانده می‌شود تا مستقل از تغییرات باشد.
        var seenUserIds = await _db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letterId && !e.IsDelete && e.IsRead)
            .Select(e => e.ReciverUserId)
            .Distinct()
            .ToListAsync();

        // ثبت‌کنندهٔ نامه هم نامه را دیده است (حتی اگر ردیف ارجاعی با IsRead نداشته باشد)؛
        // اگر دبیرخانه یا مدیر نامه را ویرایش کند، ثبت‌کننده هم باید باخبر شود.
        // همین قاعده در LetterAttachmentNotifier برای «پیوست جدید» استفاده شده است.
        if (letter.CreateUserId > 0) seenUserIds.Add(letter.CreateUserId);

        // خودِ ویرایش‌کننده اعلان نمی‌گیرد.
        seenUserIds = seenUserIds.Where(id => id > 0 && id != userId).Distinct().ToList();

        var titleBefore = letter.Title;

        letter.Title = dto.Title.Trim();
        letter.Ferestande = dto.Ferestande.Trim();
        letter.NumberLetterVarede = dto.NumberLetterVarede?.Trim();
        letter.Date = dto.Date;
        letter.DateErsal = dto.DateErsal;
        letter.TypeErsal = dto.TypeErsal?.Trim();
        letter.DeliveryName = dto.DeliveryName?.Trim();
        letter.Description = dto.Description;
        letter.Mahramanegi = dto.Mahramanegi;
        letter.Foriat = dto.Foriat;

        // ---------- همگام‌سازی نامه‌های مرتبط (عطف/پیرو) ----------
        // همان الگوی InnerLetterService.EditAsync: ردیف‌های قبلی نرم‌حذف و ردیف‌های
        // باقی‌مانده «زنده» می‌شوند تا رکورد تکراری ساخته نشود. null یعنی دست نزن؛
        // فهرست خالی یعنی همهٔ رابطه‌ها حذف شوند.
        var relatedChanged = false;
        if (dto.RelatedLetters is not null)
        {
            var oldRels = await _db.RelatedLetters
                .Where(r => r.LetterId == letterId && !r.IsDelete)
                .ToListAsync();

            var wanted = dto.RelatedLetters
                .Where(x => x.RelateLetterId > 0 && x.RelateLetterId != letterId)
                .GroupBy(x => x.RelateLetterId)
                .Select(g => (Id: g.Key, Type: g.First().Related == 2 ? 2 : 1))
                .ToList();

            // مقایسه «قبل از» تغییر ردیف‌ها انجام می‌شود؛ وگرنه نوعِ عطف↔پیرو هم‌سان
            // می‌شد و تغییر رابطه در متن اعلان دیده نمی‌شد.
            var oldSnapshot = oldRels.Select(o => (Id: o.RelateLetterId, Type: o.Related)).ToList();

            foreach (var r in oldRels) r.IsDelete = true;

            foreach (var w in wanted)
            {
                var keep = oldRels.FirstOrDefault(o => o.RelateLetterId == w.Id);
                if (keep != null)
                {
                    keep.IsDelete = false;
                    keep.Related = w.Type;
                    continue;
                }
                var exists = await _db.LetterSources.AnyAsync(x => x.Id == w.Id && !x.IsDelete);
                if (!exists) continue;
                _db.RelatedLetters.Add(new RelatedLetter
                {
                    Related = w.Type,
                    LetterId = letterId,
                    RelateLetterId = w.Id,
                    UserId = userId,
                    IsDelete = false
                });
            }

            relatedChanged = oldSnapshot.Count != wanted.Count
                             || oldSnapshot.Any(o => !wanted.Any(w => w.Id == o.Id && w.Type == o.Type));
        }

        await _db.SaveChangesAsync();

        // ---------- اعلان «به‌روزرسانی نامه» به افرادِ در گردش که نامه را دیده‌اند ----------
        // عنوانِ اعلان صریحاً می‌گوید نامه ویرایش شده تا کاربر بداند باید نسخهٔ تازه را
        // ببیند؛ متنِ اعلان می‌گوید چه چیزی عوض شده و چه کسی ویرایش کرده. لینک همان
        // قراردادِ اعلانِ نامهٔ جدید است («incoming-letters?open={id}») تا کلیک روی اعلان
        // همان نامه را در کارتابل باز کند.
        if (seenUserIds.Count > 0)
        {
            var editor = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.FirstName, u.LastName, u.Username })
                .FirstOrDefaultAsync();
            var byName = editor == null
                ? "دبیرخانه"
                : (string.IsNullOrWhiteSpace((editor.FirstName ?? "") + (editor.LastName ?? ""))
                    ? editor.Username
                    : $"{editor.FirstName} {editor.LastName}".Trim());

            var titleChanged = !string.Equals(titleBefore ?? "", letter.Title ?? "", StringComparison.Ordinal);
            var body = titleChanged
                ? $"شماره {letter.LetterNumber} — عنوان به «{letter.Title}» تغییر کرد؛ ویرایش توسط {byName}"
                : $"شماره {letter.LetterNumber} — محتوای نامه ویرایش شد؛ ویرایش توسط {byName}";
            if (relatedChanged) body += " · نامه‌های مرتبط (عطف/پیرو) هم به‌روزرسانی شد";

            await _notify.SendManyAsync(seenUserIds,
                $"نامه وارده ویرایش شد: {letter.Title}",
                body,
                byName, "نامه وارده", $"incoming-letters?open={letterId}");
        }

        await _notify.BroadcastChangedAsync("incoming-letters");
        return seenUserIds.Count;
    }

    public async Task DeleteAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.IncomingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return;
        if (letter.CreateUserId != userId && !isAdmin)
            throw new UnauthorizedAccessException("دستور حذف صادر نشد.");

        letter.IsDelete = true;
        var source = await _db.LetterSources.FirstOrDefaultAsync(s => s.Id == letterId);
        if (source != null) source.IsDelete = true;

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("incoming-letters");
    }

    public async Task<bool> ToggleLetterNeshanAsync(int letterId, int userId)
    {
        var letter = await _db.IncomingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return false;

        // فقط کسانی که به نامه دسترسی دارند (ثبت‌کننده یا حاضر در گردش) می‌توانند
        // نشانِ نامه را عوض کنند؛ بررسی دقیق‌ترِ مجوز در کنترلر انجام می‌شود.
        var inFlow = letter.CreateUserId == userId ||
                     await _db.Erjas.AnyAsync(e => e.SourceId == letterId && e.ReciverUserId == userId && !e.IsDelete);
        if (!inFlow) throw new UnauthorizedAccessException("شما در گردش این نامه نیستید.");

        letter.IsNeshan = !letter.IsNeshan;
        await _db.SaveChangesAsync();
        // پیش‌تر هیچ broadcast‌ای نبود و فهرست «نشان‌شده» تا رفرش دستی کهنه می‌ماند.
        await _notify.BroadcastChangedAsync("incoming-letters");
        return letter.IsNeshan;
    }

    public async Task<PagedResult<IncomingLetterPickDto>> PickListAsync(int userId, string? search, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.IncomingLetters.AsNoTracking()
            .Where(l => !l.IsDelete && (l.CreateUserId == userId || l.Source.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.Ferestande.Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q.OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new IncomingLetterPickDto
            {
                SourceId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                NumberSabt = l.NumberSabt,
                Title = l.Title,
                Ferestande = l.Ferestande,
                Date = l.Date
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterPickDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<LetterNumberReservationDto>> GetReservationsAsync(int userId, int typeForm = 3, Paging.Request? pagination = null)
    {
        try
        {
            await IncomingLetterSchemaV1.EnsureOnceAsync(_db);

            var list = await _db.LetterNumberReservations.AsNoTracking()
                .Where(r => r.TypeForm == typeForm && !r.IsUsed && !r.IsDelete)
                .OrderByDescending(r => r.NumberSabt).ThenBy(x => x.Id)
                .ToPageListAsync(pagination, defaultCap: 50);

            var userIds = list.Select(r => r.UserId).Distinct().ToList();
            var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);

            return list.Select(r => new LetterNumberReservationDto
            {
                Id = r.Id,
                TypeForm = r.TypeForm,
                NumberSabt = r.NumberSabt,
                DateRezerv = r.DateRezerv,
                UserId = r.UserId,
                UserName = users.TryGetValue(r.UserId, out var u) ? (string.IsNullOrEmpty(u.FirstName + u.LastName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim()) : "سیستم",
                IsUsed = r.IsUsed
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IncomingLetterService] GetReservations error: {ex.Message}");
            return new List<LetterNumberReservationDto>();
        }
    }

    public async Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int userId, int typeForm = 3, int count = 1)
    {
        count = Math.Clamp(count, 1, 20);
        await IncomingLetterSchemaV1.EnsureOnceAsync(_db);

        int lastUsedSabt = 0;
        int lastResSabt = 0;

        try { lastUsedSabt = await _db.IncomingLetters.MaxAsync(l => (int?)l.NumberSabt) ?? 0; } catch { }
        try { lastResSabt = await _db.LetterNumberReservations.Where(r => r.TypeForm == typeForm).MaxAsync(r => (int?)r.NumberSabt) ?? 0; } catch { }

        var startSabt = Math.Max(lastUsedSabt, lastResSabt) + 1;
        var created = new List<LetterNumberReservation>();

        for (int i = 0; i < count; i++)
        {
            var res = new LetterNumberReservation
            {
                TypeForm = typeForm,
                NumberSabt = startSabt + i,
                DateRezerv = DateTime.Now,
                UserId = userId,
                IsUsed = false,
                IsDelete = false
            };
            _db.LetterNumberReservations.Add(res);
            created.Add(res);
        }

        await _db.SaveChangesAsync();

        // باگ B4 (بند ۴): لیست `created` ساخته و پر می‌شد ولی «هرگز» استفاده نمی‌شد —
        // کد مرده. متد در عوض GetReservationsAsync را صدا می‌زد که کل رزروهای
        // مصرف‌نشدهٔ «همهٔ کاربران» را (با سقف ۵۰) برمی‌گرداند، پس UI پس از رزرو
        // لیست غلطی را به‌عنوان نتیجه نشان می‌داد. حالا همان شماره‌هایی برمی‌گردند
        // که در این فراخوان رزرو شده‌اند.
        //
        // UserName یک بار گرفته می‌شود چون همهٔ رزروهای این فراخوان متعلق به یک کاربرند.
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var myName = me == null
            ? "سیستم"
            : (string.IsNullOrEmpty(me.FirstName + me.LastName) ? me.Username : $"{me.FirstName} {me.LastName}".Trim());

        return created.Select(r => new LetterNumberReservationDto
        {
            Id = r.Id,
            TypeForm = r.TypeForm,
            NumberSabt = r.NumberSabt,
            DateRezerv = r.DateRezerv,
            UserId = r.UserId,
            UserName = myName,
            IsUsed = r.IsUsed
        }).ToList();
    }
}
