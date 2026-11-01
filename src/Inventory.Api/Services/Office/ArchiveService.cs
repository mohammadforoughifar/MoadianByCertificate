using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس بایگانی درختی نامه‌ها — پورت از ArchiveServices طرح کارفرما
/// (تطبیق: UserId عددی از توکن به‌جای Guid در درخواست، SematId اختیاری تا فاز چارت سازمانی،
/// TypeBayegani=1 برای بایگانی شخصی)
/// </summary>
public interface IArchiveService
{
    /// <summary>درخت کامل بایگانی کاربر (پوشه‌ها + نامه‌های بایگانی‌شده) — صفحه‌بندی ریشه‌ها در SQL</summary>
    Task<List<BayeganiNodeDto>> GetTreeAsync(int userId, Paging.Request? pagination = null);

    /// <summary>ایجاد دسته اصلی (ریشه — ParentId=0)</summary>
    Task<BayeganiNodeDto> AddMainCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ایجاد زیرپوشه داخل یک پوشه موجود</summary>
    Task<BayeganiNodeDto> AddSubCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ویرایش عنوان پوشه (جابجایی فقط با MoveFolder)</summary>
    Task<BayeganiNodeDto> EditFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto);

    /// <summary>جابجایی پوشه با جلوگیری از حلقه (Circular Reference)</summary>
    Task<BayeganiNodeDto> MoveFolderAsync(int bayeganiId, int newParentId, int userId);

    /// <summary>بایگانی یک یا چند نامه (ارجاع گیرنده یا نامه ارسالی فرستنده) داخل پوشه انتخابی</summary>
    Task AddLettersToArchiveAsync(int userId, ArchiveLettersDto dto);

    /// <summary>خروج نامه ارسالی از بایگانی بر اساس شناسه نامه (مسیر فرستنده)</summary>
    Task UnarchiveByLetterAsync(int letterId, int userId);

    /// <summary>جابجایی نامه بایگانی‌شده به پوشه دیگر</summary>
    Task<BayeganiNodeDto> MoveLetterAsync(int bayeganiId, int newParentId, int userId);

    /// <summary>حذف: پوشه خالی حذف می‌شود؛ نامه از بایگانی خارج می‌شود (Soft Delete)</summary>
    Task DeleteAsync(int bayeganiId, int userId);

    /// <summary>خروج نامه از بایگانی بر اساس شناسه ارجاع (برای دکمه بایگانی در نمایش نامه)</summary>
    Task UnarchiveByErjaAsync(int erjaId, int userId);

    /// <summary>
    /// خروج قطعی یک «نامهٔ وارده» از بایگانی کاربر: همهٔ گره‌های درختی و همهٔ پرچم‌های
    /// بایگانیِ همین کاربر برای همین نامه آزاد می‌شوند تا نامه حتماً به فهرست برگردد.
    /// </summary>
    Task<int> UnarchiveIncomingLetterAsync(int letterId, int userId);

    // ==================== بایگانی دبیرخانه (نامه صادره — TypeBayegani=2) ====================
    // بایگانی دبیرخانه درخت پوشه‌های مستقل خودش را دارد تا با بایگانی شخصی
    // نامه‌های داخلی قاطی نشود.

    /// <summary>درخت بایگانی دبیرخانه (پوشه‌ها + نامه‌های صادرهٔ بایگانی‌شده) — صفحه‌بندی ریشه‌ها در SQL</summary>
    Task<List<BayeganiNodeDto>> GetOutgoingTreeAsync(int userId, Paging.Request? pagination = null);

    /// <summary>ایجاد دسته اصلی در ریشهٔ بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> AddOutgoingMainCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ایجاد زیرپوشه در بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> AddOutgoingSubCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ویرایش عنوان پوشهٔ بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> EditOutgoingFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto);

    /// <summary>جابجایی پوشه در بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> MoveOutgoingFolderAsync(int bayeganiId, int newParentId, int userId);

    /// <summary>حذف پوشه/نامه از بایگانی دبیرخانه</summary>
    Task DeleteOutgoingAsync(int bayeganiId, int userId);

    /// <summary>بایگانی یک یا چند نامه صادره در پوشهٔ انتخابی</summary>
    Task ArchiveOutgoingLettersAsync(int userId, ArchiveOutgoingLettersDto dto);

    /// <summary>خروج نامه صادره از بایگانی دبیرخانه</summary>
    Task UnarchiveOutgoingLetterAsync(int letterId, int userId);

    /// <summary>جابجایی نامه صادرهٔ بایگانی‌شده به پوشه‌ای دیگر</summary>
    Task<BayeganiNodeDto> MoveArchivedLetterAsync(MoveArchivedLetterDto dto, int userId);
}

