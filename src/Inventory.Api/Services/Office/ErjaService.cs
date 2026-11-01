using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ارجاع (گردش نامه) — منطق بر اساس ErjaService کارفرما:
//  • ارجاع نامه به کاربر(ان) دیگر با دستور/پاراف + عملگر + مهلت پاسخ
//  • پاسخ به ارجاع (+ تایید/رد برای عملگرهای امضادار)
//  • درخت گردش کامل نامه (GetGardeshErjaTreeView)
//  • نشان‌کردن (ستاره) و ثبت زمان خواندن
// ============================================================

public interface IErjaService
{
    /// <summary>
    /// ثبت ارجاع/هامش. <paramref name="allowSelfReferral"/> فقط برای مسئول دبیرخانه
    /// (مجوز Dabirkhane) true است تا بتواند نامه را به «خودش» هم ارجاع دهد.
    /// </summary>
    Task AddErjaAsync(AddErjaDto dto, int senderUserId, string senderName, bool allowSelfReferral = false);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto, int userId, string userName);
    Task<List<ErjaTreeNodeDto>> GetGardeshTreeAsync(int sourceId, int userId, bool isAdmin, Paging.Request? pagination = null);
    Task MarkReadAsync(int erjaId, int userId);
    Task<bool> ToggleNeshanAsync(int erjaId, int userId);
    Task<bool> ToggleBayeganiAsync(int erjaId, int userId);
    Task<int> BatchBayeganiAsync(List<int> erjaIds, int userId, string? description = null);
    Task<List<AmalgarDto>> GetAmalgarsAsync(Paging.Request? pagination = null);

    /// <summary>افزودن عملگر ارجاع تازه (دکمهٔ «+» کنار انتخاب عملگر).</summary>
    Task<AmalgarDto> CreateAmalgarAsync(string title, string? taeedEmza = null);
}

