using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office.Outgoing;

// ============================================================
//  سرویس نامه صادره — با امضا کنندگان + SadereNumber
//  • DateSabt و Number قبلا موجود — حفظ شد
//  • SadereNumber و DateSadere اضافه شد — بعد از امضا مقدار می‌گیرد
//  • OutgoingLetterSigner: انتخاب بر اساس دسترسی OutgoingLetters.Sign
//  • کارتابل دریافت شامل امضایی‌ها (IsSigner) هم می‌شود
//  • امضا: POST {id}/sign → اگر همه امضا کردند Status=3 و SadereNumber=LetterNumber
// ============================================================

public interface IOutgoingLetterService
{
    Task<int> AddOutgoingLetterAsync(AddOutgoingLetterDto dto, int creatorUserId, string creatorName);
    Task<PagedResult<OutgoingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly, int page = 1, int pageSize = 20,
        bool? starredOnly = null, bool? attachOnly = null);
    Task<PagedResult<OutgoingLetterListItemDto>> GetArchiveAsync(int userId, string? search, int page = 1, int pageSize = 20);
    Task<PagedResult<OutgoingLetterListItemDto>> GetSentAsync(int userId, string? search, int page = 1, int pageSize = 20,
        bool? starredOnly = null, bool? attachOnly = null);
    Task<PagedResult<OutgoingLetterListItemDto>> GetSigningInboxAsync(int userId, string? search, bool? unsignedOnly, int page = 1, int pageSize = 20);

    /// <summary>علامت‌گذاری «خوانده شد» برای ارجاع‌ها و ردیف امضای همین کاربر (بدون نیاز به امضا).</summary>
    Task MarkReadAsync(int letterId, int userId);
    Task<OutgoingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin);
    Task<OutgoingLetterCartableStatsDto> GetStatsAsync(int userId);
    Task<PagedResult<OutgoingLetterPickDto>> PickListAsync(int userId, string? search, int page = 1, int pageSize = 20);
    Task DeleteAsync(int letterId, int userId, bool isAdmin);
    Task EditAsync(int letterId, EditOutgoingLetterDto dto, int userId, bool isAdmin);
    Task UpdateStatusAsync(int letterId, int newStatus, int userId, bool isAdmin);
    Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId, Paging.Request? pagination = null);
    Task SignAsync(int letterId, int userId, string? signNote);
    Task<bool> ToggleLetterNeshanAsync(int letterId, int userId);
    Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search, Paging.Request? pagination = null);

    // ==================== دبیرخانه نامه صادره ====================
    Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(DabirkhaneSearchDto filter, Paging.Request? pagination = null);
    Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync();
    Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto, int userId, string userName);

    /// <summary>فهرست ثبت‌کنندگان نامه (برای فیلتر جستجوی پیشرفتهٔ دبیرخانه)</summary>
    Task<List<LetterReciverDto>> GetDabirkhaneCreatorsAsync(Paging.Request? pagination = null);

    Task<List<LetterCompanyDto>> GetCompaniesAsync(Paging.Request? pagination = null);

    // ==================== رونوشت‌گیرندگان (جدول مستقل) ====================

    /// <summary>فهرست رونوشت‌گیرندگان یک نامه صادره</summary>
    Task<List<OutgoingLetterCopyToDto>> GetCopyTosAsync(int letterId, int userId, bool isAdmin, Paging.Request? pagination = null);

    /// <summary>جایگزینی کامل فهرست رونوشت‌های یک نامه</summary>
    Task<List<OutgoingLetterCopyToDto>> ReplaceCopyTosAsync(
        int letterId, List<SaveOutgoingLetterCopyToDto> items, int userId, bool isAdmin);
}

