using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Export;

/// <summary>
/// گزارش‌های خروجی ماژول آرشیو اسناد.
///
/// ⚠ نکته امنیتی: برخلاف گزارش‌های دیگر، خروجی این ماژول باید بر اساس
/// دسترسی کاربرِ درخواست‌کننده فیلتر شود؛ وگرنه Export به راه فرار از
/// سیستم دسترسی چندسطحی تبدیل می‌شود. به همین دلیل شناسه کاربر به‌صورت
/// پارامتر صریح گرفته می‌شود و هرگز از query string خوانده نمی‌شود.
/// </summary>
public interface IDocArchiveExportService
{
    Task<ExportSpec> BuildAsync(string key, ExportQuery q, int userId, bool isManager);
}

public class DocArchiveExportService : IDocArchiveExportService
{
    private readonly AppDbContext _db;
    private readonly IDocAccessService _access;

    public DocArchiveExportService(AppDbContext db, IDocAccessService access)
    {
        _db = db;
        _access = access;
    }

    public Task<ExportSpec> BuildAsync(string key, ExportQuery q, int userId, bool isManager)
        => key.ToLowerInvariant() switch
        {
            "doc-list" => DocListAsync(q, userId, isManager),
            "doc-expiring" => ExpiringAsync(q, userId, isManager),
            "doc-history" => HistoryAsync(q, userId, isManager),
            "doc-permissions" => PermissionsAsync(q, userId, isManager),
            "doc-pending" => PendingAsync(q, userId, isManager),
            _ => throw new InvalidOperationException($"گزارش «{key}» پیاده‌سازی نشده است.")
        };

    // ------------------------------------------------------------------
    // مدارکی که این کاربر اجازه دیدنشان را دارد
    // ------------------------------------------------------------------
    private async Task<List<ArchiveDocument>> VisibleDocsAsync(int userId, bool isManager, string? status)
    {
        var q = await DocQuery.AccessibleAsync(_db, _access, userId, isManager, DocAccessLevel.Read);
        q = status?.ToLowerInvariant() switch
        {
            "deleted" => q.Where(d => d.IsDeleted),
            "inactive" => q.Where(d => !d.IsDeleted && !d.IsActive),
            "all" => q.Where(d => !d.IsDeleted),
            _ => q.Where(d => !d.IsDeleted && d.IsActive)
        };
        var docs = await q.OrderBy(d => d.Code).ToListAsync();
        var map = await _access.DocumentAccessMapAsync(userId, isManager, docs.Select(d => d.Id));
        var allowed = docs.Where(d => map.TryGetValue(d.Id, out var grant)
            && grant.Level >= DocAccessLevel.Read && grant.Download).ToList();
        if (docs.Count > 0 && allowed.Count == 0)
            throw new UnauthorizedAccessException("برای خروجی گرفتن از مدارک، مجوز جداگانه دانلود لازم است.");
        return allowed;
    }