public class ErjaService : IErjaService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly ILetterGroupService _groups;

    public ErjaService(AppDbContext db, INotifyService notify, ILetterGroupService groups)
    {
        _db = db;
        _notify = notify;
        _groups = groups;
    }

    public async Task AddErjaAsync(AddErjaDto dto, int senderUserId, string senderName, bool allowSelfReferral = false)
    {
        // ---------- باز کردن گروه‌ها (منطق Reciver_GroupsErja/Hamesh کارفرما) ----------
        var allGroupIds = dto.GroupsGirandegan.Concat(dto.GroupsHamesh).Distinct().ToList();
        if (allGroupIds.Count > 0 && !await _groups.CheckGroupIdsAsync(allGroupIds))
            throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");

        var girandegan = dto.ReciversGirandegan
            .Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirandegan)).Distinct().ToList();
        var hameshIds = dto.ReciversHamesh
            .Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();

        // خود فرستنده اگر عضو گروه بود، حذف می‌شود — به‌جز مسئول دبیرخانه که باید
        // بتواند نامه را به خودش هم ارجاع دهد (درخواست: «مسئول دبیرخانه علاوه بر
        // دیگران بتواند به خودش هم ارجاع دهد»).
        if (!allowSelfReferral)
        {
            girandegan.Remove(senderUserId);
            hameshIds.Remove(senderUserId);
        }

        // فرم واحد ارجاع/هامش: بسته به انتخاب کاربر ممکن است فقط گیرنده ارجاع یا فقط گیرنده هامش داشته باشیم
        if (girandegan.Count == 0 && hameshIds.Count == 0)
            throw new Exception("حداقل یک گیرنده باید انتخاب شود.");

        // پشتیبانی از هر سه نوع نامه: داخلی، صادره و وارده
        var source = await _db.LetterSources.AsNoTracking()
            .Include(s => s.InnerLetter)
            .Include(s => s.OutgoingLetter)
            .Include(s => s.IncomingLetter)
            .FirstOrDefaultAsync(s => s.Id == dto.LetterId && !s.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        bool isInner = source.InnerLetter != null && !source.InnerLetter.IsDelete;
        bool isOutgoing = source.OutgoingLetter != null && !source.OutgoingLetter.IsDelete;
        bool isIncoming = source.IncomingLetter != null && !source.IncomingLetter.IsDelete;
        if (!isInner && !isOutgoing && !isIncoming)
            throw new Exception("نامه پیدا نشد یا حذف شده است.");

        int creatorUserId = isInner ? source.InnerLetter!.CreatorUserId
                          : isOutgoing ? source.OutgoingLetter!.CreatorUserId
                          : source.IncomingLetter!.CreateUserId;

        // فرستنده باید خودش گیرنده نامه یا فرستنده اصلی باشد
        bool allowed = creatorUserId == senderUserId ||
                       await _db.Erjas.AnyAsync(e => e.SourceId == dto.LetterId && e.ReciverUserId == senderUserId && !e.IsDelete);
        if (!allowed)
            throw new Exception("شما در گردش این نامه نیستید و امکان ارجاع ندارید.");

        // اعتبارسنجی ParentErjaId — باگ قبلی: بدون چک بود و امکان ارجاع به نامه دیگر وجود داشت
        if (dto.ParentErjaId is > 0)
        {
            var parentExists = await _db.Erjas.AnyAsync(e => e.ErjaId == dto.ParentErjaId && e.SourceId == dto.LetterId && !e.IsDelete);
            if (!parentExists)
                throw new Exception("ارجاع والد نامعتبر است یا متعلق به این نامه نیست.");
        }

        var amalgarOk = await _db.Amalgars.AnyAsync(a => a.AmalgarId == dto.AmalgarId && !a.IsDelete);
        if (!amalgarOk) throw new Exception("عملگر ارجاع نامعتبر است.");

        var allIds = girandegan.Concat(hameshIds).Distinct().ToList();
        if (allIds.Count == 0)
            throw new Exception("حداقل یک گیرنده باید انتخاب شود.");
        // خودارجاعی فقط برای مسئول دبیرخانه مجاز است (allowSelfReferral).
        if (!allowSelfReferral && allIds.Contains(senderUserId))
            throw new Exception("ارجاع نامه به خودتان امکان‌پذیر نیست.");

        var validUsers = await _db.Users
            .Where(u => allIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id).ToListAsync();
        if (allIds.Except(validUsers).Any())
            throw new Exception("برخی از گیرندگان معتبر یا فعال نیستند.");

        // گیرنده تکراری باید قبل از ایجاد گردش به کاربر اعلام شود؛ ثبت تکراری
        // فقط با AllowDuplicateRecipients که از تأیید صریح فرم می‌آید مجاز است.
        // در خودارجاعی، ردیف «گیرندهٔ» خودکارِ ثبت نامه (SenderUserId == ReciverUserId)
        // باعث می‌شد نامه «تکراری» تشخیص داده شود و مسئول دبیرخانه هرگز نتواند به خودش
        // ارجاع دهد؛ پس در این حالت خودِ فرستنده از بررسی تکراری مستثنی می‌شود.
        var duplicateCheckIds = allowSelfReferral
            ? allIds.Where(id => id != senderUserId).ToList()
            : allIds;
        var duplicateIds = duplicateCheckIds.Count == 0
            ? new List<int>()
            : await _db.Erjas
                .Where(e => e.SourceId == dto.LetterId && !e.IsDelete && duplicateCheckIds.Contains(e.ReciverUserId))
                .Select(e => e.ReciverUserId).Distinct().ToListAsync();
        if (duplicateIds.Count > 0 && !dto.AllowDuplicateRecipients)
        {
            var duplicateNames = await _db.Users.Where(u => duplicateIds.Contains(u.Id))
                .Select(u => (u.FirstName + " " + u.LastName).Trim()).ToListAsync();
            throw new InvalidOperationException($"این گیرندگان قبلاً در گردش نامه هستند: {string.Join("، ", duplicateNames)}. برای ارسال مجدد باید گزینه ارسال تکراری را تأیید کنید.");
        }

        // والدِ خودکار برای نمودار گردش ----------
        // کلاینت هیچ‌وقت ParentErjaId نمی‌فرستد؛ نتیجه این بود که هر ارجاع «ریشه» می‌شد و در
        // نمودار درختواره/فلوچارتی، گیرندهٔ دوم (C) کنار گیرندهٔ اول (B) می‌نشست، نه زیر او.
        // قاعده: ارجاع جدید فرزندِ ردیفی است که با آن، نامه به همین فرستنده رسیده است
        // (آخرین ردیفی که گیرنده‌اش خودِ فرستنده است). اگر فرستنده ثبت‌کنندهٔ نامه باشد و
        // هیچ ردیف دریافتی نداشته باشد، ارجاع او ریشه می‌ماند (زیر خودِ نامه).
        var parentErjaId = dto.ParentErjaId is > 0 ? dto.ParentErjaId : null;
        if (parentErjaId == null)
        {
            parentErjaId = await _db.Erjas.AsNoTracking()
                .Where(e => e.SourceId == dto.LetterId && !e.IsDelete
                            && e.ReciverUserId == senderUserId
                            && e.SenderUserId != senderUserId)
                .OrderByDescending(e => e.ErjaId)
                .Select(e => (int?)e.ErjaId)
                .FirstOrDefaultAsync();
        }

        var now = DateTime.Now;
        var seen = new HashSet<int>();
        foreach (var uid in girandegan.Where(seen.Add))
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = dto.LetterId,
                SenderUserId = senderUserId,
                ReciverUserId = uid,
                Date = now,
                Type = "ارجاع",
                MatnErja = dto.TextErja?.Trim() ?? "",
                AmalgarId = dto.AmalgarId,
                MohlatPasokh = dto.DeadlineAnswer,
                ParentErjaId = parentErjaId,
                Answer = "",
                ShowMassage = true
            });
        }
        foreach (var uid in hameshIds.Where(seen.Add))
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = dto.LetterId,
                SenderUserId = senderUserId,
                ReciverUserId = uid,
                Date = now,
                Type = "هامش",
                MatnErja = dto.TextErja?.Trim() ?? "",
                AmalgarId = dto.AmalgarId,
                ParentErjaId = parentErjaId,
                Answer = "",
                ShowMassage = true
            });
        }
        await _db.SaveChangesAsync();

        // عنوان و شماره نامه از منبع بارگذاری‌شده استخراج می‌شود
        var letterTitle = isInner ? source.InnerLetter!.Title
                        : isOutgoing ? source.OutgoingLetter!.Title
                        : source.IncomingLetter!.Title;
        var letterNumber = isInner ? source.InnerLetter!.LetterNumber
                         : isOutgoing ? source.OutgoingLetter!.LetterNumber
                         : source.IncomingLetter!.LetterNumber;

        // هر ماژول مسیر نمایش و کانال بروزرسانی خودش را دارد
        var (routeBase, moduleTitle, channel) = isInner
            ? ("letters", "ارجاع نامه داخلی", "letters")
            : isOutgoing
                ? ("outgoing-letters", "ارجاع نامه صادره", "outgoing-letters")
                : ("incoming-letters", "ارجاع نامه وارده", "incoming-letters");

        // نامهٔ وارده در کلاینت Route به شکل /incoming-letters/view/{id} ندارد؛
        // کارتابل وارده پارامتر ?open={id} را می‌خواند و همان نامه را در Reader
        // باز می‌کند (IncomingLetterCartable:OnInitializedAsync). نامهٔ داخلی و
        // صادره Route مخصوص view خودشان را دارند و دست نمی‌خورند.
        var link = isIncoming
            ? $"incoming-letters?open={dto.LetterId}"
            : $"{routeBase}/view/{dto.LetterId}";
        // در خودارجاعی، اعلان «ارجاع نامه» به خودِ فرستنده فرستاده نمی‌شود (نامه از قبل
        // در کارتابل خودش است) ولی ردیف ارجاع ساخته می‌شود.
        seen.Remove(senderUserId);
        await _notify.SendManyAsync(seen,
            $"ارجاع نامه: {letterTitle}",
            string.IsNullOrWhiteSpace(dto.TextErja) ? $"شماره {letterNumber}" : dto.TextErja,
            senderName, moduleTitle, link);
        await _notify.BroadcastChangedAsync(channel);
    }

    public async Task AnswerAsync(int erjaId, AnswerErjaDto dto, int userId, string userName)
    {
        var erja = await _db.Erjas
            .Include(e => e.Source).ThenInclude(s => s.InnerLetter)
            .Include(e => e.Source).ThenInclude(s => s.OutgoingLetter)
            .Include(e => e.Source).ThenInclude(s => s.IncomingLetter)
            .FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");

        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده‌ی ارجاع می‌تواند پاسخ ثبت کند.");

        // پاسخ خالی نباید ثبت شود: قرارداد «پاسخ داده نشده» در کل کدبیس Answer == ""
        // است (ActiveErjaId و گردش و شمارنده‌ها با همین شرط کار می‌کنند). پیش‌تر متن
        // خالی هم ذخیره می‌شد، تاریخ پاسخ می‌خورد و اعلانِ خالی ارسال می‌شد، در حالی
        // که نامه عملاً «بدون پاسخ» می‌ماند — یعنی کاربر فکر می‌کرد پاسخ ثبت شده است.
        var answerText = (dto.Answer ?? "").Trim();
        if (answerText.Length == 0)
            throw new Exception("متن پاسخ نمی‌تواند خالی باشد.");
        if (answerText.Length > 4000)
            throw new Exception("متن پاسخ حداکثر ۴۰۰۰ کاراکتر است.");

        var now = DateTime.Now;
        erja.Answer = answerText;
        erja.ShowForAll = dto.ShowForAll;
        erja.TypeTaeed = dto.TypeTaeed;
        erja.DateAnswer = now;
        if (dto.TypeTaeed != 0) erja.DateEmza = now;
        erja.IsReadAnswer = false;
        erja.ShowMassageAnswer = true;
        if (!erja.IsRead) { erja.IsRead = true; erja.DateRead = now; }
        await _db.SaveChangesAsync();

        var title = erja.Source.InnerLetter?.Title
                 ?? erja.Source.OutgoingLetter?.Title
                 ?? erja.Source.IncomingLetter?.Title
                 ?? "";
        var isOutgoing = erja.Source.OutgoingLetter != null;
        var isIncoming = erja.Source.IncomingLetter != null;
        var taeedText = dto.TypeTaeed switch { 1 => " (تایید شد ✅)", 2 => " (رد شد ❌)", _ => "" };
        var (answerRoute, answerModule) = isOutgoing
            ? ("outgoing-letters", "نامه صادره")
            : isIncoming
                ? ("incoming-letters", "نامه وارده")
                : ("letters", "نامه داخلی");
        // برای وارده Route به شکل /incoming-letters/view/{id} وجود ندارد — ?open=
        var link = isIncoming
            ? $"incoming-letters?open={erja.SourceId}"
            : $"{answerRoute}/view/{erja.SourceId}";
        await _notify.SendAsync(erja.SenderUserId,
            $"پاسخ به ارجاع {answerModule}: {title}{taeedText}",
            erja.Answer,
            userName, "پاسخ ارجاع", link);
        await _notify.BroadcastChangedAsync(answerRoute);
    }

    public async Task<List<ErjaTreeNodeDto>> GetGardeshTreeAsync(int sourceId, int userId, bool isAdmin, Paging.Request? pagination = null)
    {
        var source = await _db.LetterSources.AsNoTracking()
            .Include(s => s.InnerLetter)
            .Include(s => s.OutgoingLetter)
            .Include(s => s.IncomingLetter)
            .FirstOrDefaultAsync(s => s.Id == sourceId && !s.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        bool isInner = source.InnerLetter != null && !source.InnerLetter.IsDelete;
        bool isOutgoing = source.OutgoingLetter != null && !source.OutgoingLetter.IsDelete;
        bool isIncoming = source.IncomingLetter != null && !source.IncomingLetter.IsDelete;
        if (!isInner && !isOutgoing && !isIncoming)
            throw new Exception("نامه پیدا نشد یا حذف شده است.");

        int creatorUserId = isInner ? source.InnerLetter!.CreatorUserId
                          : isOutgoing ? source.OutgoingLetter!.CreatorUserId
                          : source.IncomingLetter!.CreateUserId;

        bool inFlow = creatorUserId == userId ||
                      await _db.Erjas.AnyAsync(e => e.SourceId == sourceId && e.ReciverUserId == userId && !e.IsDelete);
        if (!inFlow && !isAdmin)
            throw new Exception("شما در گردش این نامه نیستید.");

        var scope = _db.Erjas.AsNoTracking()
            .Include(e => e.UserSender).Include(e => e.UserReciver).Include(e => e.Amalgar)
            .Where(e => e.SourceId == sourceId && !e.IsDelete);
        var erjas = await scope.OrderBy(e => e.ErjaId).ReadTreePageAsync(
            scope.Where(e => e.ParentErjaId == null || !scope.Any(p => p.ErjaId == e.ParentErjaId)).OrderBy(e => e.ErjaId),
            ids => scope.Where(e => e.ParentErjaId != null && ids.Contains(e.ParentErjaId.Value)).OrderBy(e => e.ErjaId),
            e => e.ErjaId, pagination);

        static string FullName(Inventory.Api.Data.User? u) =>
            u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username
                : $"{u.FirstName} {u.LastName}".Trim();

        // پاسخ‌های خصوصی فقط برای فرستنده/گیرنده همان ارجاع یا ادمین نمایش داده می‌شوند
        ErjaTreeNodeDto ToNode(Erja e) => new()
        {
            ErjaId = e.ErjaId,
            SenderUserId = e.SenderUserId,
            ReciverUserId = e.ReciverUserId,
            Sender = FullName(e.UserSender),
            Reciver = FullName(e.UserReciver),
            Type = e.Type,
            MatnErja = e.MatnErja,
            AmalgarTitle = e.Amalgar?.Title,
            TypeTaeed = e.TypeTaeed,
            Answer = (e.ShowForAll || e.SenderUserId == userId || e.ReciverUserId == userId || isAdmin) ? e.Answer : "",
            IsRead = e.IsRead,
            Date = e.Date,
            DateRead = e.DateRead,
            DateAnswer = e.DateAnswer,
            MohlatPasokh = e.MohlatPasokh,
            ShowForAll = e.ShowForAll
        };

        // ---- استنتاج والد برای دادهٔ قدیمی ----
        // ارجاع‌هایی که پیش از قاعدهٔ «والد خودکار» ثبت شده‌اند ParentErjaId خالی دارند و
        // در نمودار همه ریشه می‌شدند (C کنار B، نه زیر B). اینجا برای هر ردیف بی‌والد،
        // والد را از نحوهٔ رسیدن نامه به فرستنده‌اش استنتاج می‌کنیم: آخرین ردیفی که
        // گیرندهٔ آن، فرستندهٔ همین ردیف است و زودتر ثبت شده است. چون همیشه ErjaId کوچک‌تر
        // انتخاب می‌شود، زنجیره هرگز حلقه نمی‌شود.
        var receivedBy = new Dictionary<int, List<int>>(); // userId → ارجاع‌هایی که به او رسیده
        foreach (var e in erjas.OrderBy(x => x.ErjaId))
        {
            // ردیف «گیرندهٔ» خودکارِ ثبت نامه (ثبت‌کننده = گیرنده) نباید والد بقیه شود؛
            // اما خودارجاعیِ واقعیِ دبیرخانه (Type = ارجاع/هامش) یک گام گردش است و باید
            // هم در درخت دیده شود و هم بتواند والد گام بعدی باشد.
            if (e.SenderUserId == e.ReciverUserId && string.Equals(e.Type, "گیرنده", StringComparison.Ordinal))
                continue;
            if (!receivedBy.ContainsKey(e.ReciverUserId))
                receivedBy[e.ReciverUserId] = new List<int>();
            receivedBy[e.ReciverUserId].Add(e.ErjaId);
        }
        int? DeriveParent(Erja e)
        {
            if (!receivedBy.TryGetValue(e.SenderUserId, out var list) || list == null || list.Count == 0)
                return null;
            int? best = null;
            foreach (var id in list)
            {
                if (id >= e.ErjaId) break;
                best = id;
            }
            return best;
        }

        var map = erjas.ToDictionary(e => e.ErjaId, ToNode);
        var roots = new List<ErjaTreeNodeDto>();
        bool IsSelfLoop(Erja e, int parentId) => parentId == e.ErjaId;
        foreach (var e in erjas)
        {
            var parentId = e.ParentErjaId is int pid0 && pid0 > 0 ? pid0 : DeriveParent(e);
            if (parentId is int pid && !IsSelfLoop(e, pid) && map.TryGetValue(pid, out var parent))
                parent.Children.Add(map[e.ErjaId]);
            else
                roots.Add(map[e.ErjaId]);
        }
        return roots;
    }

    /// <summary>
    /// کانال SignalR مناسب برای «نوع» نامه.
    ///
    /// باگ B9: سه متد پایین کانال «letters» را سخت‌کد کرده بودند، در حالی که
    /// AddErjaAsync (خط ۱۵۲) و AnswerAsync آن را بر اساس نوع نامه حل می‌کنند و کلاینت
    /// هم تفکیک‌شده مشترک می‌شود (MainLayout: «incoming-letters» / «outgoing-letters»).
    /// نتیجه: خوانده‌شدن یا بایگانی یک نامهٔ وارده/صادره روی کانال نامهٔ «داخلی»
    /// broadcast می‌شد، پس کارتابل همان ماژول و شمارندهٔ نخوانده‌های منوی کناری
    /// به‌روز نمی‌شدند و تا رفرش دستی کهنه می‌ماندند.
    ///
    /// SourceType: 1=داخلی، 2=صادره، 3=وارده — در هر سه سرویس صریحاً مقداردهی می‌شود
    /// (InnerLetterService:161، OutgoingLetterService:247، IncomingLetterService:492)،
    /// پس اتکا به آن از Include کردن سه navigation سبک‌تر و مطمئن‌تر است.
    /// نامهٔ پیدا‌نشده به «letters» برمی‌گردد تا هرگز broadcast حذف نشود.
    /// </summary>
    private static string ChannelOf(int sourceType)
        => sourceType switch { 2 => "outgoing-letters", 3 => "incoming-letters", _ => "letters" };

    private async Task<string> ChannelForAsync(int sourceId)
        => ChannelOf(await _db.LetterSources.AsNoTracking()
            .Where(s => s.Id == sourceId)
            .Select(s => s.SourceType)
            .FirstOrDefaultAsync());

    /// <summary>حالت گروهی — یک کوئی برای همهٔ نامه‌ها، و هر کانالِ درگیر فقط یک بار.</summary>
    private async Task<List<string>> ChannelsForAsync(IEnumerable<int> sourceIds)
    {
        var ids = sourceIds.Distinct().ToList();
        if (ids.Count == 0) return new List<string>();
        var types = await _db.LetterSources.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => s.SourceType)
            .ToListAsync();
        return types.Select(ChannelOf).Distinct().ToList();
    }

    public async Task MarkReadAsync(int erjaId, int userId)
    {
        var erja = await _db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId) return;
        // یک نامه ممکن است برای یک کاربر بیش از یک ارجاع داشته باشد
        // (ارجاع اولیه و گردش‌های بعدی). با بازکردن نامه، همه ارجاع‌های
        // خوانده‌نشده همان نامه برای همان کاربر رویت‌شده محسوب می‌شوند؛
        // وگرنه شمارنده کارتابل می‌تواند بی‌دلیل روی ۱ باقی بماند.
        var now = DateTime.Now;
        var sameLetter = await _db.Erjas
            .Where(e => e.SourceId == erja.SourceId
                        && e.ReciverUserId == userId
                        && !e.IsDelete
                        && !e.IsRead)
            .ToListAsync();
        foreach (var pending in sameLetter)
        {
            pending.IsRead = true;
            pending.DateRead = now;
        }
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync(await ChannelForAsync(erja.SourceId));
    }

    public async Task<bool> ToggleNeshanAsync(int erjaId, int userId)
    {
        var erja = await _db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده می‌تواند نامه را نشان کند.");

        erja.IsNeshan = !erja.IsNeshan;
        await _db.SaveChangesAsync();
        // باگ B9: این متد «هیچ» broadcast‌ای نداشت. ستاره تغییر می‌کرد ولی هیچ
        // کارتابلی باخبر نمی‌شد — پس فهرست «نشان‌شده» تا رفرش دستی کهنه می‌ماند.
        await _notify.BroadcastChangedAsync(await ChannelForAsync(erja.SourceId));
        return erja.IsNeshan;
    }

    /// <summary>بایگانی/خروج از بایگانی نامه دریافتی (روی ارجاع کاربر جاری)</summary>
    public async Task<bool> ToggleBayeganiAsync(int erjaId, int userId)
    {
        var erja = await _db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده می‌تواند نامه را بایگانی کند.");

        erja.IsBayegani = erja.IsBayegani != true;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync(await ChannelForAsync(erja.SourceId));
        return erja.IsBayegani == true;
    }

    /// <summary>بایگانی گروهی چند نامه دریافتی</summary>
    public async Task<int> BatchBayeganiAsync(List<int> erjaIds, int userId, string? description = null)
    {
        if (erjaIds == null || erjaIds.Count == 0) return 0;

        var erjas = await _db.Erjas
            .Where(e => erjaIds.Contains(e.ErjaId) && e.ReciverUserId == userId && !e.IsDelete)
            .ToListAsync();

        foreach (var erja in erjas)
        {
            erja.IsBayegani = true;

            var exists = await _db.LetterBayeganis
                .AnyAsync(b => b.ErjaId == erja.ErjaId && b.UserId == userId && !b.IsDelete);

            if (!exists)
            {
                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    ErjaId = erja.ErjaId,
                    LetterId = erja.SourceId,
                    UserId = userId,
                    IsFolder = false,
                    // موجودیت LetterBayegani ستون «Title» دارد (نه Name) و CreatedAt ندارد؛
                    // برای جلوگیری از تغییر اسکیمای دیتابیس، همان Title پر می‌شود.
                    Title = string.IsNullOrWhiteSpace(description) ? "بایگانی گروهی" : description.Trim(),
                    TypeBayegani = 1
                });
            }
        }

        await _db.SaveChangesAsync();
        // انتخاب گروهی می‌تواند نامه‌هایی از چند ماژول مختلف داشته باشد، پس هر کانالِ
        // درگیر یک بار broadcast می‌شود — با «یک» کوئی، نه یکی به ازای هر نامه.
        foreach (var ch in await ChannelsForAsync(erjas.Select(e => e.SourceId)))
            await _notify.BroadcastChangedAsync(ch);
        return erjas.Count;
    }

    public Task<List<AmalgarDto>> GetAmalgarsAsync(Paging.Request? pagination = null) =>
        _db.Amalgars.AsNoTracking()
            .Where(a => !a.IsDelete)
            .OrderBy(a => a.AmalgarId)
            .Select(a => new AmalgarDto { AmalgarId = a.AmalgarId, Title = a.Title, TaeedEmza = a.TaeedEmza })
            .ToPageListAsync(pagination);

    public async Task<AmalgarDto> CreateAmalgarAsync(string title, string? taeedEmza = null)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new Exception("عنوان عملگر را وارد کنید.");
        if (title.Length > 100) throw new Exception("عنوان عملگر حداکثر ۱۰۰ کاراکتر است.");

        // عملگر تکراری ساخته نشود — همان قبلی برگردانده و احیا می‌شود
        var exist = await _db.Amalgars.FirstOrDefaultAsync(a => a.Title == title);
        if (exist is not null)
        {
            if (exist.IsDelete) { exist.IsDelete = false; await _db.SaveChangesAsync(); }
            return new AmalgarDto { AmalgarId = exist.AmalgarId, Title = exist.Title, TaeedEmza = exist.TaeedEmza };
        }

        var amalgar = new Amalgar
        {
            Title = title,
            TaeedEmza = (taeedEmza ?? "").Trim(),
            IsDelete = false
        };
        _db.Amalgars.Add(amalgar);
        await _db.SaveChangesAsync();
        return new AmalgarDto { AmalgarId = amalgar.AmalgarId, Title = amalgar.Title, TaeedEmza = amalgar.TaeedEmza };
    }
}