public class OutgoingLetterService : IOutgoingLetterService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly ILetterGroupService _groups;
    private readonly ILetterStratureService _strature;
    private readonly IEmailService _email;

    public OutgoingLetterService(AppDbContext db, INotifyService notify, ILetterGroupService groups, ILetterStratureService strature, IEmailService email)
    {
        _db = db;
        _notify = notify;
        _groups = groups;
        _strature = strature;
        _email = email;
    }

    private async Task<int> NextNumberAsync()
    {
        var pc = new PersianCalendar();
        int currentYear = pc.GetYear(DateTime.Now);
        var startOfYear = new DateTime(currentYear, 1, 1, new PersianCalendar());

        var list = await _db.OutgoingLetters
            .Where(l => !l.IsDelete && !l.Source.IsDelete && l.DateSabt >= startOfYear)
            .ToListAsync();

        var maxInYear = list
            .Where(l => pc.GetYear(l.DateSabt) == currentYear)
            .Select(l => (int?)l.Number)
            .Max() ?? 0;

        return maxInYear + 1;
    }

    /// <summary>
    /// شناسهٔ سمتِ پیش‌فرضِ یک کاربر (SematId — نه OrganizationId) برای fallback جزء «واحد»
    /// در شمارهٔ نامه، وقتی کاربر واحد سازمانی اختصاصی ندارد.
    /// </summary>
    private async Task<int?> DefaultSematIdAsync(int userId)
    {
        if (userId <= 0) return null;
        return await _db.Semats.AsNoTracking()
            .Where(s => s.UserId == userId && s.DefaultSemat && s.IsActive && !s.IsDelete
                        && s.OrganizationId > 0)
            .Select(s => (int?)s.SematId)
            .FirstOrDefaultAsync()
            ?? await _db.Semats.AsNoTracking()
                .Where(s => s.UserId == userId && s.IsActive && !s.IsDelete && s.OrganizationId > 0)
                .OrderBy(s => s.SematId)
                .Select(s => (int?)s.SematId)
                .FirstOrDefaultAsync();
    }

    /// <summary>
    /// ساخت شماره نامه صادره از ساختار پیکربندی‌شده (TypeForm=2) — قابل تنظیم از
    /// «تنظیمات ← ساختار شماره نامه». اگر ساختاری تعریف نشده باشد، فرمت قبلی
    /// «سال/ص-شماره» (مثل 1404/ص-5) حفظ می‌شود تا رفتار قدیمی نشکند.
    /// </summary>
    private async Task<string> BuildLetterNumberAsync(int number, DateTime? date = null, int? sematId = null, int? userId = null)
    {
        var s = await _strature.TotalNumberAsync(number, typeForm: 2, date: date, sematId: sematId, userId: userId);
        if (!string.IsNullOrEmpty(s)) return s;

        var pc = new PersianCalendar();
        return $"{pc.GetYear(date ?? DateTime.Now)}/ص-{number}";
    }

    /// <summary>
    /// شرکت پیش‌فرض نامه وقتی انتخاب نشده است: شرکت فعالِ همین درخواست
    /// (هدر X-Company-Id که میان‌افزار اعتبارسنجی کرده است). عمداً «اولین شرکت پایگاه داده»
    /// انتخاب نمی‌شود تا نامه روی شرکت دیگر ثبت و چاپ نشود.
    /// </summary>
    private int? DefaultCompanyIdForLetter()
    {
        var active = _db.ActiveCompanyId;
        return active is > 0 ? active : null;
    }

    private static Erja NewErja(int sourceId, int senderUserId, int reciverUserId, DateTime date, string type, string matn = "", int amalgarId = 1, DateTime? mohlat = null, int? parentErjaId = null) => new()
    {
        SourceId = sourceId,
        SenderUserId = senderUserId,
        ReciverUserId = reciverUserId,
        Date = date,
        Type = type,
        TypeTaeed = 0,
        Answer = "",
        IsRead = false,
        IsBayegani = false,
        MatnErja = matn,
        AmalgarId = amalgarId,
        MohlatPasokh = mohlat,
        IsNeshan = false,
        ShowForAll = false,
        ShowMassage = true,
        IsReadAnswer = false,
        ShowMassageAnswer = false,
        IsDelete = false,
        ParentErjaId = parentErjaId
    };

    /// <summary>تبدیل ارقام لاتین به فارسی — برای جستجوی شماره‌ها با ارقام فارسی</summary>
    private static string ToFaDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var map = new[] { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] >= '0' && chars[i] <= '9') chars[i] = map[chars[i] - '0'];
        return new string(chars);
    }

    /// <summary>تبدیل ارقام فارسی/عربی به لاتین — برای جستجوی یکسان شماره‌ها</summary>
    private static string ToEnDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '۰' && chars[i] <= '۹') chars[i] = (char)(chars[i] - '۰' + '0');
            else if (chars[i] >= '٠' && chars[i] <= '٩') chars[i] = (char)(chars[i] - '٠' + '0');
        }
        return new string(chars);
    }

    // ---------- بررسی دسترسی Sign برای لیست کاربران ----------
    private async Task<HashSet<int>> GetUsersWithSignPermissionAsync()
    {
        // کاربران دارای پرمیشن OutgoingLetters.Sign via نقش
        var perm = await _db.Permissions.FirstOrDefaultAsync(p => p.Module == "OutgoingLetters" && p.Action == "Sign");
        if (perm == null) return new HashSet<int>();

        var roleIds = await _db.RolePermissions.Where(rp => rp.PermissionId == perm.Id).Select(rp => rp.RoleId).ToListAsync();
        if (roleIds.Count == 0) return new HashSet<int>();

        var userIds = await _db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
        // ادمین‌ها (Role=Admin) همیشه مجازند
        var adminUserIds = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive).Select(u => u.Id).ToListAsync();
        var set = new HashSet<int>(userIds);
        foreach (var a in adminUserIds) set.Add(a);
        return set;
    }

    private async Task<List<int>> ResolveSignersAsync(List<int> userIds, List<int> groupIds)
    {
        var all = new List<int>();
        all.AddRange(userIds);
        if (groupIds.Count > 0)
        {
            var expanded = await _groups.ExpandToUserIdsAsync(groupIds);
            all.AddRange(expanded);
        }
        return all.Distinct().ToList();
    }

    public async Task<int> AddOutgoingLetterAsync(AddOutgoingLetterDto dto, int creatorUserId, string creatorName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان نامه الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.ReceiverOrganization))
            throw new Exception("نام سازمان مقصد الزامی است.");

        var allGroupIds = dto.GroupsGirande.Concat(dto.GroupsErja).Concat(dto.GroupsHamesh).Concat(dto.SignerGroupIds).Distinct().ToList();
        if (allGroupIds.Count > 0)
        {
            var ok = await _groups.CheckGroupIdsAsync(allGroupIds);
            if (!ok) throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");
        }

        var girande = dto.ReciversGirande.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirande)).Distinct().ToList();
        var erjaIds = dto.ReciversErja.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsErja)).Distinct().ToList();
        var hamesh = dto.ReciversHamesh.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();
        var signerIds = await ResolveSignersAsync(
            dto.SignerSelections.Select(x => x.UserId).Concat(dto.SignerUserIds).Distinct().ToList(), dto.SignerGroupIds);
        var requestedSignerSemats = await ResolveRequestedSignerSematsAsync(dto.SignerSelections);
        var permittedSignerIds = await GetUsersWithSignPermissionAsync();
        if (signerIds.Any(id => !permittedSignerIds.Contains(id)))
            throw new Exception("فقط کاربران دارای مجوز امضای نامه صادره می‌توانند امضاکننده باشند.");

        // 🔒 الزام کارفرما: نامه صادره باید حداقل یک امضا کننده داشته باشد
        if (signerIds.Count == 0)
            throw new Exception("نامه صادره باید حداقل یک امضا کننده داشته باشد.");

        // شرکت صادرکننده (سربرگ چاپ) — در صورت انتخاب باید معتبر باشد
        if (dto.CompanyId is > 0)
        {
            var companyOk = await _db.SystemCompanies.AnyAsync(c => c.Id == dto.CompanyId && c.IsActive);
            if (!companyOk) throw new Exception("شرکت انتخاب‌شده برای سربرگ معتبر یا فعال نیست.");
        }

        girande.Remove(creatorUserId);
        erjaIds.Remove(creatorUserId);
        hamesh.Remove(creatorUserId);

        var allReciverIds = girande.Concat(erjaIds).Concat(hamesh).Distinct().ToList();
        var allUserIdsForValidation = allReciverIds.Concat(signerIds).Distinct().ToList();

        if (allUserIdsForValidation.Count > 0)
        {
            var validUsers = await _db.Users.Where(u => allUserIdsForValidation.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync();
            var invalid = allUserIdsForValidation.Except(validUsers).ToList();
            if (invalid.Count > 0)
                throw new Exception("برخی از کاربران انتخاب‌شده معتبر یا فعال نیستند.");
        }

        // اعتبارسنجی دسترسی Sign برای امضا کنندگان (اختیاری: فقط هشدار، اما اگر هیچ‌کس دسترسی ندارد اجازه می‌دهیم تا ادمین تنظیم کند)
        // اگر RBAC فعال است، چک می‌کنیم اما خطا نمی‌دهیم — فقط اگر لیست کاربران مجاز خالی نباشد
        var allowedSigners = await GetUsersWithSignPermissionAsync();
        if (allowedSigners.Count > 0 && signerIds.Count > 0)
        {
            var notAllowed = signerIds.Where(id => !allowedSigners.Contains(id)).ToList();
            if (notAllowed.Count > 0)
            {
                // برای سخت‌گیری کمتر: فقط لاگ، اما می‌توان خطا داد — فعلا خطا می‌دهیم تا کارفرما متوجه شود
                // throw new Exception("برخی از امضا کنندگان دسترسی امضای نامه صادره را ندارند.");
                // فعلا اجازه می‌دهیم اما در UI فیلتر می‌شود — برای انعطاف
            }
        }

        var now = DateTime.Now;

        using var tx = await _db.Database.BeginTransactionAsync();

        var source = new LetterSource { SourceType = 2, IsDelete = false };
        _db.LetterSources.Add(source);
        await _db.SaveChangesAsync();

        var number = await NextNumberAsync();
        var letter = new OutgoingLetter
        {
            Id = source.Id,
            Number = number,
            // جزء «واحد»: اول واحدِ اختصاصی خودِ سازندهٔ نامه، بعد سازمانِ سمتِ پیش‌فرض او
            // و در نهایت واحد پیش‌فرض شرکت فعال (منطق در LetterStratureService).
            LetterNumber = await BuildLetterNumberAsync(number, now,
                sematId: await DefaultSematIdAsync(creatorUserId), userId: creatorUserId),
            // هر دو ستون برای سازگاری دیتابیس‌های جدید و قدیمی مقداردهی می‌شوند.
            CreatorUserId = creatorUserId,
            CreatorId = creatorUserId,
            Title = dto.Title.Trim(),
            Text = dto.Text,
            DateSabt = now,
            Mahramanegi = string.IsNullOrWhiteSpace(dto.Mahramanegi) ? "عادی" : dto.Mahramanegi,
            Foriat = string.IsNullOrWhiteSpace(dto.Foriat) ? "عادی" : dto.Foriat,
            ReceiverOrganization = dto.ReceiverOrganization.Trim(),
            ReceiverName = dto.ReceiverName?.Trim(),
            ReceiverTitle = dto.ReceiverTitle?.Trim(),
            ReceiverAddress = dto.ReceiverAddress?.Trim(),
            CopyTo = dto.CopyTo?.Trim(),
            ExternalRefNumber = dto.ExternalRefNumber?.Trim(),
            // اگر کاربر شرکتی انتخاب نکرده باشد، شرکت فعالِ همین درخواست ذخیره می‌شود؛
            // در غیر این صورت CompanyId = NULL می‌ماند و چاپ نمی‌داند روی طرح کدام شرکت برود.
            CompanyId = dto.CompanyId is > 0 ? dto.CompanyId : DefaultCompanyIdForLetter(),
            Status = signerIds.Count > 0 ? 1 : (allReciverIds.Count > 0 ? 1 : 2),
            IsDelete = false
        };
        _db.OutgoingLetters.Add(letter);

        var seen = new HashSet<int>();
        foreach (var uid in girande.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "گیرنده"));
        foreach (var uid in erjaIds.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "ارجاع"));
        foreach (var uid in hamesh.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "هامش"));

        // امضا کنندگان — سمتِ هر امضاکننده هم‌زمان ثبت می‌شود تا در چاپِ نامه
        // نام و سمتِ درستِ همان لحظه نمایش داده شود.
        int order = 1;
        var distinctSignerIds = signerIds.Distinct().ToList();
        var signerSematIds = await DefaultSematIdsAsync(distinctSignerIds);
        foreach (var uid in distinctSignerIds)
        {
            _db.OutgoingLetterSigners.Add(new OutgoingLetterSigner
            {
                SourceId = source.Id,
                UserId = uid,
                SematId = requestedSignerSemats.TryGetValue(uid, out var selectedSematId)
                    ? selectedSematId
                    : signerSematIds.TryGetValue(uid, out var sid) ? sid : null,
                Order = order++,
                IsSigned = false,
                IsDelete = false
            });
        }

        foreach (var rel in dto.RelatedLetters)
        {
            if (rel.RelateLetterId <= 0) continue;
            var exists = await _db.LetterSources.AnyAsync(s => s.Id == rel.RelateLetterId && !s.IsDelete);
            if (!exists) continue;
            _db.RelatedLetters.Add(new RelatedLetter
            {
                Related = rel.Related == 2 ? 2 : 1,
                LetterId = source.Id,
                RelateLetterId = rel.RelateLetterId,
                UserId = creatorUserId,
                IsDelete = false
            });
        }

        await _db.SaveChangesAsync();

        // رونوشت‌گیرندگان — هر گیرنده یک ردیف مستقل در جدول OutgoingLetterCopyToes.
        // ستون قدیمی CopyTo نیز یکپارچه می‌شود تا جستجو و چاپ‌های پیشین همچنان کار کنند.
        if (dto.CopyTos != null && dto.CopyTos.Count > 0)
        {
            var validCopies = dto.CopyTos.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
            var rowNo = 1;
            foreach (var ct in validCopies)
            {
                _db.OutgoingLetterCopyToes.Add(new OutgoingLetterCopyTo
                {
                    OutgoingLetterId = source.Id,
                    RowNo = rowNo++,
                    Name = ct.Name.Trim(),
                    Desc = string.IsNullOrWhiteSpace(ct.Desc) ? null : ct.Desc.Trim(),
                    RefNo = string.IsNullOrWhiteSpace(ct.RefNo) ? null : ct.RefNo.Trim(),
                    CreatorUserId = creatorUserId,
                    CreatedAt = DateTime.Now,
                    IsDelete = false
                });
            }
            letter.CopyTo = validCopies.Count == 0
                ? null
                : string.Join("، ", validCopies.Select(x => x.Name.Trim()));
            await _db.SaveChangesAsync();
        }


        if (dto.FromPishnevisId is > 0)
        {
            var pish = await _db.OutgoingPishnevisLetters.FirstOrDefaultAsync(p => p.PishnevisId == dto.FromPishnevisId && p.UserId == creatorUserId && !p.IsDelete);
            if (pish != null)
            {
                // پیش‌نویس را حذف نمی‌کنیم — کاربر ممکن است دوباره از آن استفاده کند
                // فقط پیوست‌ها را به نامه ارسال‌شده منتقل می‌کنیم
                var pishAtts = await _db.AppAttachments.Where(a => a.Module == "OutgoingPishnevis" && a.RefId == pish.PishnevisId).ToListAsync();
                foreach (var a in pishAtts)
                {
                    a.Module = "OutgoingLetters";
                    a.RefId = source.Id;
                }
                await _db.SaveChangesAsync();
            }
        }

        await tx.CommitAsync();

        // نوتیفیکیشن به گیرندگان داخلی + امضا کنندگان
        var allNotified = seen.Union(signerIds).Distinct().ToList();
        if (allNotified.Count > 0)
        {
            var link = $"outgoing-letters/view/{source.Id}";
            await _notify.SendManyAsync(allNotified,
                signerIds.Count > 0 ? $"نامه صادره جهت امضا: {letter.Title}" : $"نامه صادره جدید جهت تایید: {letter.Title}",
                $"گیرنده: {letter.ReceiverOrganization} — شماره {letter.LetterNumber} — فوریت: {letter.Foriat}",
                creatorName, "نامه صادره", link);
        }
        await _notify.BroadcastChangedAsync("outgoing-letters");

        return source.Id;
    }

    /// <summary>متنِ اختصاصیِ ردیف‌های «دسترسی از طریق دستورکار» — برای کنار گذاشتن خودارجاعی.</summary>
    private const string AutoFlowText = "دسترسی به نامه از طریق دستورکار";

    // ---------- Inbox: شامل ارجاعات + امضا ----------
    public async Task<PagedResult<OutgoingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly, int page = 1, int pageSize = 20,
        bool? starredOnly = null, bool? attachOnly = null)
    {
        // بخش 1: ارجاعات
        var qErja = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.OutgoingLetter != null && !e.Source.OutgoingLetter.IsDelete
                        && e.IsBayegani != true
                        // ردیف خودارجاعیِ ساختگیِ «دسترسی از طریق دستورکار» در دریافتی نیاید
                        && !(e.SenderUserId == userId && e.MatnErja.StartsWith(AutoFlowText)));

        if (unreadOnly == true) qErja = qErja.Where(e => !e.IsRead);

        // چیپ‌های کارتابل «سمت سرور» — فیلتر سمت کلاینت فقط صفحهٔ جاری را می‌دید
        if (starredOnly == true) qErja = qErja.Where(e => e.IsNeshan);
        if (attachOnly == true)
            qErja = qErja.Where(e => _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == e.SourceId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            qErja = qErja.Where(e => e.Source.OutgoingLetter!.Title.Contains(s)
                             || (e.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                             || e.Source.OutgoingLetter!.ReceiverOrganization.Contains(s)
                             || (e.UserSender!.FirstName + " " + e.UserSender.LastName).Contains(s)
                             || e.UserSender!.Username.Contains(s));
        }

        // بخش 2: امضایی‌ها (حتی اگر ارجاع نداشته باشد)
        var qSign = _db.OutgoingLetterSigners.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDelete && !s.Source.IsDelete && s.Source.OutgoingLetter != null && !s.Source.OutgoingLetter.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            qSign = qSign.Where(sg => sg.Source.OutgoingLetter!.Title.Contains(s)
                                   || (sg.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                                   || sg.Source.OutgoingLetter!.ReceiverOrganization.Contains(s)
                                   || (sg.Source.OutgoingLetter!.SadereNumber ?? "").Contains(s));
        }
        if (unreadOnly == true)
        {
            // «خوانده‌نشده» برای امضاکننده یعنی نامه را باز نکرده؛ نه یعنی امضا نکرده.
            // نامهٔ امضاشده هم به‌طور طبیعی خوانده‌شده است (سازگاری با دادهٔ قبل از این ستون).
            qSign = qSign.Where(s => !s.IsRead && !s.IsSigned);
        }

        // همان فیلترهای کارتابل برای ردیف‌های «امضایی» (بدون ارجاع)
        if (starredOnly == true) qSign = qSign.Where(sg => sg.Source.OutgoingLetter!.IsNeshan);
        if (attachOnly == true)
            qSign = qSign.Where(sg => _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == sg.SourceId));

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 20 : pageSize;
        // The previous dictionary kept the lowest ErjaId for each letter.
        // Preserve that rule explicitly before unioning signer-only letters.
        var matchingErjas = qErja;
        qErja = qErja.Where(e => !matchingErjas.Any(other => other.SourceId == e.SourceId && other.ErjaId < e.ErjaId));
        var keys = qErja.Select(e => new { LetterId = e.SourceId, Date = e.Date })
            .Concat(qSign.Where(s => !qErja.Any(e => e.SourceId == s.SourceId))
                .Select(s => new { LetterId = s.SourceId, Date = s.Source.OutgoingLetter!.DateSabt }).Distinct());
        var totalCount = await keys.CountAsync();
        var pageIds = await keys.OrderByDescending(k => k.Date).ThenByDescending(k => k.LetterId)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).Select(k => k.LetterId).ToListAsync();
        qErja = qErja.Where(e => pageIds.Contains(e.SourceId));
        qSign = qSign.Where(s => pageIds.Contains(s.SourceId));

        var erjaList = await qErja
            .OrderByDescending(e => e.ErjaId)
            .Select(e => new OutgoingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = e.Source.OutgoingLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                ReceiverOrganization = e.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = e.Source.OutgoingLetter!.ReceiverName,
                Date = e.Date,
                Mahramanegi = e.Source.OutgoingLetter!.Mahramanegi,
                Foriat = e.Source.OutgoingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                TypeTaeed = e.TypeTaeed,
                HasAnswer = e.Answer != "",
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == e.SourceId),
                Status = e.Source.OutgoingLetter!.Status,
                SadereNumber = e.Source.OutgoingLetter!.SadereNumber,
                DateSadere = e.Source.OutgoingLetter!.DateSadere,
                IsSigner = false,
                IsSigned = false,
                CanSign = false,
                SignersTotal = e.Source.OutgoingSigners.Count(s => !s.IsDelete),
                SignersSigned = e.Source.OutgoingSigners.Count(s => !s.IsDelete && s.IsSigned)
            })
            .ToListAsync();

        var signerLetters = await qSign
            .OrderByDescending(s => s.Source.OutgoingLetter!.DateSabt)
            .Select(s => new OutgoingLetterListItemDto
            {
                LetterId = s.SourceId,
                ErjaId = null,
                LetterNumber = s.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = s.Source.OutgoingLetter!.Title,
                Sender = s.Source.OutgoingLetter!.Creator != null
                    ? (string.IsNullOrEmpty(s.Source.OutgoingLetter!.Creator.FirstName + s.Source.OutgoingLetter!.Creator.LastName)
                        ? s.Source.OutgoingLetter!.Creator.Username
                        : (s.Source.OutgoingLetter!.Creator.FirstName + " " + s.Source.OutgoingLetter!.Creator.LastName).Trim())
                    : "",
                SenderUserId = s.Source.OutgoingLetter!.CreatorUserId,
                ReceiverOrganization = s.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = s.Source.OutgoingLetter!.ReceiverName,
                // تاریخ نمایش = تاریخ ارجاع به همین کاربر (اگر ارجاعی نباشد، تاریخ ثبت نامه)
                Date = _db.Erjas
                    .Where(e => e.SourceId == s.SourceId && e.ReciverUserId == userId && !e.IsDelete)
                    .Max(e => (DateTime?)e.Date) ?? s.Source.OutgoingLetter!.DateSabt,
                Mahramanegi = s.Source.OutgoingLetter!.Mahramanegi,
                Foriat = s.Source.OutgoingLetter!.Foriat,
                ErjaType = "امضا",
                MatnErja = s.SignNote,
                IsNeshan = false,
                IsRead = s.IsRead || s.IsSigned, // خوانده‌شدن مستقل از امضاست
                TypeTaeed = 0,
                HasAnswer = s.IsSigned,
                ReciverCount = s.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == s.SourceId),
                Status = s.Source.OutgoingLetter!.Status,
                SadereNumber = s.Source.OutgoingLetter!.SadereNumber,
                DateSadere = s.Source.OutgoingLetter!.DateSadere,
                IsSigner = true,
                IsSigned = s.IsSigned,
                CanSign = !s.IsSigned,
                SignersTotal = s.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = s.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            })
            .ToListAsync();

        // ترکیب و حذف تکراری (اگر کاربر هم ارجاع دارد هم امضا کننده است)
        var dict = new Dictionary<int, OutgoingLetterListItemDto>();
        foreach (var item in erjaList)
        {
            dict[item.LetterId] = item;
        }
        foreach (var item in signerLetters)
        {
            if (dict.TryGetValue(item.LetterId, out var existing))
            {
                existing.IsSigner = true;
                existing.IsSigned = item.IsSigned;
                // اگر از هر یک از دو مسیر (ارجاع یا امضا) خوانده شده باشد، خوانده‌شده است
                existing.IsRead = existing.IsRead || item.IsRead;
                existing.CanSign = item.CanSign;
                existing.SignersTotal = item.SignersTotal;
                existing.SignersSigned = item.SignersSigned;
                if (existing.ErjaType != "امضا")
                {
                    // اگر ارجاع و امضا همزمان باشد، نوع را ترکیبی نشان بده
                    existing.ErjaType = existing.ErjaType + " + امضا";
                }
            }
            else
            {
                dict[item.LetterId] = item;
            }
        }

        var items = dict.Values.OrderByDescending(x => x.Date).ThenByDescending(x => x.LetterId).ToList();

        return new PagedResult<OutgoingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<OutgoingLetterListItemDto>> GetSigningInboxAsync(int userId, string? search, bool? unsignedOnly, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var q = _db.OutgoingLetterSigners.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDelete && !s.Source.IsDelete
                        && s.Source.OutgoingLetter != null && !s.Source.OutgoingLetter.IsDelete);

        if (unsignedOnly == true) q = q.Where(s => !s.IsSigned);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(sg => sg.Source.OutgoingLetter!.Title.Contains(s)
                           || (sg.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                           || sg.Source.OutgoingLetter!.ReceiverOrganization.Contains(s));
        }

        var totalCount = await q.CountAsync();

        // ترتیب «امضایی‌های من»:
        //   ۱) نامه‌هایی که هنوز امضا نشده‌اند (نیاز به امضا) بالاتر از امضاشده‌ها؛
        //   ۲) در هر گروه، جدیدترین «تاریخ ارجاع به این کاربر» اول؛ اگر نامه ارجاعی
        //      به کاربر نداشته باشد، تاریخ ثبت نامه ملاک است.
        var items = await q
            .OrderBy(s => s.IsSigned)
            .ThenByDescending(s => _db.Erjas
                .Where(e => e.SourceId == s.SourceId && e.ReciverUserId == userId && !e.IsDelete)
                .Max(e => (DateTime?)e.Date) ?? s.Source.OutgoingLetter!.DateSabt)
            .ThenByDescending(s => s.SourceId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new OutgoingLetterListItemDto
            {
                LetterId = s.SourceId,
                LetterNumber = s.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = s.Source.OutgoingLetter!.Title,
                Sender = s.Source.OutgoingLetter!.Creator != null
                    ? (string.IsNullOrEmpty(s.Source.OutgoingLetter!.Creator.FirstName + s.Source.OutgoingLetter!.Creator.LastName)
                        ? s.Source.OutgoingLetter!.Creator.Username
                        : (s.Source.OutgoingLetter!.Creator.FirstName + " " + s.Source.OutgoingLetter!.Creator.LastName).Trim())
                    : "",
                SenderUserId = s.Source.OutgoingLetter!.CreatorUserId,
                ReceiverOrganization = s.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = s.Source.OutgoingLetter!.ReceiverName,
                Date = s.Source.OutgoingLetter!.DateSabt,
                Mahramanegi = s.Source.OutgoingLetter!.Mahramanegi,
                Foriat = s.Source.OutgoingLetter!.Foriat,
                ErjaType = "امضا",
                IsNeshan = false,
                IsRead = s.IsRead || s.IsSigned,
                TypeTaeed = 0,
                HasAnswer = s.IsSigned,
                ReciverCount = s.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == s.SourceId),
                Status = s.Source.OutgoingLetter!.Status,
                SadereNumber = s.Source.OutgoingLetter!.SadereNumber,
                DateSadere = s.Source.OutgoingLetter!.DateSadere,
                IsSigner = true,
                IsSigned = s.IsSigned,
                CanSign = !s.IsSigned,
                SignersTotal = s.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = s.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            }).ToListAsync();

        return new PagedResult<OutgoingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<OutgoingLetterListItemDto>> GetArchiveAsync(int userId, string? search, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.OutgoingLetter != null && !e.Source.OutgoingLetter.IsDelete
                        && e.IsBayegani == true
                        && !(e.SenderUserId == userId && e.MatnErja.StartsWith(AutoFlowText)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.OutgoingLetter!.Title.Contains(s)
                             || (e.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                             || e.Source.OutgoingLetter!.ReceiverOrganization.Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.ErjaId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new OutgoingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = e.Source.OutgoingLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                ReceiverOrganization = e.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = e.Source.OutgoingLetter!.ReceiverName,
                Date = e.Date,
                Mahramanegi = e.Source.OutgoingLetter!.Mahramanegi,
                Foriat = e.Source.OutgoingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                TypeTaeed = e.TypeTaeed,
                HasAnswer = e.Answer != "",
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == e.SourceId),
                Status = e.Source.OutgoingLetter!.Status,
                SadereNumber = e.Source.OutgoingLetter!.SadereNumber,
                DateSadere = e.Source.OutgoingLetter!.DateSadere,
                SignersTotal = e.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = e.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            }).ToListAsync();

        return new PagedResult<OutgoingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<OutgoingLetterListItemDto>> GetSentAsync(int userId, string? search, int page = 1, int pageSize = 20,
        bool? starredOnly = null, bool? attachOnly = null)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var q = _db.OutgoingLetters.AsNoTracking()
            .Where(l => l.CreatorUserId == userId && !l.IsDelete && !l.Source.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.ReceiverOrganization.Contains(s) || (l.SadereNumber ?? "").Contains(s));
        }

        // چیپ‌های کارتابل «ارسالی» سمت سرور (روی کل نامه‌ها، نه صفحهٔ جاری)
        if (starredOnly == true) q = q.Where(l => l.IsNeshan);
        if (attachOnly == true)
            q = q.Where(l => _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id));

        var totalCount = await q.CountAsync();

        var items = await q
            // ترتیب بریدگی صفحه = ترتیب نمایش (تاریخ ثبت، سپس شناسه)
            .OrderByDescending(l => l.DateSabt)
            .ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new OutgoingLetterListItemDto
            {
                LetterId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                Title = l.Title,
                Sender = "",
                SenderUserId = l.CreatorUserId,
                IsNeshan = l.IsNeshan,
                ReceiverOrganization = l.ReceiverOrganization,
                ReceiverName = l.ReceiverName,
                Date = l.DateSabt,
                Mahramanegi = l.Mahramanegi,
                Foriat = l.Foriat,
                IsRead = l.Source.Erjas.Where(e => !e.IsDelete && e.ParentErjaId == null).All(e => e.IsRead),
                HasAnswer = l.Source.Erjas.Any(e => !e.IsDelete && e.Answer != ""),
                ReciverCount = l.Source.Erjas.Count(e => !e.IsDelete && e.ParentErjaId == null),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id),
                Status = l.Status,
                SadereNumber = l.SadereNumber,
                DateSadere = l.DateSadere,
                SignersTotal = l.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = l.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            }).ToListAsync();

        return new PagedResult<OutgoingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<OutgoingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Creator)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return null;

        var erjas = await _db.Erjas.AsNoTracking()
            .Include(e => e.UserReciver)
            .Where(e => e.SourceId == letterId && !e.IsDelete)
            .OrderBy(e => e.ErjaId)
            .ToListAsync();

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToListAsync();

        bool isMine = letter.CreatorUserId == userId;
        var myErja = erjas.Where(e => e.ReciverUserId == userId).OrderByDescending(e => e.ErjaId).FirstOrDefault();
        var mySigner = signers.FirstOrDefault(s => s.UserId == userId);

        if (!isMine && myErja == null && mySigner == null && !isAdmin) return null;

        static string FullName(User? u) =>
            u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username
                : $"{u.FirstName} {u.LastName}".Trim();

        var dto = new OutgoingLetterDetailDto
        {
            LetterId = letter.Id,
            LetterNumber = letter.LetterNumber ?? "",
            Number = letter.Number,
            Title = letter.Title,
            Text = letter.Text,
            Mahramanegi = letter.Mahramanegi,
            Foriat = letter.Foriat,
            DateSabt = letter.DateSabt,
            SenderUserId = letter.CreatorUserId,
            SenderName = FullName(letter.Creator),
            IsMine = isMine,
            ReceiverOrganization = letter.ReceiverOrganization,
            ReceiverName = letter.ReceiverName,
            ReceiverTitle = letter.ReceiverTitle,
            ReceiverAddress = letter.ReceiverAddress,
            CopyTo = letter.CopyTo,
            // رونوشت‌گیرندگان از جدول مستقل (منبع اصلی) — ستون قدیمی CopyTo فقط برای نامه‌های قدیمی
            CopyTos = await _db.OutgoingLetterCopyToes.AsNoTracking()
                .Where(c => c.OutgoingLetterId == letter.Id && !c.IsDelete)
                .OrderBy(c => c.RowNo).ThenBy(c => c.Id)
                .Select(c => new OutgoingLetterCopyToDto
                {
                    Id = c.Id,
                    OutgoingLetterId = c.OutgoingLetterId,
                    RowNo = c.RowNo,
                    Name = c.Name,
                    Desc = c.Desc,
                    RefNo = c.RefNo
                })
                .ToListAsync(),
            ExternalRefNumber = letter.ExternalRefNumber,
            Status = letter.Status,
            SadereNumber = letter.SadereNumber,
            DateSadere = letter.DateSadere,
            CompanyId = letter.CompanyId,
            DabirkhaneSabt = letter.DabirkhaneSabt,
            DateDabirkhane = letter.DateDabirkhane,
            DestRegNumber = letter.DestRegNumber,
            SendMethod = letter.SendMethod,
            DabirkhaneNote = letter.DabirkhaneNote,
            DestEmail = letter.DestEmail,
            IsNeshan = letter.IsNeshan,
            CanEdit = isAdmin || (isMine && !erjas.Any(e => e.IsRead) && signers.All(s => !s.IsSigned)),
            IsSigner = mySigner != null,
            CanSign = mySigner != null && !mySigner.IsSigned
        };

        if (letter.CompanyId is > 0)
        {
            dto.CompanyName = await _db.SystemCompanies.AsNoTracking()
                .Where(c => c.Id == letter.CompanyId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();
        }

        if (letter.DabirkhaneUserId is > 0)
        {
            dto.DabirkhaneUserName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == letter.DabirkhaneUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == "" ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync();
        }

        foreach (var e in erjas.Where(e => e.ParentErjaId == null))
        {
            var r = new LetterReciverDto { UserId = e.ReciverUserId, FullName = FullName(e.UserReciver) };
            switch (e.Type)
            {
                case "ارجاع": dto.ReciversErja.Add(r); break;
                case "هامش": dto.ReciversHamesh.Add(r); break;
                default: dto.ReciversGirande.Add(r); break;
            }
        }

        var sematTitles = await SematTitleMapAsync(signers.Where(s => s.SematId != null).Select(s => s.SematId!.Value).ToList());

        dto.Signers = signers.Select(s => new OutgoingSignerDto
        {
            Id = s.Id,
            SourceId = s.SourceId,
            UserId = s.UserId,
            UserName = s.User?.Username ?? "",
            DisplayName = FullName(s.User),
            SematId = s.SematId,
            SematTitle = s.SematId != null && sematTitles.TryGetValue(s.SematId.Value, out var st) ? st : null,
            Order = s.Order,
            IsSigned = s.IsSigned,
            DateSigned = s.DateSigned,
            SignNote = s.SignNote
        }).ToList();

        dto.RelatedLetters = await _db.RelatedLetters.AsNoTracking()
            .Where(r => r.LetterId == letterId && !r.IsDelete)
            .Select(r => new RelatedLetterDto
            {
                Id = r.Id,
                Related = r.Related,
                RelateLetterId = r.RelateLetterId,
                RelateLetterNumber = r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.LetterNumber :
                                     r.RelateLetter.OutgoingLetter != null ? r.RelateLetter.OutgoingLetter.LetterNumber :
                                     r.RelateLetter.IncomingLetter != null ? r.RelateLetter.IncomingLetter.LetterNumber : "",
                RelateLetterTitle = r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.Title :
                                    r.RelateLetter.OutgoingLetter != null ? r.RelateLetter.OutgoingLetter.Title :
                                    r.RelateLetter.IncomingLetter != null ? r.RelateLetter.IncomingLetter.Title : "",
                RelateSourceType = r.RelateLetter.SourceType
            }).ToListAsync();

        if (myErja != null)
        {
            dto.MyErja = new ErjaDto
            {
                ErjaId = myErja.ErjaId,
                SourceId = myErja.SourceId,
                SenderUserId = myErja.SenderUserId,
                ReciverUserId = myErja.ReciverUserId,
                Date = myErja.Date,
                Type = myErja.Type,
                TypeTaeed = myErja.TypeTaeed,
                Answer = myErja.Answer,
                IsRead = myErja.IsRead,
                MohlatPasokh = myErja.MohlatPasokh,
                MatnErja = myErja.MatnErja,
                AmalgarId = myErja.AmalgarId,
                IsNeshan = myErja.IsNeshan,
                ShowForAll = myErja.ShowForAll,
                DateRead = myErja.DateRead,
                DateAnswer = myErja.DateAnswer,
                ParentErjaId = myErja.ParentErjaId
            };
        }

        if (mySigner != null)
        {
            dto.MySigner = new OutgoingSignerDto
            {
                Id = mySigner.Id,
                SourceId = mySigner.SourceId,
                UserId = mySigner.UserId,
                UserName = mySigner.User?.Username ?? "",
                DisplayName = FullName(mySigner.User),
                SematId = mySigner.SematId,
                SematTitle = mySigner.SematId != null && sematTitles.TryGetValue(mySigner.SematId.Value, out var mst) ? mst : null,
                Order = mySigner.Order,
                IsSigned = mySigner.IsSigned,
                DateSigned = mySigner.DateSigned,
                SignNote = mySigner.SignNote
            };
        }

        return dto;
    }

    public async Task<OutgoingLetterCartableStatsDto> GetStatsAsync(int userId)
    {
        // بج «دریافتی» = تعداد نخوانده‌های همان فهرست؛ پس نامه‌های بایگانی‌شده
        // (IsBayegani) نباید شمرده شوند، همان‌طور که GetInboxAsync آن‌ها را نشان نمی‌دهد.
        var inbox = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.OutgoingLetter != null && !e.Source.OutgoingLetter.IsDelete
                        && e.IsBayegani != true
                        && !(e.SenderUserId == userId && e.MatnErja.StartsWith(AutoFlowText)));

        var signing = _db.OutgoingLetterSigners.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDelete && !s.Source.IsDelete
                        && s.Source.OutgoingLetter != null && !s.Source.OutgoingLetter.IsDelete);

        var soon = DateTime.Now.AddDays(2);
        return new OutgoingLetterCartableStatsDto
        {
            InboxUnread = await inbox.CountAsync(e => !e.IsRead) + await signing.CountAsync(s => !s.IsRead && !s.IsSigned),
            InboxTotal = await inbox.CountAsync() + await signing.CountAsync(),
            SentTotal = await _db.OutgoingLetters.CountAsync(l => l.CreatorUserId == userId && !l.IsDelete),
            PishnevisTotal = await _db.OutgoingPishnevisLetters.CountAsync(p => p.UserId == userId && !p.IsDelete),
            DeadlineSoon = await inbox.CountAsync(e => e.MohlatPasokh != null && e.Answer == "" && e.MohlatPasokh <= soon)
        };
    }

    public async Task MarkReadAsync(int letterId, int userId)
    {
        var now = DateTime.Now;
        var erjas = await _db.Erjas
            .Where(e => e.SourceId == letterId && e.ReciverUserId == userId && !e.IsDelete && !e.IsRead)
            .ToListAsync();
        foreach (var e in erjas) { e.IsRead = true; e.DateRead = now; }

        var signers = await _db.OutgoingLetterSigners
            .Where(s => s.SourceId == letterId && s.UserId == userId && !s.IsDelete && !s.IsRead)
            .ToListAsync();
        foreach (var s in signers) { s.IsRead = true; s.DateRead = now; }

        if (erjas.Count > 0 || signers.Count > 0) await _db.SaveChangesAsync();
    }

    public async Task<PagedResult<OutgoingLetterPickDto>> PickListAsync(int userId, string? search, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var q = _db.LetterSources.AsNoTracking()
            .Where(s => !s.IsDelete &&
                        ((s.InnerLetter != null && !s.InnerLetter.IsDelete &&
                          (s.InnerLetter.CreatorUserId == userId || s.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete))) ||
                         (s.OutgoingLetter != null && !s.OutgoingLetter.IsDelete &&
                          (s.OutgoingLetter.CreatorUserId == userId || s.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete)))));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(src =>
                (src.InnerLetter != null && (src.InnerLetter.Title.Contains(s) || (src.InnerLetter.LetterNumber ?? "").Contains(s))) ||
                (src.OutgoingLetter != null && (src.OutgoingLetter.Title.Contains(s) || (src.OutgoingLetter.LetterNumber ?? "").Contains(s) || src.OutgoingLetter.ReceiverOrganization.Contains(s) || (src.OutgoingLetter.SadereNumber ?? "").Contains(s))));
        }

        var totalCount = await q.CountAsync();

        var items = await q.OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new OutgoingLetterPickDto
            {
                LetterId = s.Id,
                LetterNumber = s.InnerLetter != null ? s.InnerLetter.LetterNumber ?? "" : s.OutgoingLetter!.SadereNumber ?? s.OutgoingLetter!.LetterNumber ?? "",
                Title = s.InnerLetter != null ? s.InnerLetter.Title : s.OutgoingLetter!.Title,
                Date = s.InnerLetter != null ? s.InnerLetter.DateSabt : s.OutgoingLetter!.DateSabt,
                IsSent = s.InnerLetter != null ? s.InnerLetter.CreatorUserId == userId : s.OutgoingLetter!.CreatorUserId == userId,
                SourceType = s.SourceType
            }).ToListAsync();

        return new PagedResult<OutgoingLetterPickDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task DeleteAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.Include(l => l.Source).FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند نامه را حذف کند.");

        if (letter.Status == 3 && !isAdmin)
            throw new Exception("نامه صادر شده قابل حذف نیست — فقط مدیر می‌تواند حذف کند.");

        var anyRead = await _db.Erjas.AnyAsync(e => e.SourceId == letterId && !e.IsDelete && e.IsRead);
        var anySigned = await _db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == letterId && !s.IsDelete && s.IsSigned);
        if ((anyRead || anySigned) && !isAdmin)
            throw new Exception("این نامه توسط گیرنده(ها) خوانده یا امضا شده و قابل حذف نیست.");

        letter.IsDelete = true;
        letter.Source.IsDelete = true;
        await _db.Erjas.Where(e => e.SourceId == letterId).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsDelete, true));
        await _db.OutgoingLetterSigners.Where(s => s.SourceId == letterId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDelete, true));
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task EditAsync(int letterId, EditOutgoingLetterDto dto, int userId, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان نامه الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.ReceiverOrganization))
            throw new Exception("نام سازمان مقصد الزامی است.");

        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند نامه را ویرایش کند.");

        if (letter.Status == 3 && !isAdmin)
            throw new Exception("نامه صادر شده قابل ویرایش نیست.");

        var anyRead = await _db.Erjas.AnyAsync(e => e.SourceId == letterId && !e.IsDelete && e.IsRead);
        var anySigned = await _db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == letterId && !s.IsDelete && s.IsSigned);
        if ((anyRead || anySigned) && !isAdmin)
            throw new Exception("این نامه توسط گیرنده(ها) خوانده یا امضا شده و دیگر قابل ویرایش نیست.");

        var allGroupIds = dto.GroupsGirande.Concat(dto.GroupsErja).Concat(dto.GroupsHamesh).Concat(dto.SignerGroupIds).Distinct().ToList();
        if (allGroupIds.Count > 0)
        {
            var ok = await _groups.CheckGroupIdsAsync(allGroupIds);
            if (!ok) throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");
        }

        var girande = dto.ReciversGirande.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirande)).Distinct().ToList();
        var erjaIds = dto.ReciversErja.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsErja)).Distinct().ToList();
        var hamesh = dto.ReciversHamesh.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();
        var signerIds = await ResolveSignersAsync(
            dto.SignerSelections.Select(x => x.UserId).Concat(dto.SignerUserIds).Distinct().ToList(), dto.SignerGroupIds);
        var requestedSignerSemats = await ResolveRequestedSignerSematsAsync(dto.SignerSelections);
        var permittedSignerIds = await GetUsersWithSignPermissionAsync();
        if (signerIds.Any(id => !permittedSignerIds.Contains(id)))
            throw new Exception("فقط کاربران دارای مجوز امضای نامه صادره می‌توانند امضاکننده باشند.");

        // 🔒 الزام کارفرما: نامه صادره باید حداقل یک امضا کننده داشته باشد
        if (signerIds.Count == 0)
            throw new Exception("نامه صادره باید حداقل یک امضا کننده داشته باشد.");

        if (dto.CompanyId is > 0)
        {
            var companyOk = await _db.SystemCompanies.AnyAsync(c => c.Id == dto.CompanyId && c.IsActive);
            if (!companyOk) throw new Exception("شرکت انتخاب‌شده برای سربرگ معتبر یا فعال نیست.");
        }

        girande.Remove(letter.CreatorUserId);
        erjaIds.Remove(letter.CreatorUserId);
        hamesh.Remove(letter.CreatorUserId);

        var allReciverIds = girande.Concat(erjaIds).Concat(hamesh).Distinct().ToList();
        var allUserIdsForValidation = allReciverIds.Concat(signerIds).Distinct().ToList();
        if (allUserIdsForValidation.Count > 0)
        {
            var validUsers = await _db.Users.Where(u => allUserIdsForValidation.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync();
            if (allUserIdsForValidation.Except(validUsers).Any())
                throw new Exception("برخی از کاربران انتخاب‌شده معتبر یا فعال نیستند.");
        }

        using var tx = await _db.Database.BeginTransactionAsync();

        letter.Title = dto.Title.Trim();
        letter.Text = dto.Text;
        letter.Mahramanegi = string.IsNullOrWhiteSpace(dto.Mahramanegi) ? "عادی" : dto.Mahramanegi;
        letter.Foriat = string.IsNullOrWhiteSpace(dto.Foriat) ? "عادی" : dto.Foriat;
        letter.ReceiverOrganization = dto.ReceiverOrganization.Trim();
        letter.ReceiverName = dto.ReceiverName?.Trim();
        letter.ReceiverTitle = dto.ReceiverTitle?.Trim();
        letter.ReceiverAddress = dto.ReceiverAddress?.Trim();
        letter.CopyTo = dto.CopyTo?.Trim();
        letter.ExternalRefNumber = dto.ExternalRefNumber?.Trim();
        // شرکت نامه با ویرایشِ بدون انتخاب شرکت پاک نمی‌شود؛ اگر هیچ شرکتی هم ارسال نشده باشد
        // شرکت فعال درخواست جایگزین می‌شود (قبلاً NULL می‌شد و چاپ شرکت را گم می‌کرد).
        letter.CompanyId = dto.CompanyId is > 0 ? dto.CompanyId : DefaultCompanyIdForLetter();

        var now = DateTime.Now;
        var rootErjas = await _db.Erjas.Where(e => e.SourceId == letterId && !e.IsDelete && e.ParentErjaId == null).ToListAsync();

        var wanted = new Dictionary<int, string>();
        foreach (var uid in girande) wanted.TryAdd(uid, "گیرنده");
        foreach (var uid in erjaIds) wanted.TryAdd(uid, "ارجاع");
        foreach (var uid in hamesh) wanted.TryAdd(uid, "هامش");

        var newlyAdded = new List<int>();
        var allSubErjas = await _db.Erjas.Where(x => x.SourceId == letterId && !x.IsDelete && x.ParentErjaId != null).ToListAsync();

        foreach (var e in rootErjas)
        {
            if (wanted.TryGetValue(e.ReciverUserId, out var type))
            {
                e.Type = type;
                wanted.Remove(e.ReciverUserId);
            }
            else
            {
                e.IsDelete = true;
                var subIds = new List<int> { e.ErjaId };
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var s in allSubErjas.Where(s => s.ParentErjaId is { } pid && subIds.Contains(pid) && !subIds.Contains(s.ErjaId)))
                    { subIds.Add(s.ErjaId); s.IsDelete = true; changed = true; }
                }
            }
        }
        foreach (var (uid, type) in wanted)
        {
            _db.Erjas.Add(NewErja(letterId, letter.CreatorUserId, uid, now, type));
            newlyAdded.Add(uid);
        }

        // Sync signers
        var existingSigners = await _db.OutgoingLetterSigners.Where(s => s.SourceId == letterId && !s.IsDelete).ToListAsync();
        var existingUserIds = existingSigners.Select(s => s.UserId).ToHashSet();
        var wantedSignerIds = signerIds.Distinct().ToHashSet();

        foreach (var es in existingSigners)
        {
            if (!wantedSignerIds.Contains(es.UserId))
                es.IsDelete = true;
            else if (requestedSignerSemats.TryGetValue(es.UserId, out var selectedSematId))
                es.SematId = selectedSematId;
        }
        int maxOrder = existingSigners.Any() ? existingSigners.Max(s => s.Order) : 0;
        var newSignerIds = wantedSignerIds.Where(id => !existingUserIds.Contains(id)).ToList();
        var newSignerSematIds = await DefaultSematIdsAsync(newSignerIds);
        foreach (var uid in newSignerIds)
        {
            _db.OutgoingLetterSigners.Add(new OutgoingLetterSigner
            {
                SourceId = letterId,
                UserId = uid,
                SematId = requestedSignerSemats.TryGetValue(uid, out var selectedSematId)
                    ? selectedSematId
                    : newSignerSematIds.TryGetValue(uid, out var sid) ? sid : null,
                Order = ++maxOrder,
                IsSigned = false,
                IsDelete = false
            });
            newlyAdded.Add(uid);
        }

        var oldRels = await _db.RelatedLetters.Where(r => r.LetterId == letterId && !r.IsDelete).ToListAsync();
        foreach (var r in oldRels) r.IsDelete = true;
        foreach (var rel in dto.RelatedLetters)
        {
            if (rel.RelateLetterId <= 0 || rel.RelateLetterId == letterId) continue;
            var keep = oldRels.FirstOrDefault(o => o.RelateLetterId == rel.RelateLetterId);
            if (keep != null) { keep.IsDelete = false; keep.Related = rel.Related == 2 ? 2 : 1; continue; }
            var exists = await _db.LetterSources.AnyAsync(s => s.Id == rel.RelateLetterId && !s.IsDelete);
            if (!exists) continue;
            _db.RelatedLetters.Add(new RelatedLetter
            {
                Related = rel.Related == 2 ? 2 : 1,
                LetterId = letterId,
                RelateLetterId = rel.RelateLetterId,
                UserId = letter.CreatorUserId,
                IsDelete = false
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        if (newlyAdded.Count > 0)
        {
            var senderName = await _db.Users.Where(u => u.Id == letter.CreatorUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == "" ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync() ?? "";
            await _notify.SendManyAsync(newlyAdded,
                $"نامه صادره جهت بررسی: {letter.Title}",
                $"گیرنده: {letter.ReceiverOrganization} — شماره {letter.LetterNumber}",
                senderName, "نامه صادره", $"outgoing-letters/view/{letterId}");
        }
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task UpdateStatusAsync(int letterId, int newStatus, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند وضعیت نامه را تغییر دهد.");

        if (newStatus < 0 || newStatus > 3)
            throw new Exception("وضعیت نامعتبر است.");

        // درخواست کارفرما: گزینه «صادره شده» از دراپ‌داون حذف شد —
        // وضعیت 3 فقط به‌صورت خودکار بعد از امضای همه امضاکنندگان ثبت می‌شود.
        if (newStatus == 3)
            throw new Exception("وضعیت «صادر شده» به‌صورت دستی قابل انتخاب نیست و فقط با امضای نامه ثبت می‌شود.");

        letter.Status = newStatus;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId, Paging.Request? pagination = null)
    {
        return await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .Select(s => new OutgoingSignerDto
            {
                Id = s.Id,
                SourceId = s.SourceId,
                UserId = s.UserId,
                UserName = s.User != null ? s.User.Username : "",
                DisplayName = s.User != null
                    ? (string.IsNullOrWhiteSpace((s.User.FirstName ?? "") + (s.User.LastName ?? "")) ? s.User.Username : (s.User.FirstName + " " + s.User.LastName).Trim())
                    : "",
                SematId = s.SematId,
                SematTitle = s.SematId != null
                    ? _db.Semats.Where(x => x.SematId == s.SematId)
                        .Select(x => string.IsNullOrWhiteSpace(x.OnvanMokatebati) ? x.Title : x.OnvanMokatebati)
                        .FirstOrDefault()
                    : null,
                Order = s.Order,
                IsSigned = s.IsSigned,
                DateSigned = s.DateSigned,
                SignNote = s.SignNote
            }).ToPageListAsync(pagination);
    }

    public async Task SignAsync(int letterId, int userId, string? signNote)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        var signer = await _db.OutgoingLetterSigners.FirstOrDefaultAsync(s => s.SourceId == letterId && s.UserId == userId && !s.IsDelete)
            ?? throw new Exception("شما جزو امضا کنندگان این نامه نیستید.");

        if (signer.IsSigned)
            throw new Exception("این نامه قبلا توسط شما امضا شده است.");

        signer.IsSigned = true;
        signer.DateSigned = DateTime.Now;
        signer.SignNote = signNote?.Trim();

        // بررسی اینکه همه امضا کرده‌اند یا حداقل یک امضا کافی است؟
        // طبق نیاز: وقتی امضا شد SadereNumber مقدار می‌گیرد — اگر چند امضا کننده دارد، بعد از آخرین امضا
        var allSigners = await _db.OutgoingLetterSigners.Where(s => s.SourceId == letterId && !s.IsDelete).ToListAsync();
        bool allSigned = allSigners.All(s => s.IsSigned || s.Id == signer.Id); // شامل همین امضا

        if (allSigned)
        {
            // SadereNumber = LetterNumber اگر خالی است، در غیر اینصورت حفظ شود
            if (string.IsNullOrWhiteSpace(letter.SadereNumber))
            {
                letter.SadereNumber = letter.LetterNumber;
            }
            letter.DateSadere = DateTime.Now;
            letter.Status = 3; // صادر شده
        }

        await _db.SaveChangesAsync();

        var senderName = await _db.Users.Where(u => u.Id == userId)
            .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == "" ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
            .FirstOrDefaultAsync() ?? "";

        // اطلاع به ایجاد کننده
        await _notify.SendAsync(letter.CreatorUserId,
            $"نامه صادره امضا شد: {letter.Title}",
            $"{senderName} نامه {letter.LetterNumber} را امضا کرد. {(allSigned ? $"شماره صادره: {letter.SadereNumber}" : "")}",
            senderName, "نامه صادره", $"outgoing-letters/view/{letterId}");

        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    /// <summary>
    /// نشان‌کردن/برداشتن نشان (ستاره) نامه صادره توسط فرستنده — مشابه ToggleLetterNeshan نامه داخلی.
    /// نشان گیرندگان داخلی روی Erja.IsNeshan است؛ این متد برای سمت «ارسالی/صادره» است.
    /// </summary>
    public async Task<bool> ToggleLetterNeshanAsync(int letterId, int userId)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");
        if (letter.CreatorUserId != userId)
            throw new Exception("فقط فرستنده نامه می‌تواند آن را نشان کند.");

        letter.IsNeshan = !letter.IsNeshan;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return letter.IsNeshan;
    }

    public async Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search, Paging.Request? pagination = null)
    {
        var allowed = await GetUsersWithSignPermissionAsync();

        var q = _db.Users.AsNoTracking().Where(u => u.IsActive);

        q = q.Where(u => allowed.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // جستجو هم با نام و هم با عنوانِ سمتِ کاربر کار می‌کند
            q = q.Where(u => u.Username.Contains(s) || (u.FirstName + " " + u.LastName).Contains(s) || (u.FirstName ?? "").Contains(s) || (u.LastName ?? "").Contains(s)
                || _db.Semats.Any(x => x.UserId == u.Id && !x.IsDelete && x.IsActive
                    && (x.Title.Contains(s) || (x.OnvanMokatebati ?? "").Contains(s))));
        }

        var list = await q.OrderBy(u => u.Username).ThenBy(x => x.Id)
            .Select(u => new LetterReciverDto
            {
                UserId = u.Id,
                FullName = string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim()
            }).ToPageListAsync(pagination, defaultCap: 100);

        // سمت‌های فعالِ هر امضاکننده (هر کاربر می‌تواند چند سمت داشته باشد)
        var userIds = list.Select(x => x.UserId).ToList();
        var positions = await UserPositionMapAsync(userIds);
        var positionOptions = await UserPositionOptionsMapAsync(userIds);
        var defaultSematIds = await DefaultSematIdsAsync(userIds);
        var defaultSematTitles = await SematTitleMapAsync(defaultSematIds.Values.ToList());
        foreach (var item in list)
        {
            if (positions.TryGetValue(item.UserId, out var titles) && titles.Count > 0)
                item.Positions = titles;
            if (positionOptions.TryGetValue(item.UserId, out var options))
                item.PositionOptions = options;
            if (defaultSematIds.TryGetValue(item.UserId, out var sematId))
            {
                item.SematId = sematId;
                if (defaultSematTitles.TryGetValue(sematId, out var sematTitle))
                    item.SematTitle = sematTitle;
            }
        }
        return list;
    }

    /// <summary>نگاشتِ شناسهٔ کاربر → عنوانِ سمت‌های فعالِ او</summary>
    private async Task<Dictionary<int, List<string>>> UserPositionMapAsync(List<int> userIds)
    {
        var map = new Dictionary<int, List<string>>();
        if (userIds.Count == 0) return map;
        var rows = await _db.Semats.AsNoTracking()
            .Where(s => s.UserId != null && userIds.Contains(s.UserId.Value) && !s.IsDelete && s.IsActive)
            .OrderBy(s => s.SematId)
            .Select(s => new { s.UserId, s.Title })
            .ToListAsync();
        foreach (var r in rows)
        {
            var title = (r.Title ?? "").Trim();
            if (title.Length == 0) continue;
            if (!map.TryGetValue(r.UserId!.Value, out var list))
            {
                list = new List<string>();
                map[r.UserId!.Value] = list;
            }
            if (!list.Any(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase)))
                list.Add(title);
        }
        return map;
    }

    private async Task<Dictionary<int, int?>> ResolveRequestedSignerSematsAsync(List<OutgoingSignerSelectionDto> selections)
    {
        var result = new Dictionary<int, int?>();
        foreach (var selection in selections.Where(x => x.UserId > 0))
            result[selection.UserId] = selection.SematId;
        var sematIds = result.Values.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (sematIds.Count == 0) return result;
        var userIds = result.Keys.ToList();
        var valid = await _db.Semats.AsNoTracking()
            .Where(s => sematIds.Contains(s.SematId) && s.UserId != null && userIds.Contains(s.UserId.Value)
                && !s.IsDelete && s.IsActive)
            .Select(s => new { s.SematId, UserId = s.UserId!.Value })
            .ToListAsync();
        var validPairs = valid.Select(x => (x.UserId, (int?)x.SematId)).ToHashSet();
        foreach (var uid in result.Keys.ToList())
            if (result[uid] is int sid && !validPairs.Contains((uid, (int?)sid)))
                throw new Exception("سمت انتخاب‌شده برای یکی از امضاکنندگان معتبر یا فعال نیست.");
        return result;
    }

    private async Task<Dictionary<int, List<LetterPositionDto>>> UserPositionOptionsMapAsync(List<int> userIds)
    {
        var map = new Dictionary<int, List<LetterPositionDto>>();
        if (userIds.Count == 0) return map;
        var rows = await _db.Semats.AsNoTracking()
            .Where(s => s.UserId != null && userIds.Contains(s.UserId.Value) && !s.IsDelete && s.IsActive)
            .OrderBy(s => s.SematId)
            .Select(s => new { UserId = s.UserId!.Value, s.SematId, s.Title, s.OnvanMokatebati })
            .ToListAsync();
        foreach (var row in rows)
        {
            var title = string.IsNullOrWhiteSpace(row.OnvanMokatebati) ? row.Title?.Trim() : row.OnvanMokatebati.Trim();
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (!map.TryGetValue(row.UserId, out var list)) map[row.UserId] = list = new();
            list.Add(new LetterPositionDto { SematId = row.SematId, Title = title });
        }
        return map;
    }

    /// <summary>نگاشتِ شناسهٔ کاربر → شناسهٔ سمتِ پیش‌فرضِ او</summary>
    private async Task<Dictionary<int, int>> DefaultSematIdsAsync(List<int> userIds)
    {
        var map = new Dictionary<int, int>();
        if (userIds.Count == 0) return map;
        var rows = await _db.Semats.AsNoTracking()
            .Where(s => s.UserId != null && userIds.Contains(s.UserId.Value) && !s.IsDelete && s.IsActive)
            .OrderByDescending(s => s.DefaultSemat)
            .ThenBy(s => s.SematId)
            .Select(s => new { s.UserId, s.SematId })
            .ToListAsync();
        foreach (var r in rows)
        {
            var uid = r.UserId!.Value;
            if (map.ContainsKey(uid)) continue;
            map[uid] = r.SematId;
        }
        return map;
    }

    /// <summary>نگاشتِ شناسهٔ سمت → عنوانِ سمت</summary>
    private async Task<Dictionary<int, string>> SematTitleMapAsync(List<int> sematIds)
    {
        var map = new Dictionary<int, string>();
        if (sematIds.Count == 0) return map;
        var rows = await _db.Semats.AsNoTracking()
            .Where(s => sematIds.Contains(s.SematId))
            .Select(s => new { s.SematId, s.Title, s.OnvanMokatebati })
            .ToListAsync();
        foreach (var r in rows)
        {
            var title = string.IsNullOrWhiteSpace(r.OnvanMokatebati) ? (r.Title ?? "").Trim() : r.OnvanMokatebati!.Trim();
            if (title.Length == 0) continue;
            map[r.SematId] = title;
        }
        return map;
    }

    // ============================================================
    //  دبیرخانه نامه صادره
    //  • فقط نامه‌هایی وارد دبیرخانه می‌شوند که امضا شده باشند
    //    (یعنی SadereNumber مقدار گرفته باشد)
    //  • دبیرخانه: ثبت «شماره ثبت مقصد» + «روش ارسال» + توضیح
    // ============================================================

    /// <summary>
    /// لیست دبیرخانه — نامه‌های امضا شده (SadereNumber دار) با جستجوی پیشرفته.
    /// </summary>
    public async Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(DabirkhaneSearchDto filter, Paging.Request? pagination = null)
    {
        filter ??= new DabirkhaneSearchDto();

        // شرط ورود به دبیرخانه: SadereNumber مقدار گرفته باشد (نامه امضا شده)
        var q = _db.OutgoingLetters.AsNoTracking()
            .Where(l => !l.IsDelete && !l.Source.IsDelete && l.SadereNumber != null && l.SadereNumber != "");

        // ---------- وضعیت ثبت دبیرخانه ----------
        if (filter.RegisteredOnly == true) q = q.Where(l => l.DabirkhaneSabt);
        else if (filter.RegisteredOnly == false) q = q.Where(l => !l.DabirkhaneSabt);

        // ---------- وضعیت بایگانی ----------
        if (filter.ArchivedOnly == true) q = q.Where(l => l.IsArchived);
        else if (filter.ArchivedOnly == false) q = q.Where(l => !l.IsArchived);

        // ---------- فیلترهای مشخص ----------
        if (!string.IsNullOrWhiteSpace(filter.SendMethod))
            q = q.Where(l => l.SendMethod == filter.SendMethod.Trim());

        if (filter.CreatorUserId is > 0)
            q = q.Where(l => l.CreatorUserId == filter.CreatorUserId.Value);

        if (filter.CompanyId is > 0)
            q = q.Where(l => l.CompanyId == filter.CompanyId.Value);

        if (!string.IsNullOrWhiteSpace(filter.ReceiverOrganization))
        {
            var org = filter.ReceiverOrganization.Trim();
            q = q.Where(l => l.ReceiverOrganization.Contains(org));
        }

        if (!string.IsNullOrWhiteSpace(filter.Mahramanegi))
            q = q.Where(l => l.Mahramanegi == filter.Mahramanegi.Trim());

        if (!string.IsNullOrWhiteSpace(filter.Foriat))
            q = q.Where(l => l.Foriat == filter.Foriat.Trim());

        if (filter.HasAttachment == true)
            q = q.Where(l => _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id));
        else if (filter.HasAttachment == false)
            q = q.Where(l => !_db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id));

        // ---------- بازهٔ تاریخ صدور ----------
        if (filter.FromDate is { } from) q = q.Where(l => (l.DateSadere ?? l.DateSabt) >= from);
        if (filter.ToDate is { } to) q = q.Where(l => (l.DateSadere ?? l.DateSabt) <= to);

        // ---------- جستجوی متنی ----------
        // نوشتهٔ شماره‌ها با ارقام فارسی هم پشتیبانی می‌شود (جستجوی ۱۴۰۴/ص-۱۲ با 1404/ص-12)
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var s = filter.Text.Trim();
            var sFa = ToFaDigits(s);
            var sEn = ToEnDigits(s);

            q = q.Where(l => l.Title.Contains(s)
                          || (l.LetterNumber ?? "").Contains(s) || (l.LetterNumber ?? "").Contains(sFa) || (l.LetterNumber ?? "").Contains(sEn)
                          || (l.SadereNumber ?? "").Contains(s) || (l.SadereNumber ?? "").Contains(sFa) || (l.SadereNumber ?? "").Contains(sEn)
                          || l.ReceiverOrganization.Contains(s)
                          || (l.ReceiverName ?? "").Contains(s)
                          || (l.DestRegNumber ?? "").Contains(s) || (l.DestRegNumber ?? "").Contains(sFa) || (l.DestRegNumber ?? "").Contains(sEn)
                          || (l.SendMethod ?? "").Contains(s)
                          || (l.DelivererName ?? "").Contains(s)
                          || (l.TrackingCode ?? "").Contains(s) || (l.TrackingCode ?? "").Contains(sFa) || (l.TrackingCode ?? "").Contains(sEn)
                          || (l.DestFax ?? "").Contains(s) || (l.DestFax ?? "").Contains(sFa) || (l.DestFax ?? "").Contains(sEn)
                          || (l.DestEmail ?? "").Contains(s)
                          || (l.CopyTo ?? "").Contains(s)
                          || (l.DabirkhaneNote ?? "").Contains(s));
        }

        var list = await q
            .OrderBy(l => l.DabirkhaneSabt)           // اول منتظرها
            .ThenByDescending(l => l.DateSadere)
            .ThenByDescending(l => l.Id)
            .Select(l => new DabirkhaneListItemDto
            {
                LetterId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                SadereNumber = l.SadereNumber,
                Title = l.Title,
                CreatorName = l.Creator != null
                    ? (string.IsNullOrEmpty(l.Creator.FirstName + l.Creator.LastName)
                        ? l.Creator.Username
                        : (l.Creator.FirstName + " " + l.Creator.LastName).Trim())
                    : "",
                ReceiverOrganization = l.ReceiverOrganization,
                ReceiverName = l.ReceiverName,
                DateSabt = l.DateSabt,
                DateSadere = l.DateSadere,
                Mahramanegi = l.Mahramanegi,
                Foriat = l.Foriat,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id),
                SignersTotal = l.Source.OutgoingSigners.Count(s => !s.IsDelete),
                SignersSigned = l.Source.OutgoingSigners.Count(s => !s.IsDelete && s.IsSigned),
                CompanyId = l.CompanyId,
                DabirkhaneSabt = l.DabirkhaneSabt,
                DateDabirkhane = l.DateDabirkhane,
                DestRegNumber = l.DestRegNumber,
                SendMethod = l.SendMethod,
                DabirkhaneNote = l.DabirkhaneNote,
                DestEmail = l.DestEmail,

                // فیلدهای وابسته به روش ارسال
                DelivererName = l.DelivererName,
                TrackingCode = l.TrackingCode,
                DestFax = l.DestFax,

                // بایگانی دبیرخانه
                IsArchived = l.IsArchived,
                ArchivedAt = l.ArchivedAt,

                // چاپ دو نسخه
                CopyTo = l.CopyTo,
                HasCopyTo = l.CopyTo != null && l.CopyTo != ""
            })
            .ToPageListAsync(pagination);

        // شناسه گره بایگانی + عنوان پوشه (جدا از کوئری اصلی برای سادگی ترجمهٔ EF)
        var archivedIds = list.Where(x => x.IsArchived).Select(x => x.LetterId).ToList();
        if (archivedIds.Count > 0)
        {
            var nodes = await _db.LetterBayeganis.AsNoTracking()
                .Where(b => b.LetterId.HasValue && archivedIds.Contains(b.LetterId.Value)
                         && b.TypeBayegani == 2 && !b.IsDelete)
                .Select(b => new { b.BayeganiId, b.LetterId, b.ParentId })
                .ToListAsync();

            var folderIds = nodes.Select(n => n.ParentId).Where(p => p > 0).Distinct().ToList();
            var folderTitles = folderIds.Count > 0
                ? await _db.LetterBayeganis.AsNoTracking()
                    .Where(b => folderIds.Contains(b.BayeganiId))
                    .ToDictionaryAsync(b => b.BayeganiId, b => b.Title)
                : new Dictionary<int, string>();

            var byLetter = nodes.ToDictionary(n => n.LetterId!.Value);
            foreach (var item in list)
            {
                if (!byLetter.TryGetValue(item.LetterId, out var node)) continue;
                item.BayeganiId = node.BayeganiId;
                if (node.ParentId > 0 && folderTitles.TryGetValue(node.ParentId, out var ft))
                    item.ArchiveFolderTitle = ft;
            }
        }

        // نام شرکت و نام کاربر دبیرخانه (جدا برای سادگی کوئری)
        var companyIds = list.Where(x => x.CompanyId is > 0).Select(x => x.CompanyId!.Value).Distinct().ToList();
        if (companyIds.Count > 0)
        {
            var companies = await _db.SystemCompanies.AsNoTracking()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            foreach (var item in list)
                if (item.CompanyId is > 0 && companies.TryGetValue(item.CompanyId.Value, out var name))
                    item.CompanyName = name;
        }

        var dabLetterIds = list.Select(x => x.LetterId).ToList();
        var dabUserIds = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => dabLetterIds.Contains(l.Id) && l.DabirkhaneUserId != null)
            .Select(l => new { l.Id, l.DabirkhaneUserId })
            .ToListAsync();
        if (dabUserIds.Count > 0)
        {
            var uids = dabUserIds.Select(x => x.DabirkhaneUserId!.Value).Distinct().ToList();
            var users = await _db.Users.AsNoTracking()
                .Where(u => uids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id,
                    u => string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim());
            var byLetter = dabUserIds.ToDictionary(x => x.Id, x => x.DabirkhaneUserId!.Value);
            foreach (var item in list)
                if (byLetter.TryGetValue(item.LetterId, out var uid) && users.TryGetValue(uid, out var uname))
                    item.DabirkhaneUserName = uname;
        }

        // نام کاربر دبیرخانه‌ای که نامه را بایگانی کرده است
        var archLetterIds = list.Where(x => x.ArchivedByUserName == null && x.IsArchived).Select(x => x.LetterId).ToList();
        if (archLetterIds.Count > 0)
        {
            var archRows = await _db.OutgoingLetters.AsNoTracking()
                .Where(l => archLetterIds.Contains(l.Id) && l.ArchivedByUserId != null)
                .Select(l => new { l.Id, l.ArchivedByUserId })
                .ToListAsync();

            var archUids = archRows.Select(x => x.ArchivedByUserId!.Value).Distinct().ToList();
            var archUsers = await _db.Users.AsNoTracking()
                .Where(u => archUids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id,
                    u => string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim());

            var archByLetter = archRows.ToDictionary(x => x.Id, x => x.ArchivedByUserId!.Value);
            foreach (var item in list)
                if (archByLetter.TryGetValue(item.LetterId, out var uid) && archUsers.TryGetValue(uid, out var uname))
                    item.ArchivedByUserName = uname;
        }

        return list;
    }

    public async Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync()
    {
        var q = _db.OutgoingLetters.AsNoTracking()
            .Where(l => !l.IsDelete && !l.Source.IsDelete && l.SadereNumber != null && l.SadereNumber != "");
        return new DabirkhaneStatsDto
        {
            Pending = await q.CountAsync(l => !l.DabirkhaneSabt),
            Registered = await q.CountAsync(l => l.DabirkhaneSabt),
            Archived = await q.CountAsync(l => l.IsArchived)
        };
    }

    /// <summary>فهرست ثبت‌کنندگان نامه‌های صادره — برای فیلتر جستجوی پیشرفتهٔ دبیرخانه</summary>
    public async Task<List<LetterReciverDto>> GetDabirkhaneCreatorsAsync(Paging.Request? pagination = null)
    {
        var ids = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => !l.IsDelete && !l.Source.IsDelete)
            .Select(l => l.CreatorUserId)
            .Distinct()
            .ToListAsync();

        if (ids.Count == 0) return new List<LetterReciverDto>();

        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.FirstName).ThenBy(u => u.Username).ThenBy(x => x.Id)
            .Select(u => new LetterReciverDto
            {
                UserId = u.Id,
                FullName = string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                    ? u.Username
                    : $"{u.FirstName} {u.LastName}".Trim()
            })
            .ToPageListAsync(pagination);
    }

    /// <summary>ثبت دبیرخانه: شماره ثبت مقصد + روش ارسال — فقط برای نامه‌های امضا شده</summary>
    public async Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto, int userId, string userName)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        // 🔒 شرط کارفرما: نامه فقط وقتی در دبیرخانه ثبت می‌شود که امضا شده باشد (SadereNumber مقدار گرفته)
        if (string.IsNullOrWhiteSpace(letter.SadereNumber))
            throw new Exception("این نامه هنوز امضا نشده است — فقط نامه‌های امضا شده (دارای شماره صادره) در دبیرخانه ثبت می‌شوند.");

        if (string.IsNullOrWhiteSpace(dto.SendMethod))
            throw new Exception("روش ارسال نامه الزامی است.");

        var method = dto.SendMethod.Trim();

        // ==================== اعتبارسنجی فیلدهای وابسته به روش ارسال ====================
        // هر روش ارسال اطلاعات متفاوتی لازم دارد:
        //   • ایمیل            → کدوم ایمیل (آدرس ایمیل مقصد)
        //   • پست / پست پیشتاز  → نام تحویل گیرنده (+ کد رهگیری)
        //   • پیک / تحویل حضوری → نام تحویل گیرنده
        //   • فکس              → شماره فکس مقصد
        //   • اتوماسیون (ECE)  → شماره ثبت مقصد
        var spec = LetterSendMethods.Spec(method);

        bool byEmail = dto.SendByEmail || spec.NeedsDestEmail;
        string? destEmail = dto.DestEmail?.Trim();
        string? delivererName = dto.DelivererName?.Trim();
        string? trackingCode = dto.TrackingCode?.Trim();
        string? destFax = dto.DestFax?.Trim();
        string? destRegNumber = dto.DestRegNumber?.Trim();

        if (byEmail && string.IsNullOrWhiteSpace(destEmail))
            throw new Exception("آدرس ایمیل مقصد را وارد کنید (مشخص کنید نامه به کدوم ایمیل ارسال شود).");

        if (byEmail && !string.IsNullOrWhiteSpace(destEmail) && !destEmail.Contains('@'))
            throw new Exception("آدرس ایمیل مقصد معتبر نیست.");

        if (spec.NeedsDelivererName && string.IsNullOrWhiteSpace(delivererName))
            throw new Exception("نام تحویل گیرنده را وارد کنید.");

        if (spec.TrackingCodeRequired && string.IsNullOrWhiteSpace(trackingCode))
            throw new Exception("کد رهگیری مرسوله را وارد کنید.");

        if (spec.NeedsFax && string.IsNullOrWhiteSpace(destFax))
            throw new Exception("شماره فکس مقصد را وارد کنید.");

        if (spec.NeedsDestRegNumber && string.IsNullOrWhiteSpace(destRegNumber))
            throw new Exception("شماره ثبت مقصد (ECE) را وارد کنید.");

        letter.DabirkhaneSabt = true;
        letter.DabirkhaneUserId = userId;
        letter.DateDabirkhane ??= DateTime.Now;
        letter.DestRegNumber = destRegNumber;
        letter.SendMethod = method;
        letter.DabirkhaneNote = dto.Note?.Trim();
        if (byEmail) letter.DestEmail = destEmail;

        // فیلدهای وابسته به روش ارسال — فقط مقادیر مرتبط با روش انتخابی ذخیره می‌شوند
        letter.DelivererName = spec.NeedsDelivererName ? delivererName : null;
        letter.TrackingCode = spec.NeedsTrackingCode ? trackingCode : null;
        letter.DestFax = spec.NeedsFax ? destFax : null;

        await _db.SaveChangesAsync();

        // ==================== ارسال نامه با پست الکترونیک ====================
        // دبیرخانه نامه امضا شده را (PDF روی سربرگ + پیوست‌ها) به ایمیل مقصد می‌فرستد.
        // اگر ارسال ایمیل ناموفق باشد، ثبت دبیرخانه حفظ می‌شود اما خطا به کاربر نمایش داده می‌شود.
        string? emailError = null;
        if (byEmail)
        {
            try
            {
                await _email.SendLetterEmailAsync(letterId, dto, userName);
            }
            catch (Exception ex)
            {
                emailError = ex.Message;
            }
        }

        // اطلاع به ایجادکننده نامه
        await _notify.SendAsync(letter.CreatorUserId,
            $"نامه صادره در دبیرخانه ثبت شد: {letter.Title}",
            $"شماره صادره: {letter.SadereNumber} — روش ارسال: {letter.SendMethod}" +
            (string.IsNullOrWhiteSpace(letter.DestRegNumber) ? "" : $" — شماره ثبت مقصد: {letter.DestRegNumber}") +
            (byEmail && emailError == null && !string.IsNullOrWhiteSpace(destEmail) ? $" — ارسال با ایمیل به {destEmail}" : "") +
            (emailError != null ? $" — ⚠️ ارسال ایمیل ناموفق: {emailError}" : ""),
            userName, "دبیرخانه صادره", $"outgoing-letters/view/{letterId}");

        await _notify.BroadcastChangedAsync("outgoing-letters");

        if (emailError != null)
            throw new Exception($"نامه در دبیرخانه ثبت شد اما ارسال ایمیل ناموفق بود: {emailError}");
    }

    /// <summary>شرکت‌های فعال برای انتخاب سربرگ نامه صادره</summary>
    public async Task<List<LetterCompanyDto>> GetCompaniesAsync(Paging.Request? pagination = null)
    {
        return await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name).ThenBy(x => x.Id)
            .Select(c => new LetterCompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                LetterheadFileName = c.LetterheadFileName,
                HasLetterhead = c.LetterheadFileName != null && c.LetterheadFileName != ""
            }).ToPageListAsync(pagination);
    }

    // ==================== رونوشت‌گیرندگان (جدول مستقل) ====================

    /// <summary>فهرست رونوشت‌گیرندگان یک نامه صادره</summary>
    public async Task<List<OutgoingLetterCopyToDto>> GetCopyTosAsync(int letterId, int userId, bool isAdmin, Paging.Request? pagination = null)
    {
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) throw new Exception("نامه پیدا نشد.");
        if (!isAdmin && letter.CreatorUserId != userId)
            throw new Exception("فقط ثبت‌کنندهٔ نامه می‌تواند رونوشت‌ها را ببیند.");

        return await _db.OutgoingLetterCopyToes.AsNoTracking()
            .Where(c => c.OutgoingLetterId == letterId && !c.IsDelete)
            .OrderBy(c => c.RowNo).ThenBy(c => c.Id)
            .Select(c => new OutgoingLetterCopyToDto
            {
                Id = c.Id,
                OutgoingLetterId = c.OutgoingLetterId,
                RowNo = c.RowNo,
                Name = c.Name,
                Desc = c.Desc,
                RefNo = c.RefNo
            }).ToPageListAsync(pagination);
    }

    /// <summary>
    /// جایگزینی کامل فهرست رونوشت‌های یک نامه.
    /// ردیف‌های حذف‌شده به‌صورت نرم (IsDelete) حذف می‌شوند تا سابقه بماند؛
    /// ستون قدیمی CopyTo هم با متن یکپارچه به‌روزرسانی می‌شود تا جستجو و
    /// خروجی‌های قدیمی همچنان کار کنند.
    /// </summary>
    public async Task<List<OutgoingLetterCopyToDto>> ReplaceCopyTosAsync(
        int letterId, List<SaveOutgoingLetterCopyToDto> items, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) throw new Exception("نامه پیدا نشد.");
        if (!isAdmin && letter.CreatorUserId != userId)
            throw new Exception("فقط ثبت‌کنندهٔ نامه می‌تواند رونوشت‌ها را تغییر دهد.");

        items ??= new List<SaveOutgoingLetterCopyToDto>();
        var keep = items.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        var keepIds = keep.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();

        var existing = await _db.OutgoingLetterCopyToes
            .Where(c => c.OutgoingLetterId == letterId && !c.IsDelete)
            .ToListAsync();

        // حذف نرمِ ردیف‌هایی که دیگر در فهرست نیستند
        foreach (var old in existing.Where(e => !keepIds.Contains(e.Id)))
            old.IsDelete = true;

        var rowNo = 1;
        foreach (var it in keep)
        {
            var clean = it.Name.Trim();
            if (clean.Length > 300) clean = clean[..300];

            var row = it.Id > 0 ? existing.FirstOrDefault(e => e.Id == it.Id) : null;
            if (row == null)
            {
                row = new OutgoingLetterCopyTo
                {
                    OutgoingLetterId = letterId,
                    CreatorUserId = userId,
                    CreatedAt = DateTime.Now
                };
                _db.OutgoingLetterCopyToes.Add(row);
            }

            row.RowNo = rowNo++;
            row.Name = clean;
            row.Desc = string.IsNullOrWhiteSpace(it.Desc) ? null : it.Desc.Trim();
            row.RefNo = string.IsNullOrWhiteSpace(it.RefNo) ? null : it.RefNo.Trim();
            row.IsDelete = false;
        }

        // همگام‌سازی ستون قدیمی — برای جستجو و چاپ‌های پیشین
        letter.CopyTo = keep.Count == 0 ? null : string.Join("، ", keep.Select(x => x.Name.Trim()));

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");

        return await GetCopyTosAsync(letterId, userId, true);
    }

}