    private async Task<Dictionary<int, string>> FolderNamesAsync()
        => await _db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name);

    private static string StatusName(DocVersionStatus s) => s switch
    {
        DocVersionStatus.Draft => "پیش‌نویس",
        DocVersionStatus.InReview => "در گردش تایید",
        DocVersionStatus.Approved => "تایید شده",
        DocVersionStatus.Rejected => "رد شده",
        DocVersionStatus.Archived => "بایگانی",
        _ => "—"
    };

    private static string LevelName(DocAccessLevel l) => l switch
    {
        DocAccessLevel.View => "مشاهده",
        DocAccessLevel.Read => "خواندن",
        DocAccessLevel.Write => "نوشتن",
        DocAccessLevel.Full => "کامل",
        _ => "ندارد"
    };

    // ==================================================================
    // ۱) فهرست مدارک
    // ==================================================================
    private async Task<ExportSpec> DocListAsync(ExportQuery q, int userId, bool isManager)
    {
        var docs = await VisibleDocsAsync(userId, isManager, q.Status);
        var folders = await FolderNamesAsync();

        if (q.Id is > 0) docs = docs.Where(d => d.FolderId == q.Id).ToList();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            docs = docs.Where(d =>
                d.Code.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                d.Title.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (d.CustomerCode ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var ids = docs.Select(d => d.Id).ToList();
        var vers = await _db.DocumentVersions.AsNoTracking()
            .Where(v => ids.Contains(v.DocumentId)).ToListAsync();

        var spec = new ExportSpec
        {
            Title = "فهرست مدارک آرشیو",
            Landscape = true,
            Columns =
            {
                new ExportColumn("کد مدرک", ExportValueKind.Text, 70),
                new ExportColumn("کد مشتری", ExportValueKind.Text, 62),
                new ExportColumn("عنوان") { Wrap = true },
                new ExportColumn("پوشه"),
                new ExportColumn("ورژن فعال", ExportValueKind.Int, 48),
                new ExportColumn("تعداد ورژن", ExportValueKind.Int, 48),
                new ExportColumn("وضعیت ورژن"),
                new ExportColumn("وضعیت مدرک", ExportValueKind.Text, 55),
                new ExportColumn("تاریخ انقضا", ExportValueKind.Date, 62),
                new ExportColumn("شرح") { ExcelOnly = true, Wrap = true }
            }
        };

        spec.Meta.Add(new ExportMeta("وضعیت", q.Status switch
        {
            "inactive" => "غیرفعال",
            "deleted" => "سطل بازیافت",
            "all" => "همه",
            _ => "فعال"
        }));
        if (!string.IsNullOrWhiteSpace(q.Search))
            spec.Meta.Add(new ExportMeta("جستجو", q.Search));
        if (q.Id is > 0 && folders.TryGetValue(q.Id.Value, out var fn))
            spec.Meta.Add(new ExportMeta("پوشه", fn));

        spec.Summary.Add(new ExportMeta("تعداد مدارک", docs.Count.ToString()));
        spec.Summary.Add(new ExportMeta("دارای انقضا", docs.Count(d => d.ExpireDate.HasValue).ToString()));

        foreach (var d in docs.Take(q.MaxRows))
        {
            var dv = vers.Where(v => v.DocumentId == d.Id).ToList();
            var active = dv.FirstOrDefault(v => v.IsActive);
            var last = dv.OrderByDescending(v => v.VersionNo).FirstOrDefault();

            spec.Rows.Add(new ExportRow(
                d.Code,
                d.CustomerCode,
                d.Title,
                folders.TryGetValue(d.FolderId, out var f) ? f : "—",
                active?.VersionNo,
                dv.Count,
                last is null ? "—" : StatusName(last.Status),
                d.IsDeleted ? "حذف‌شده" : d.IsActive ? "فعال" : "غیرفعال",
                d.ExpireDate,
                d.Description));
        }

        spec.Notes.Add("این گزارش فقط شامل مدارکی است که کاربر درخواست‌کننده به آن‌ها دسترسی دارد.");
        return spec;
    }

    // ==================================================================
    // ۲) مدارک رو به انقضا و منقضی‌شده
    // ==================================================================
    private async Task<ExportSpec> ExpiringAsync(ExportQuery q, int userId, bool isManager)
    {
        var days = q.Level is not null && int.TryParse(q.Level, out var dd) ? dd : 60;
        var today = DateTime.Today;
        var limit = today.AddDays(days);

        var docs = (await VisibleDocsAsync(userId, isManager, "active"))
            .Where(d => d.ExpireDate.HasValue && d.ExpireDate.Value.Date <= limit)
            .OrderBy(d => d.ExpireDate)
            .ToList();

        var folders = await FolderNamesAsync();

        var spec = new ExportSpec
        {
            Title = "مدارک منقضی‌شده و رو به انقضا",
            Subtitle = $"تا {days} روز آینده",
            Landscape = true,
            Columns =
            {
                new ExportColumn("کد مدرک", ExportValueKind.Text, 70),
                new ExportColumn("عنوان") { Wrap = true },
                new ExportColumn("پوشه"),
                new ExportColumn("تاریخ انقضا", ExportValueKind.Date, 65),
                new ExportColumn("روز باقی‌مانده", ExportValueKind.Int, 55),
                new ExportColumn("وضعیت", ExportValueKind.Text, 70),
                new ExportColumn("مسئولان دسترسی کامل") { ExcelOnly = true, Wrap = true }
            }
        };

        var expired = docs.Count(d => d.ExpireDate!.Value.Date < today);
        spec.Summary.Add(new ExportMeta("منقضی‌شده", expired.ToString()));
        spec.Summary.Add(new ExportMeta("رو به انقضا", (docs.Count - expired).ToString()));

        foreach (var d in docs.Take(q.MaxRows))
        {
            var left = (d.ExpireDate!.Value.Date - today).Days;
            var owners = await _access.UsersWithFullAccessAsync(d.Id);
            var names = await _db.Users.AsNoTracking()
                .Where(u => owners.Contains(u.Id))
                .Select(u => (u.FirstName + " " + u.LastName).Trim() == "" ? u.Username
                                                                            : u.FirstName + " " + u.LastName)
                .ToListAsync();

            spec.Rows.Add(new ExportRow(
                d.Code,
                d.Title,
                folders.TryGetValue(d.FolderId, out var f) ? f : "—",
                d.ExpireDate,
                left,
                left < 0 ? "منقضی شده" : left == 0 ? "امروز منقضی می‌شود" : "در آستانه انقضا",
                string.Join("، ", names))
            {
                Style = left < 0 ? ExportRowStyle.Danger
                      : left <= 7 ? ExportRowStyle.Danger
                      : left <= 30 ? ExportRowStyle.Subtotal
                      : ExportRowStyle.Normal
            });
        }

        spec.Notes.Add("سطرهای قرمز: منقضی‌شده یا کمتر از ۷ روز باقی‌مانده.");
        return spec;
    }

    // ==================================================================
    // ۳) تاریخچه یک مدرک — برای ممیزی
    // ==================================================================
    private async Task<ExportSpec> HistoryAsync(ExportQuery q, int userId, bool isManager)
    {
        if (q.Id is not > 0)
            throw new InvalidOperationException("برای گزارش تاریخچه، مدرک را انتخاب کنید.");

        var (lvl, download) = await _access.DocumentAccessAsync(userId, isManager, q.Id.Value);
        if (lvl < DocAccessLevel.Read || !download)
            throw new UnauthorizedAccessException("خروجی تاریخچه نیازمند خواندن و مجوز جداگانه دانلود است.");

        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == q.Id.Value)
                  ?? throw new InvalidOperationException("مدرک یافت نشد.");

        var logQuery = _db.DocumentLogs.AsNoTracking().Where(l => l.DocumentId == doc.Id);
        if (lvl < DocAccessLevel.Full)
        {
            // حق دانلود تاریخچه، مجوز مشاهده درخواست‌ها و مدیریت دسترسی دیگران نیست.
            string[] hiddenActions = { "Permissions", "AccessRequest", "AccessRequestApproved", "AccessRequestRejected", "TemporaryAccess", "TemporaryAccessRevoked", "RenewalPolicy" };
            logQuery = logQuery.Where(l => !hiddenActions.Contains(l.Action));
        }
        var logs = await logQuery.OrderBy(l => l.Id).Take(q.MaxRows).ToListAsync();

        var vers = await _db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == doc.Id)
            .ToDictionaryAsync(v => v.Id, v => v.VersionNo);

        var spec = new ExportSpec
        {
            Title = "تاریخچه مدرک",
            Subtitle = $"{doc.Code} — {doc.Title}",
            Columns =
            {
                new ExportColumn("تاریخ", ExportValueKind.DateTime, 90),
                new ExportColumn("رویداد", ExportValueKind.Text, 80),
                new ExportColumn("کاربر", ExportValueKind.Text, 80),
                new ExportColumn("ورژن", ExportValueKind.Int, 40),
                new ExportColumn("شرح") { Wrap = true }
            }
        };

        if (!string.IsNullOrWhiteSpace(doc.CustomerCode))
            spec.Meta.Add(new ExportMeta("کد مشتری", doc.CustomerCode));
        spec.Meta.Add(new ExportMeta("وضعیت", doc.IsActive ? "فعال" : "غیرفعال"));
        spec.Summary.Add(new ExportMeta("تعداد رویداد", logs.Count.ToString()));

        foreach (var l in logs)
        {
            spec.Rows.Add(new ExportRow(
                l.CreatedAt,
                ActionName(l.Action),
                l.UserName,
                l.VersionId is int vid && vers.TryGetValue(vid, out var no) ? no : (int?)null,
                l.Detail));
        }

        spec.Notes.Add("این گزارش برای ارائه به ممیز و مستندسازی ISO تهیه شده است.");
        return spec;
    }

    private static string ActionName(string a) => a switch
    {
        "Create" => "ایجاد",
        "Update" => "ویرایش",
        "NewVersion" => "ورژن جدید",
        "Approve" => "تایید",
        "Approved" => "تایید نهایی",
        "Reject" => "رد",
        "Activate" => "فعال‌سازی",
        "Link" => "لینک",
        "Unlink" => "حذف لینک",
        "RelatedUpdate" => "مدرک مرتبط",
        "Deactivate" => "غیرفعال‌سازی",
        "Permissions" => "تغییر دسترسی",
        "ExpiryAlert" => "هشدار انقضا",
        "Delete" => "حذف",
        "Restore" => "بازگردانی",
        _ => a
    };

    // ==================================================================
    // ۴) ماتریس دسترسی پوشه‌ها
    // ==================================================================
    private async Task<ExportSpec> PermissionsAsync(ExportQuery q, int userId, bool isManager)
    {
        if (!isManager)
            throw new InvalidOperationException("گزارش دسترسی‌ها فقط برای مدیر ماژول در دسترس است.");

        var folders = await _db.DocFolders.AsNoTracking().OrderBy(f => f.Name).ToListAsync();
        var perms = await _db.DocFolderPermissions.AsNoTracking().ToListAsync();
        var users = await _db.Users.AsNoTracking()
            .ToDictionaryAsync(u => u.Id,
                u => (u.FirstName + " " + u.LastName).Trim() == "" ? u.Username : u.FirstName + " " + u.LastName);

        var spec = new ExportSpec
        {
            Title = "ماتریس دسترسی پوشه‌های آرشیو",
            Columns =
            {
                new ExportColumn("پوشه") { Wrap = true },
                new ExportColumn("کاربر", ExportValueKind.Text, 110),
                new ExportColumn("سطح دسترسی", ExportValueKind.Text, 70),
                new ExportColumn("حق دانلود", ExportValueKind.Bool, 55),
                new ExportColumn("نمایش برای همه", ExportValueKind.Bool, 60)
            }
        };

        spec.Summary.Add(new ExportMeta("تعداد پوشه", folders.Count.ToString()));
        spec.Summary.Add(new ExportMeta("تعداد تخصیص", perms.Count.ToString()));

        foreach (var f in folders)
        {
            var fp = perms.Where(p => p.FolderId == f.Id).ToList();
            if (fp.Count == 0)
            {
                spec.Rows.Add(new ExportRow(f.Name, "—", "بدون تخصیص", null, f.IsPublic)
                { Style = ExportRowStyle.Muted });
                continue;
            }

            foreach (var p in fp)
            {
                spec.Rows.Add(new ExportRow(
                    f.Name,
                    users.TryGetValue(p.UserId, out var un) ? un : $"#{p.UserId}",
                    LevelName(p.Level),
                    p.CanDownload,
                    f.IsPublic));
            }
        }

        spec.Notes.Add("دسترسی پوشه به‌صورت بازگشتی به زیرپوشه‌ها و مدارک داخل آن‌ها ارث می‌رسد.");
        return spec;
    }

    // ==================================================================
    // ۵) ورژن‌های در انتظار تایید
    // ==================================================================
    private async Task<ExportSpec> PendingAsync(ExportQuery q, int userId, bool isManager)
    {
        var docs = await VisibleDocsAsync(userId, isManager, "all");
        var ids = docs.Select(d => d.Id).ToList();

        var vers = await _db.DocumentVersions.AsNoTracking()
            .Where(v => ids.Contains(v.DocumentId) && v.Status == DocVersionStatus.InReview)
            .OrderBy(v => v.CreatedAt).ToListAsync();

        var vids = vers.Select(v => v.Id).ToList();
        var apprs = await _db.DocumentApprovers.AsNoTracking()
            .Where(a => a.VersionId != null && vids.Contains(a.VersionId!.Value)).ToListAsync();

        var spec = new ExportSpec
        {
            Title = "ورژن‌های در انتظار تایید",
            Landscape = true,
            Columns =
            {
                new ExportColumn("کد مدرک", ExportValueKind.Text, 70),
                new ExportColumn("عنوان") { Wrap = true },
                new ExportColumn("ورژن", ExportValueKind.Int, 40),
                new ExportColumn("ثبت‌کننده", ExportValueKind.Text, 85),
                new ExportColumn("تاریخ ثبت", ExportValueKind.Date, 62),
                new ExportColumn("روز در انتظار", ExportValueKind.Int, 52),
                new ExportColumn("تایید شده", ExportValueKind.Text, 50),
                new ExportColumn("معطل چه کسی") { Wrap = true }
            }
        };

        spec.Summary.Add(new ExportMeta("در انتظار تایید", vers.Count.ToString()));

        foreach (var v in vers.Take(q.MaxRows))
        {
            var d = docs.First(x => x.Id == v.DocumentId);
            var va = apprs.Where(a => a.VersionId == v.Id).ToList();
            var waiting = va.Where(a => a.Status == 0).Select(a => a.UserName).ToList();
            var waitDays = (DateTime.Today - v.CreatedAt.Date).Days;

            spec.Rows.Add(new ExportRow(
                d.Code,
                d.Title,
                v.VersionNo,
                v.CreatedByName,
                v.CreatedAt,
                waitDays,
                $"{va.Count(a => a.Status == 1)} از {va.Count}",
                string.Join("، ", waiting))
            {
                Style = waitDays > 14 ? ExportRowStyle.Danger
                      : waitDays > 7 ? ExportRowStyle.Subtotal
                      : ExportRowStyle.Normal
            });
        }

        spec.Notes.Add("سطرهای قرمز: بیش از ۱۴ روز در گردش مانده‌اند.");
        return spec;
    }
}