public class ArchiveService : IArchiveService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public ArchiveService(AppDbContext db, INotifyService notify)
    {
        _db = db;
        _notify = notify;
    }

    // ---------- اعتبارسنجی مشترک (معادل Validators طرح اصلی) ----------
    private static string ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new Exception("عنوان را وارد کنید.");
        var t = title.Trim();
        if (t.Length > 200)
            throw new Exception("عنوان نمی‌تواند بیشتر از ۲۰۰ کاراکتر باشد.");
        return t;
    }

    private async Task<LetterBayegani> GetOwnedAsync(int bayeganiId, int userId)
    {
        var entity = await _db.LetterBayeganis
            .SingleOrDefaultAsync(x => x.BayeganiId == bayeganiId && !x.IsDelete);

        if (entity == null) throw new Exception("مورد بایگانی یافت نشد.");
        if (entity.UserId != userId) throw new Exception("دسترسی به بایگانی کاربر دیگر مجاز نیست.");
        return entity;
    }

    /// <summary>نوع بایگانی شخصی نامه‌های داخلی</summary>
    private const int TypeInner = 1;

    /// <summary>نوع بایگانی دبیرخانه (نامه صادره)</summary>
    private const int TypeOutgoing = 2;

    /// <summary>بررسی وجود پوشه — type جداکنندهٔ بایگانی شخصی از بایگانی دبیرخانه است</summary>
    private async Task<bool> FolderExistsAsync(int folderId, int userId, int type = TypeInner) =>
        await _db.LetterBayeganis.AsNoTracking()
            .AnyAsync(x => x.BayeganiId == folderId && x.IsFolder && !x.IsDelete
                        && x.UserId == userId && x.TypeBayegani == type);

    /// <summary>جلوگیری از قرارگرفتن پوشه داخل زیرپوشه‌های خودش</summary>
    private async Task<bool> IsDescendantAsync(int folderId, int targetParentId, int type = TypeInner)
    {
        var currentParentId = targetParentId;
        var guard = 0;

        while (currentParentId > 0 && guard++ < 100)
        {
            if (currentParentId == folderId) return true;

            currentParentId = await _db.LetterBayeganis
                .Where(x => x.BayeganiId == currentParentId && x.IsFolder && !x.IsDelete && x.TypeBayegani == type)
                .Select(x => x.ParentId)
                .FirstOrDefaultAsync();
        }

        return false;
    }

    private static BayeganiNodeDto Map(LetterBayegani e) => new()
    {
        BayeganiId = e.BayeganiId,
        Title = e.Title,
        ParentId = e.ParentId,
        TypeBayegani = e.TypeBayegani,
        IsFolder = e.IsFolder,
        ErjaId = e.ErjaId,
        LetterId = e.LetterId,
        SourceType = e.TypeBayegani == TypeOutgoing ? 2 : 1
    };

    // ==================== درخت ====================

    public async Task<List<BayeganiNodeDto>> GetTreeAsync(int userId, Paging.Request? pagination = null)
    {
        // مهاجرت تنبل: نامه‌هایی که با toggle قدیمی بایگانی شده‌اند ولی رکورد درختی ندارند.
        // این مهاجرت برای «هر سه نوع نامه» اجرا می‌شود؛ پیش‌تر فقط نامهٔ داخلی را پوشش می‌داد و
        // نتیجه این بود که نامهٔ واردهٔ بایگانی‌شده (IsBayegani=true روی ارجاع) هیچ رکورد
        // درختی نداشت و در ریل «بایگانی» کارتابل وارده دیده نمی‌شد.
        var looseInner = await _db.Erjas
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && e.IsBayegani == true
                        && e.Source.InnerLetter != null && !e.Source.InnerLetter.IsDelete
                        && !_db.LetterBayeganis.Any(b => b.ErjaId == e.ErjaId && !b.IsDelete))
            .Select(e => new { e.ErjaId, Title = e.Source.InnerLetter!.Title })
            .ToListAsync();

        var looseIncoming = await _db.Erjas
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && e.IsBayegani == true
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && !_db.LetterBayeganis.Any(b => b.ErjaId == e.ErjaId && !b.IsDelete))
            .Select(e => new { e.ErjaId, Title = e.Source.IncomingLetter!.Title })
            .ToListAsync();

        var loose = looseInner.Concat(looseIncoming).ToList();
        if (loose.Count > 0)
        {
            foreach (var l in loose)
                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = l.Title.Length > 200 ? l.Title[..200] : l.Title,
                    ErjaId = l.ErjaId,
                    ParentId = 0,
                    UserId = userId,
                    TypeBayegani = 1,
                    IsFolder = false,
                    IsDelete = false
                });
            await _db.SaveChangesAsync();
        }

        // فقط بایگانی شخصی (نامه‌های داخلی) — نامه‌های صادرهٔ بایگانی‌شده در
        // درخت جداگانهٔ دبیرخانه (TypeBayegani=2) نمایش داده می‌شوند.
        // الگوی DatabaseTreePaging: ریشه‌ها در SQL شمارش/صفحه‌بندی می‌شوند (total = تعداد ریشه‌ها)
        // و فقط زیرشاخه‌های ریشه‌های منتخب واکشی می‌شود؛ اطلاعات نامه‌ها هم فقط برای همین گره‌ها hydrate می‌شود.
        var scope = _db.LetterBayeganis.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDelete && b.TypeBayegani != TypeOutgoing);
        IOrderedQueryable<LetterBayegani> Order(IQueryable<LetterBayegani> q) => q
            .OrderByDescending(b => b.IsFolder).ThenBy(b => b.Title).ThenBy(b => b.BayeganiId);
        var rows = await Order(scope).ReadTreePageAsync(
            Order(scope.Where(b => b.ParentId == 0 || !scope.Any(p => p.BayeganiId == b.ParentId && p.IsFolder))),
            ids => Order(scope.Where(b => ids.Contains(b.ParentId))),
            b => b.BayeganiId, pagination);

        // اطلاعات نامه برای برگ‌ها
        var erjaIds = rows.Where(r => r.ErjaId.HasValue).Select(r => r.ErjaId!.Value).ToList();
        var letterInfo = await _db.Erjas.AsNoTracking()
            .Where(e => erjaIds.Contains(e.ErjaId) && e.Source.InnerLetter != null)
            .Select(e => new
            {
                e.ErjaId,
                LetterId = e.SourceId,
                LetterNumber = e.Source.InnerLetter!.LetterNumber ?? "",
                LetterTitle = e.Source.InnerLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                e.Date,
                e.Source.InnerLetter!.Foriat,
                e.Source.InnerLetter!.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();
        var infoByErja = letterInfo.ToDictionary(x => x.ErjaId);
        var incomingInfo = await _db.Erjas.AsNoTracking()
            .Where(e => erjaIds.Contains(e.ErjaId) && e.Source.IncomingLetter != null)
            .Select(e => new
            {
                e.ErjaId,
                LetterId = e.SourceId,
                LetterNumber = e.Source.IncomingLetter!.LetterNumber ?? "",
                LetterTitle = e.Source.IncomingLetter!.Title,
                Sender = e.Source.IncomingLetter!.Ferestande,
                e.Source.IncomingLetter!.Date,
                e.Source.IncomingLetter!.Foriat,
                e.Source.IncomingLetter!.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId)
            }).ToListAsync();
        var infoByIncomingErja = incomingInfo.ToDictionary(x => x.ErjaId);

        // اطلاعات نامه‌های ارسالی بایگانی‌شده (مسیر فرستنده)
        var sentIds = rows.Where(r => r.LetterId.HasValue).Select(r => r.LetterId!.Value).ToList();
        var sentInfo = await _db.InnerLetters.AsNoTracking()
            .Where(l => sentIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                LetterNumber = l.LetterNumber ?? "",
                LetterTitle = l.Title,
                Recivers = l.Source.Erjas.Where(e => !e.IsDelete).Select(e =>
                    string.IsNullOrEmpty(e.UserReciver!.FirstName + e.UserReciver.LastName)
                        ? e.UserReciver.Username
                        : (e.UserReciver.FirstName + " " + e.UserReciver.LastName).Trim()).ToList(),
                l.DateSabt,
                l.Foriat,
                l.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == l.Id)
            })
            .ToListAsync();
        var infoByLetter = sentInfo.ToDictionary(x => x.Id);
        var outgoingPersonalInfo = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => sentIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                LetterNumber = l.LetterNumber ?? "",
                LetterTitle = l.Title,
                l.DateSabt,
                l.Foriat,
                l.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id),
                Recivers = l.Source.Erjas.Where(e => !e.IsDelete).Select(e => e.UserReciver!.Username).ToList()
            }).ToListAsync();
        var infoByOutgoing = outgoingPersonalInfo.ToDictionary(x => x.Id);

        var nodes = new Dictionary<int, BayeganiNodeDto>();
        foreach (var r in rows)
        {
            var n = Map(r);
            if (r.ErjaId is { } eid && infoByErja.TryGetValue(eid, out var li))
            {
                n.LetterId = li.LetterId;
                n.LetterNumber = li.LetterNumber;
                n.Title = li.LetterTitle; // عنوان زنده نامه
                n.Sender = li.Sender;
                n.Date = li.Date;
                n.Foriat = li.Foriat;
                n.Mahramanegi = li.Mahramanegi;
                n.HasAttachment = li.HasAttachment;
            }
            else if (r.ErjaId is { } incomingErjaId && infoByIncomingErja.TryGetValue(incomingErjaId, out var ii))
            {
                n.SourceType = 3;
                n.LetterId = ii.LetterId;
                n.LetterNumber = ii.LetterNumber;
                n.Title = ii.LetterTitle;
                n.Sender = ii.Sender;
                n.Date = ii.Date;
                n.Foriat = ii.Foriat.ToString();
                n.Mahramanegi = ii.Mahramanegi.ToString();
                n.HasAttachment = ii.HasAttachment;
            }
            else if (r.LetterId is { } lid && infoByLetter.TryGetValue(lid, out var si))
            {
                n.LetterId = lid;
                n.LetterNumber = si.LetterNumber;
                n.Title = si.LetterTitle;
                n.Sender = "به: " + string.Join("، ", si.Recivers.Take(2)) + (si.Recivers.Count > 2 ? " و…" : "");
                n.Date = si.DateSabt;
                n.Foriat = si.Foriat;
                n.Mahramanegi = si.Mahramanegi;
                n.HasAttachment = si.HasAttachment;
            }
            else if (r.LetterId is { } outgoingId && infoByOutgoing.TryGetValue(outgoingId, out var oi))
            {
                n.LetterId = outgoingId;
                n.LetterNumber = oi.LetterNumber;
                n.Title = oi.LetterTitle;
                n.Sender = "به: " + string.Join("، ", oi.Recivers.Take(2)) + (oi.Recivers.Count > 2 ? " و…" : "");
                n.Date = oi.DateSabt;
                n.Foriat = oi.Foriat;
                n.Mahramanegi = oi.Mahramanegi;
                n.HasAttachment = oi.HasAttachment;
            }
            nodes[n.BayeganiId] = n;
        }

        var roots = new List<BayeganiNodeDto>();
        foreach (var n in nodes.Values)
        {
            if (n.ParentId != 0 && nodes.TryGetValue(n.ParentId, out var parent) && parent.IsFolder)
                parent.Children.Add(n);
            else
                roots.Add(n); // ریشه یا والدِ ازدست‌رفته
        }

        SortTree(roots);
        return roots;
    }

    private static void SortTree(List<BayeganiNodeDto> list)
    {
        list.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1; // پوشه‌ها اول
            if (a.IsFolder) return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
            return Comparer<DateTime?>.Default.Compare(b.Date, a.Date); // نامه‌ها: جدیدترین اول
        });
        foreach (var n in list) SortTree(n.Children);
    }

    // ==================== پوشه‌ها ====================

    public async Task<BayeganiNodeDto> AddMainCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId != 0)
            throw new Exception("دسته اصلی باید در ریشه ساخته شود.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            IsFolder = true,
            UserId = userId,
            SematId = null, // فاز چارت سازمانی
            ParentId = 0,
            TypeBayegani = dto.TypeBayegani > 0 ? dto.TypeBayegani : 1,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    public async Task<BayeganiNodeDto> AddSubCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId <= 0)
            throw new Exception("شناسه پوشه والد نامعتبر است.");

        if (!await FolderExistsAsync(dto.ParentId, userId))
            throw new Exception("پوشه والد یافت نشد.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            IsFolder = true,
            UserId = userId,
            SematId = null,
            ParentId = dto.ParentId,
            TypeBayegani = dto.TypeBayegani > 0 ? dto.TypeBayegani : 1,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    public async Task<BayeganiNodeDto> EditFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        var entity = await GetOwnedAsync(bayeganiId, userId);

        if (!entity.IsFolder)
            throw new Exception("رکورد انتخاب‌شده پوشه نیست.");

        // پوشه‌های بایگانی دبیرخانه فقط با متدهای مخصوص خودشان ویرایش می‌شوند
        if (entity.TypeBayegani == TypeOutgoing)
            throw new Exception("این پوشه متعلق به بایگانی دبیرخانه است.");

        // فقط مشخصات تغییر می‌کند — ParentId فقط توسط MoveFolder تغییر می‌کند.
        entity.Title = title;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    public async Task<BayeganiNodeDto> MoveFolderAsync(int bayeganiId, int newParentId, int userId)
    {
        if (bayeganiId == newParentId)
            throw new Exception("پوشه نمی‌تواند داخل خودش قرار بگیرد.");

        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (!entity.IsFolder)
            throw new Exception("رکورد انتخاب‌شده پوشه نیست.");
        if (entity.TypeBayegani == TypeOutgoing)
            throw new Exception("این پوشه متعلق به بایگانی دبیرخانه است.");

        if (newParentId != 0)
        {
            if (!await FolderExistsAsync(newParentId, userId))
                throw new Exception("پوشه مقصد یافت نشد.");

            if (await IsDescendantAsync(bayeganiId, newParentId))
                throw new Exception("پوشه نمی‌تواند داخل یکی از زیرپوشه‌های خودش قرار بگیرد.");
        }

        entity.ParentId = newParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    // ==================== نامه‌ها ====================

    public async Task AddLettersToArchiveAsync(int userId, ArchiveLettersDto dto)
    {
        var erjaIds = (dto.ErjaIds ?? new()).Where(x => x > 0).Distinct().ToList();
        var letterIds = (dto.LetterIds ?? new()).Where(x => x > 0).Distinct().ToList();
        var outgoingLetterIds = (dto.OutgoingLetterIds ?? new()).Where(x => x > 0).Distinct().ToList();

        if (erjaIds.Count == 0 && letterIds.Count == 0 && outgoingLetterIds.Count == 0)
            throw new Exception("حداقل یک نامه برای بایگانی انتخاب کنید.");

        if (!await FolderExistsAsync(dto.FolderId, userId))
            throw new Exception("پوشه مقصد یافت نشد.");

        // ---------- نامه‌های دریافتی (ارجاع) ----------
        if (erjaIds.Count > 0)
        {
            // نامه‌هایی که قبلاً بایگانی شده‌اند
            var archivedIds = await _db.LetterBayeganis
                .Where(x => x.ErjaId.HasValue && erjaIds.Contains(x.ErjaId.Value) && !x.IsDelete)
                .Select(x => x.ErjaId!.Value)
                .ToListAsync();

            if (archivedIds.Count > 0)
                throw new Exception("برخی از نامه‌ها قبلاً بایگانی شده‌اند.");

            // دریافت ارجاع‌ها — فقط گیرنده می‌تواند نامه خودش را بایگانی کند
            var erjas = await _db.Erjas
                .Include(e => e.Source).ThenInclude(s => s!.InnerLetter)
                .Include(e => e.Source).ThenInclude(s => s!.IncomingLetter)
                .Where(x => erjaIds.Contains(x.ErjaId) && !x.IsDelete)
                .ToListAsync();

            var missing = erjaIds.Except(erjas.Select(x => x.ErjaId)).ToList();
            if (missing.Count > 0)
                throw new Exception("برخی از نامه‌ها یافت نشدند.");

            if (erjas.Any(e => e.ReciverUserId != userId))
                throw new Exception("فقط گیرنده می‌تواند نامه را بایگانی کند.");

            foreach (var erja in erjas)
            {
                var title = string.IsNullOrWhiteSpace(dto.Title)
                    ? (erja.Source?.InnerLetter?.Title ?? erja.Source?.IncomingLetter?.Title ?? "نامه")
                    : dto.Title.Trim();

                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = title.Length > 200 ? title[..200] : title,
                    ErjaId = erja.ErjaId,
                    IsFolder = false,
                    UserId = userId,
                    SematId = null,
                    ParentId = dto.FolderId,
                    TypeBayegani = 1,
                    IsDelete = false
                });

                // تغییر وضعیت نامه
                erja.IsBayegani = true;
            }
        }

        // ---------- نامه‌های ارسالی (فرستنده — بدون ارجاع) ----------
        if (letterIds.Count > 0)
        {
            var already = await _db.LetterBayeganis
                .Where(x => x.LetterId.HasValue && letterIds.Contains(x.LetterId.Value)
                            && x.UserId == userId && !x.IsDelete)
                .Select(x => x.LetterId!.Value)
                .ToListAsync();

            if (already.Count > 0)
                throw new Exception("برخی از نامه‌ها قبلاً بایگانی شده‌اند.");

            var letters = await _db.InnerLetters
                .Where(l => letterIds.Contains(l.Id) && !l.IsDelete)
                .ToListAsync();

            if (letters.Count != letterIds.Count)
                throw new Exception("برخی از نامه‌ها یافت نشدند.");

            if (letters.Any(l => l.CreatorUserId != userId))
                throw new Exception("فقط فرستنده می‌تواند نامه ارسالی خود را بایگانی کند.");

            foreach (var letter in letters)
            {
                var title = string.IsNullOrWhiteSpace(dto.Title) ? letter.Title : dto.Title.Trim();

                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = title.Length > 200 ? title[..200] : title,
                    ErjaId = null,
                    LetterId = letter.Id,
                    IsFolder = false,
                    UserId = userId,
                    SematId = null,
                    ParentId = dto.FolderId,
                    TypeBayegani = 1,
                    IsDelete = false
                });
            }
        }

        if (outgoingLetterIds.Count > 0)
        {
            var alreadyOutgoing = await _db.LetterBayeganis
                .Where(x => x.LetterId.HasValue && outgoingLetterIds.Contains(x.LetterId.Value)
                            && x.UserId == userId && x.TypeBayegani == TypeInner && !x.IsDelete)
                .Select(x => x.LetterId!.Value).ToListAsync();
            if (alreadyOutgoing.Count > 0)
                throw new Exception("برخی از نامه‌های صادره قبلاً در بایگانی داخلی هستند.");

            var outgoingLetters = await _db.OutgoingLetters
                .Where(x => outgoingLetterIds.Contains(x.Id) && !x.IsDelete && !x.Source.IsDelete)
                .ToListAsync();
            if (outgoingLetters.Count != outgoingLetterIds.Count)
                throw new Exception("برخی از نامه‌های صادره یافت نشدند.");
            if (outgoingLetters.Any(x => x.CreatorUserId != userId))
                throw new Exception("فقط فرستنده می‌تواند نامه صادره خود را در بایگانی داخلی ذخیره کند.");

            foreach (var letter in outgoingLetters)
            {
                var title = string.IsNullOrWhiteSpace(dto.Title) ? letter.Title : dto.Title.Trim();
                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = title.Length > 200 ? title[..200] : title,
                    LetterId = letter.Id, ParentId = dto.FolderId, UserId = userId,
                    TypeBayegani = TypeInner, IsFolder = false, IsDelete = false
                });
            }
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task UnarchiveByLetterAsync(int letterId, int userId)
    {
        var nodes = await _db.LetterBayeganis
            .Where(x => x.LetterId == letterId && !x.IsDelete && x.UserId == userId && !x.IsFolder)
            .ToListAsync();

        if (nodes.Count == 0)
        {
            // بایگانیِ فقط با پرچم (بدون گره درخت): خودِ پرچم نامه آزاد می‌شود
            var incoming = await _db.IncomingLetters
                .FirstOrDefaultAsync(l => l.Id == letterId && l.CreateUserId == userId);
            var outgoing = incoming == null
                ? await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && l.CreatorUserId == userId)
                : null;

            if (incoming == null && outgoing == null) throw new Exception("این نامه در بایگانی شما نیست.");
            if (incoming != null) incoming.IsBayegani = false;
            if (outgoing != null) outgoing.IsArchived = false;
        }

        foreach (var node in nodes)
        {
            node.IsDelete = true;
            await ClearArchiveFlagsAsync(node, userId);
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task<BayeganiNodeDto> MoveLetterAsync(int bayeganiId, int newParentId, int userId)
    {
        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (entity.IsFolder)
            throw new Exception("رکورد انتخاب‌شده نامه نیست.");
        if (entity.TypeBayegani == TypeOutgoing)
            throw new Exception("این نامه متعلق به بایگانی دبیرخانه است.");

        if (!await FolderExistsAsync(newParentId, userId))
            throw new Exception("پوشه مقصد یافت نشد.");

        entity.ParentId = newParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    // ==================== حذف ====================

    /// <summary>شناسهٔ همهٔ گره‌های زیرِ یک پوشه (تا هر عمقی) — برای حذف آبشاری پوشه.</summary>
    private async Task<HashSet<int>> CollectSubtreeIdsAsync(int folderId, int userId)
    {
        var rows = await _db.LetterBayeganis.AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDelete && x.TypeBayegani != TypeOutgoing)
            .Select(x => new { x.BayeganiId, x.ParentId })
            .ToListAsync();

        var result = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(folderId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in rows.Where(x => x.ParentId == current))
            {
                if (result.Add(child.BayeganiId)) queue.Enqueue(child.BayeganiId);
            }
        }
        return result;
    }

    /// <summary>
    /// آزادکردن پرچم‌های بایگانیِ مرتبط با یک گره درخت.
    /// <para>
    /// نامهٔ وارده دو پرچم مستقل دارد: <c>Erja.IsBayegani</c> (سمت گیرنده) و
    /// <c>IncomingLetter.IsBayegani</c> (سمت ثبت‌کننده/دبیرخانه)؛ نامهٔ صادرهٔ شخصی هم
    /// <c>OutgoingLetter.IsArchived</c> دارد. پیش‌تر فقط پرچم ارجاع آزاد می‌شد و نتیجه این بود
    /// که نامه از درخت بایگانی بیرون می‌رفت ولی به «دریافتی/ارسالی» هم برنمی‌گشت و گم می‌شد.
    /// </para>
    /// </summary>
    private async Task ClearArchiveFlagsAsync(LetterBayegani node, int userId)
    {
        if (node.ErjaId is { } erjaId)
        {
            var erja = await _db.Erjas.FirstOrDefaultAsync(x => x.ErjaId == erjaId);
            if (erja != null)
            {
                erja.IsBayegani = false;

                // نامه ممکن است چند ارجاع برای همین کاربر داشته باشد (ارجاع مجدد). اگر
                // فقط ارجاعِ متصل به گره آزاد شود، ردیفِ دیگر با پرچم بایگانی، نامه را
                // همچنان از «دریافتی» بیرون نگه می‌دارد؛ پس همهٔ ردیف‌های همین کاربر
                // روی همین نامه آزاد می‌شوند.
                var siblings = await _db.Erjas
                    .Where(e => e.SourceId == erja.SourceId && e.ReciverUserId == userId && e.IsBayegani == true)
                    .ToListAsync();
                foreach (var sibling in siblings) sibling.IsBayegani = false;

                // ثبت‌کنندهٔ همان نامهٔ وارده اگر خودِ کاربر باشد، پرچم خود نامه هم آزاد شود
                var ownIncoming = await _db.IncomingLetters
                    .FirstOrDefaultAsync(l => l.Id == erja.SourceId && l.CreateUserId == userId);
                if (ownIncoming != null) ownIncoming.IsBayegani = false;
            }
        }

        if (node.LetterId is { } letterId)
        {
            // یک شناسه در سه جدول معنی دارد؛ فقط رکوردی که مالکش همین کاربر است و واقعاً
            // بایگانی است آزاد می‌شود تا نامهٔ بی‌ربط از بایگانی خارج نشود.
            var incoming = await _db.IncomingLetters
                .FirstOrDefaultAsync(l => l.Id == letterId && l.CreateUserId == userId && l.IsBayegani);
            if (incoming != null) incoming.IsBayegani = false;

            var outgoing = await _db.OutgoingLetters
                .FirstOrDefaultAsync(l => l.Id == letterId && l.CreatorUserId == userId && l.IsArchived);
            if (outgoing != null) outgoing.IsArchived = false;
        }
    }

    public async Task DeleteAsync(int bayeganiId, int userId)
    {
        var entity = await GetOwnedAsync(bayeganiId, userId);

        // گره‌های بایگانی دبیرخانه مسیر حذف خودشان را دارند (آزاد کردن پرچم IsArchived)
        if (entity.TypeBayegani == TypeOutgoing)
        {
            await DeleteOutgoingAsync(bayeganiId, userId);
            return;
        }

        if (entity.IsFolder)
        {
            // حذف پوشه آبشاری است: همان چیزی که پیام تأیید هم می‌گوید («نامه‌های داخل آن از
            // بایگانی خارج می‌شوند»). مهم است که «همهٔ» نسل‌های زیرپوشه‌ها هم بسته شوند؛
            // وگرنه نامهٔ داخل زیرپوشه نه در بایگانی می‌ماند (گره حذف شده) و نه به
            // «دریافتی» برمی‌گردد (پرچم بایگانی هنوز true است) — همان باگ «نامه گم می‌شود».
            var ids = await CollectSubtreeIdsAsync(bayeganiId, userId);
            ids.Add(bayeganiId);

            var nodes = await _db.LetterBayeganis
                .Where(x => ids.Contains(x.BayeganiId) && x.UserId == userId && !x.IsDelete)
                .ToListAsync();

            foreach (var node in nodes)
            {
                if (!node.IsFolder) await ClearArchiveFlagsAsync(node, userId);
                node.IsDelete = true;
            }

            await _db.SaveChangesAsync();
            await _notify.BroadcastChangedAsync("letters");
            return;
        }

        // نامه: soft delete + آزادسازی همهٔ پرچم‌های بایگانیِ مربوط به آن
        entity.IsDelete = true;
        await ClearArchiveFlagsAsync(entity, userId);

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task UnarchiveByErjaAsync(int erjaId, int userId)
    {
        var node = await _db.LetterBayeganis
            .FirstOrDefaultAsync(x => x.ErjaId == erjaId && !x.IsDelete && x.UserId == userId);

        if (node != null)
        {
            await DeleteAsync(node.BayeganiId, userId);
            return;
        }

        // حالت قدیمی: فقط پرچم ارجاع
        var erja = await _db.Erjas.SingleOrDefaultAsync(x => x.ErjaId == erjaId && !x.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده می‌تواند نامه را از بایگانی خارج کند.");

        erja.IsBayegani = false;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    /// <summary>
    /// خروج نامهٔ وارده از بایگانی به‌صورت «کامل»: افزون بر گره درختی، پرچم بایگانیِ
    /// خود نامه و همهٔ ارجاع‌های همین کاربر روی این نامه آزاد می‌شود.
    /// <para>
    /// چرا لازم است: یک نامهٔ وارده می‌تواند هم‌زمان در ردیفِ ارجاعِ کاربر و در ردیفِ
    /// «ثبت‌کننده» بایگانی شود؛ اگر فقط یکی از پرچم‌ها آزاد شود، نامه از بایگانی حذف
    /// می‌شود ولی با پرچمِ دیگر همچنان از «دریافتی» بیرون می‌ماند و در عمل گم می‌شود.
    /// </para>
    /// </summary>
    public async Task<int> UnarchiveIncomingLetterAsync(int letterId, int userId)
    {
        var erjaIds = await _db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letterId && e.ReciverUserId == userId)
            .Select(e => e.ErjaId)
            .ToListAsync();

        // ۱) همهٔ گره‌های درختیِ همین کاربر برای همین نامه (بر اساس شناسهٔ نامه یا ارجاع)
        var nodes = await _db.LetterBayeganis
            .Where(b => b.UserId == userId && !b.IsDelete && !b.IsFolder &&
                        (b.LetterId == letterId ||
                         (b.ErjaId != null && erjaIds.Contains(b.ErjaId.Value))))
            .ToListAsync();

        foreach (var node in nodes)
        {
            node.IsDelete = true;
            await ClearArchiveFlagsAsync(node, userId);
        }

        // ۲) پرچم همهٔ ارجاع‌های همین کاربر روی این نامه
        var erjas = await _db.Erjas
            .Where(e => erjaIds.Contains(e.ErjaId))
            .ToListAsync();
        foreach (var erja in erjas) erja.IsBayegani = false;

        // ۳) پرچم خود نامه (فقط اگر کاربر ثبت‌کنندهٔ آن باشد)
        var letter = await _db.IncomingLetters
            .FirstOrDefaultAsync(l => l.Id == letterId && l.CreateUserId == userId);
        if (letter != null) letter.IsBayegani = false;

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return nodes.Count;
    }

    // ============================================================
    //  بایگانی دبیرخانه — نامه‌های صادره (TypeBayegani = 2)
    //  درخت پوشه‌ها مستقل از بایگانی شخصی نامه‌های داخلی است تا
    //  دبیرخانه بتواند نامه‌های ارسال‌شده را دسته‌بندی کند.
    // ============================================================

    private static BayeganiNodeDto MapOutgoing(LetterBayegani e) => new()
    {
        BayeganiId = e.BayeganiId,
        Title = e.Title,
        ParentId = e.ParentId,
        TypeBayegani = e.TypeBayegani,
        IsFolder = e.IsFolder,
        ErjaId = e.ErjaId,
        LetterId = e.LetterId,
        SourceType = 2
    };

    public async Task<List<BayeganiNodeDto>> GetOutgoingTreeAsync(int userId, Paging.Request? pagination = null)
    {
        // الگوی DatabaseTreePaging: ریشه‌ها در SQL شمارش/صفحه‌بندی می‌شوند (total = تعداد ریشه‌ها)
        // و فقط زیرشاخه‌های ریشه‌های منتخب واکشی می‌شود؛ اطلاعات نامه‌ها هم فقط برای همین گره‌ها hydrate می‌شود.
        var scope = _db.LetterBayeganis.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDelete && b.TypeBayegani == TypeOutgoing);
        IOrderedQueryable<LetterBayegani> Order(IQueryable<LetterBayegani> q) => q
            .OrderByDescending(b => b.IsFolder).ThenBy(b => b.Title).ThenBy(b => b.BayeganiId);
        var rows = await Order(scope).ReadTreePageAsync(
            Order(scope.Where(b => b.ParentId == 0 || !scope.Any(p => p.BayeganiId == b.ParentId && p.IsFolder))),
            ids => Order(scope.Where(b => ids.Contains(b.ParentId))),
            b => b.BayeganiId, pagination);

        // اطلاعات نامه‌های صادرهٔ بایگانی‌شده
        var letterIds = rows.Where(r => r.LetterId.HasValue).Select(r => r.LetterId!.Value).Distinct().ToList();

        var info = new Dictionary<int, BayeganiNodeDto>();
        if (letterIds.Count > 0)
        {
            var letters = await _db.OutgoingLetters.AsNoTracking()
                .Where(l => letterIds.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.Title,
                    l.LetterNumber,
                    l.SadereNumber,
                    l.ReceiverOrganization,
                    l.DateSabt,
                    l.DateSadere,
                    l.Foriat,
                    l.Mahramanegi,
                    l.SendMethod,
                    Sender = l.Creator != null
                        ? (string.IsNullOrEmpty(l.Creator.FirstName + l.Creator.LastName)
                            ? l.Creator.Username
                            : (l.Creator.FirstName + " " + l.Creator.LastName).Trim())
                        : "",
                    HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id)
                })
                .ToListAsync();

            foreach (var l in letters)
            {
                info[l.Id] = new BayeganiNodeDto
                {
                    LetterId = l.Id,
                    LetterNumber = l.SadereNumber ?? l.LetterNumber ?? "",
                    Title = l.Title,
                    Sender = l.Sender,
                    Date = l.DateSadere ?? l.DateSabt,
                    Foriat = l.Foriat,
                    Mahramanegi = l.Mahramanegi,
                    HasAttachment = l.HasAttachment,
                    ReceiverOrganization = l.ReceiverOrganization,
                    SadereNumber = l.SadereNumber,
                    SendMethod = l.SendMethod,
                    SourceType = 2
                };
            }
        }

        var nodes = new Dictionary<int, BayeganiNodeDto>();
        foreach (var r in rows)
        {
            var n = MapOutgoing(r);
            if (r.LetterId is { } lid && info.TryGetValue(lid, out var li))
            {
                n.LetterNumber = li.LetterNumber;
                n.Title = li.Title;                 // عنوان زندهٔ نامه
                n.Sender = li.Sender;
                n.Date = li.Date;
                n.Foriat = li.Foriat;
                n.Mahramanegi = li.Mahramanegi;
                n.HasAttachment = li.HasAttachment;
                n.ReceiverOrganization = li.ReceiverOrganization;
                n.SadereNumber = li.SadereNumber;
                n.SendMethod = li.SendMethod;
            }
            nodes[n.BayeganiId] = n;
        }

        var roots = new List<BayeganiNodeDto>();
        foreach (var n in nodes.Values)
        {
            if (n.ParentId != 0 && nodes.TryGetValue(n.ParentId, out var parent) && parent.IsFolder)
                parent.Children.Add(n);
            else
                roots.Add(n);
        }

        SortTree(roots);
        return roots;
    }

    public async Task<BayeganiNodeDto> AddOutgoingMainCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId != 0)
            throw new Exception("دسته اصلی باید در ریشه ساخته شود.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            LetterId = null,
            IsFolder = true,
            UserId = userId,
            ParentId = 0,
            TypeBayegani = TypeOutgoing,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    public async Task<BayeganiNodeDto> AddOutgoingSubCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId <= 0)
            throw new Exception("شناسه پوشه والد نامعتبر است.");
        if (!await FolderExistsAsync(dto.ParentId, userId, TypeOutgoing))
            throw new Exception("پوشه والد یافت نشد.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            LetterId = null,
            IsFolder = true,
            UserId = userId,
            ParentId = dto.ParentId,
            TypeBayegani = TypeOutgoing,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    public async Task<BayeganiNodeDto> EditOutgoingFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        var entity = await GetOwnedAsync(bayeganiId, userId);

        if (!entity.IsFolder) throw new Exception("رکورد انتخاب‌شده پوشه نیست.");
        if (entity.TypeBayegani != TypeOutgoing) throw new Exception("این پوشه متعلق به بایگانی دبیرخانه نیست.");

        entity.Title = title;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    public async Task<BayeganiNodeDto> MoveOutgoingFolderAsync(int bayeganiId, int newParentId, int userId)
    {
        if (bayeganiId == newParentId)
            throw new Exception("پوشه نمی‌تواند داخل خودش قرار بگیرد.");

        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (!entity.IsFolder) throw new Exception("رکورد انتخاب‌شده پوشه نیست.");
        if (entity.TypeBayegani != TypeOutgoing) throw new Exception("این پوشه متعلق به بایگانی دبیرخانه نیست.");

        if (newParentId != 0)
        {
            if (!await FolderExistsAsync(newParentId, userId, TypeOutgoing))
                throw new Exception("پوشه مقصد یافت نشد.");
            if (await IsDescendantAsync(bayeganiId, newParentId, TypeOutgoing))
                throw new Exception("پوشه نمی‌تواند داخل یکی از زیرپوشه‌های خودش قرار بگیرد.");
        }

        entity.ParentId = newParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    /// <summary>بایگانی یک یا چند نامه صادره در پوشهٔ انتخابی دبیرخانه</summary>
    public async Task ArchiveOutgoingLettersAsync(int userId, ArchiveOutgoingLettersDto dto)
    {
        var letterIds = (dto.LetterIds ?? new()).Where(x => x > 0).Distinct().ToList();
        if (letterIds.Count == 0)
            throw new Exception("حداقل یک نامه برای بایگانی انتخاب کنید.");

        // پوشه مقصد — FolderId=0 یعنی ریشهٔ بایگانی دبیرخانه
        if (dto.FolderId != 0 && !await FolderExistsAsync(dto.FolderId, userId, TypeOutgoing))
            throw new Exception("پوشه مقصد یافت نشد.");

        var already = await _db.LetterBayeganis
            .Where(x => x.LetterId.HasValue && letterIds.Contains(x.LetterId.Value)
                        && x.UserId == userId && x.TypeBayegani == TypeOutgoing && !x.IsDelete)
            .Select(x => x.LetterId!.Value)
            .ToListAsync();

        if (already.Count > 0)
            throw new Exception("برخی از نامه‌ها قبلاً بایگانی شده‌اند.");

        var letters = await _db.OutgoingLetters
            .Where(l => letterIds.Contains(l.Id) && !l.IsDelete)
            .ToListAsync();

        if (letters.Count != letterIds.Count)
            throw new Exception("برخی از نامه‌ها یافت نشدند.");

        // نامه صادره فقط بعد از امضا (دارا بودن شماره صادره) بایگانی می‌شود
        var notSigned = letters.Where(l => string.IsNullOrWhiteSpace(l.SadereNumber)).ToList();
        if (notSigned.Count > 0)
            throw new Exception("فقط نامه‌های امضا شده (دارای شماره صادره) قابل بایگانی در دبیرخانه هستند.");

        foreach (var letter in letters)
        {
            var title = string.IsNullOrWhiteSpace(dto.Title) ? letter.Title : dto.Title.Trim();

            _db.LetterBayeganis.Add(new LetterBayegani
            {
                Title = title.Length > 200 ? title[..200] : title,
                ErjaId = null,
                LetterId = letter.Id,
                IsFolder = false,
                UserId = userId,
                ParentId = dto.FolderId,
                TypeBayegani = TypeOutgoing,
                IsDelete = false
            });

            letter.IsArchived = true;
            letter.ArchivedAt = DateTime.Now;
            letter.ArchivedByUserId = userId;
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task UnarchiveOutgoingLetterAsync(int letterId, int userId)
    {
        var node = await _db.LetterBayeganis
            .FirstOrDefaultAsync(x => x.LetterId == letterId && x.UserId == userId
                                   && x.TypeBayegani == TypeOutgoing && !x.IsDelete)
            ?? throw new Exception("این نامه در بایگانی دبیرخانه شما نیست.");

        node.IsDelete = true;

        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId);
        if (letter != null)
        {
            letter.IsArchived = false;
            letter.ArchivedAt = null;
            letter.ArchivedByUserId = null;
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task<BayeganiNodeDto> MoveArchivedLetterAsync(MoveArchivedLetterDto dto, int userId)
    {
        var entity = await GetOwnedAsync(dto.BayeganiId, userId);
        if (entity.IsFolder) throw new Exception("رکورد انتخاب‌شده نامه نیست.");
        if (entity.TypeBayegani != TypeOutgoing) throw new Exception("این نامه متعلق به بایگانی دبیرخانه نیست.");
        if (!await FolderExistsAsync(dto.NewParentId, userId, TypeOutgoing))
            throw new Exception("پوشه مقصد یافت نشد.");

        entity.ParentId = dto.NewParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    /// <summary>
    /// حذف از بایگانی دبیرخانه — پوشهٔ خالی حذف می‌شود؛ نامه از بایگانی خارج
    /// می‌شود (Soft Delete روی گره + آزاد شدن پرچم IsArchived روی نامه)
    /// </summary>
    public async Task DeleteOutgoingAsync(int bayeganiId, int userId)
    {
        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (entity.TypeBayegani != TypeOutgoing)
            throw new Exception("این مورد متعلق به بایگانی دبیرخانه نیست.");

        if (entity.IsFolder)
        {
            var hasChildren = await _db.LetterBayeganis
                .AnyAsync(x => x.ParentId == bayeganiId && !x.IsDelete);

            if (hasChildren)
                throw new Exception("این پوشه دارای محتوا یا زیرپوشه است. ابتدا محتویات پوشه را حذف یا منتقل کنید.");
        }
        else if (entity.LetterId is { } lid)
        {
            var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == lid);
            if (letter != null)
            {
                letter.IsArchived = false;
                letter.ArchivedAt = null;
                letter.ArchivedByUserId = null;
            }
        }

        entity.IsDelete = true;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }
}
