using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Api.Services.Watermark;
using Inventory.Shared;
using Inventory.Shared.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace Inventory.Api.Controllers;

/// <summary>
/// ================== بایگانی جامع — یک API برای همه ماژول‌ها ==================
/// هر کاربر فقط بایگانی خودش را می‌بیند (خصوصی). پوشه/زیرپوشه نامحدود.
/// افزودن رکورد هر ماژول: POST items با { module, refId, title, link }
/// </summary>
[ApiController]
[Route("api/archive")]
[Authorize]
public class ArchiveController : ControllerBase
{
    private readonly AppDbContext _db;
    public ArchiveController(AppDbContext db) => _db = db;

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    // ---------- پوشه‌ها ----------
    [HttpGet("folders")]
    public async Task<IActionResult> Folders([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.ArchiveFolders.Where(f => f.OwnerUserId == MyUserId)
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.ParentId, f.Name, f.CreatedAt,
                ItemCount = _db.ArchiveItems.Count(i => i.FolderId == f.Id) }), skip, take));

    public class FolderDto { public string Name { get; set; } = ""; public int? ParentId { get; set; } }

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] FolderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام پوشه را وارد کنید." });
        if (dto.ParentId is > 0 &&
            !await _db.ArchiveFolders.AnyAsync(f => f.Id == dto.ParentId && f.OwnerUserId == MyUserId))
            return BadRequest(new { message = "پوشه والد نامعتبر است." });

        var f = new ArchiveFolder { OwnerUserId = MyUserId, ParentId = dto.ParentId, Name = dto.Name.Trim() };
        _db.ArchiveFolders.Add(f);
        await _db.SaveChangesAsync();
        return Ok(new { id = f.Id });
    }

    [HttpPut("folders/{id:int}")]
    public async Task<IActionResult> RenameFolder(int id, [FromBody] FolderDto dto)
    {
        var f = await _db.ArchiveFolders.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == MyUserId);
        if (f == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "نام پوشه را وارد کنید." });
        f.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("folders/{id:int}")]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        var f = await _db.ArchiveFolders.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == MyUserId);
        if (f == null) return NotFound();

        // حذف بازگشتی زیرپوشه‌ها و آیتم‌ها
        var all = await _db.ArchiveFolders.Where(x => x.OwnerUserId == MyUserId).ToListAsync();
        var toDelete = new List<int> { id };
        void Collect(int pid)
        {
            foreach (var c in all.Where(x => x.ParentId == pid)) { toDelete.Add(c.Id); Collect(c.Id); }
        }
        Collect(id);

        _db.ArchiveItems.RemoveRange(_db.ArchiveItems.Where(i => toDelete.Contains(i.FolderId)));
        _db.ArchiveFolders.RemoveRange(all.Where(x => toDelete.Contains(x.Id)));
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ---------- آیتم‌ها ----------
    [HttpGet("items/{folderId:int}")]
    public async Task<IActionResult> Items(int folderId, [FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.ArchiveItems
            .Where(i => i.OwnerUserId == MyUserId && i.FolderId == folderId)
            .OrderByDescending(i => i.Id)
            .Select(i => new { i.Id, i.Module, i.RefId, i.Title, i.Link, i.Note, i.CreatedAt }), skip, take));

    public class ItemDto
    {
        public int FolderId { get; set; }
        public string Module { get; set; } = "";
        public int RefId { get; set; }
        public string Title { get; set; } = "";
        public string? Link { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] ItemDto dto)
    {
        if (!await _db.ArchiveFolders.AnyAsync(f => f.Id == dto.FolderId && f.OwnerUserId == MyUserId))
            return BadRequest(new { message = "پوشه نامعتبر است." });

        // جلوگیری از تکرار همان رکورد در همان پوشه
        if (await _db.ArchiveItems.AnyAsync(i => i.OwnerUserId == MyUserId && i.FolderId == dto.FolderId
                && i.Module == dto.Module && i.RefId == dto.RefId))
            return BadRequest(new { message = "این مورد قبلاً در این پوشه بایگانی شده است." });

        _db.ArchiveItems.Add(new ArchiveItem
        {
            OwnerUserId = MyUserId, FolderId = dto.FolderId, Module = dto.Module.Trim(),
            RefId = dto.RefId, Title = dto.Title.Trim(), Link = dto.Link, Note = dto.Note?.Trim()
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        var i = await _db.ArchiveItems.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == MyUserId);
        if (i == null) return NotFound();
        _db.ArchiveItems.Remove(i);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

/// <summary>
/// ================== پیوست جامع — یک API برای همه فرم‌ها ==================
/// آپلود/لیست/دانلود/حذف با (module + refId) — فرم جدید = صفر کد بک‌اند.
/// </summary>
[ApiController]
[Route("api/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileStore _store;
    private readonly IAttachmentGuard _guard;
    private readonly Inventory.Api.Services.DocArchive.IDocIndexService _docIndex;
    private readonly Inventory.Api.Services.DocArchive.IDocDownloadConfirmService _confirm;
    private readonly IServerWatermarkService _watermark;
    private readonly IDocAccessService _docAccess;
    public AttachmentsController(AppDbContext db, FileStore store, IAttachmentGuard guard,
        Inventory.Api.Services.DocArchive.IDocIndexService docIndex,
        Inventory.Api.Services.DocArchive.IDocDownloadConfirmService confirm,
        IServerWatermarkService watermark, IDocAccessService docAccess)
    { _db = db; _store = store; _guard = guard; _docIndex = docIndex; _confirm = confirm; _watermark = watermark; _docAccess = docAccess; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsAdmin => User.IsInRole("Admin");

    private Task<bool> IsDocArchiveManagerAsync() => DocArchiveAuthorization.IsManagerAsync(_db, MyUserId, IsAdmin);

    private async Task<bool> CanWriteDocVersion(int versionId)
    {
        var version = await _db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId && !v.IsFrozen);
        if (version == null || !await _db.Documents.AnyAsync(d => d.Id == version.DocumentId && d.IsActive && !d.IsDeleted)) return false;
        var level = await _docAccess.DocumentAccessAsync(MyUserId, await IsDocArchiveManagerAsync(), version.DocumentId);
        return level.Item1 >= DocAccessLevel.Write;
    }

    /// <summary>فرمت‌هایی که مرورگر به‌صورت native نمایش می‌دهد.</summary>
    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif",
        "image/webp", "image/bmp", "image/x-ms-bmp", "image/svg+xml",
        "image/x-icon", "image/vnd.microsoft.icon", "image/avif",
        "text/plain", "text/csv", "text/html", "application/json", "application/xml", "text/xml"
    };

    private static string GuessContentType(string fileName, string stored) => AttachmentPreviewFormats.Mime(fileName, stored);

    private static bool IsPreviewable(string fileName, string ct) => AttachmentPreviewFormats.Kind(fileName, ct) != "other";

    private static bool NeedsImageConvert(string fileName, string ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".tif" or ".tiff" or ".heic" or ".heif"
            || ct.Contains("tiff", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("heic", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("heif", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// برای پیوست ورژنِ آرشیو اسناد، فلگ‌های امنیتی مدرک (شناسه + محرمانه + واترمارک پیش‌نمایش).
    /// اگر پیوست ماژول DocVersion نباشد یا ورژن یافت نشود null.
    /// </summary>
    private async Task<(int DocId, bool RequireConfirm, bool Watermark)?> DocSecurityFlagsAsync(string module, int refId)
    {
        if (!string.Equals(module, "DocVersion", StringComparison.OrdinalIgnoreCase)) return null;
        var docId = await _db.DocumentVersions.Where(v => v.Id == refId)
            .Select(v => (int?)v.DocumentId).FirstOrDefaultAsync();
        if (docId is not int dId) return null;
        var flags = await _db.Documents.Where(d => d.Id == dId)
            .Select(d => new { d.RequireDownloadConfirm, d.WatermarkPreview }).FirstOrDefaultAsync();
        if (flags == null) return null;
        return (dId, flags.RequireDownloadConfirm, flags.WatermarkPreview);
    }

    /// <summary>ثبت گردانه مشاهده/دانلود پیوست (همه ماژول‌ها) — خطای لاگ دانلود را متوقف نمی‌کند.</summary>
    private async Task LogAccessAsync(AppAttachment a, string action)
    {
        try
        {
            _db.AppAttachmentAccessLogs.Add(new AppAttachmentAccessLog
            {
                AttachmentId = a.Id, Module = a.Module, RefId = a.RefId, FileName = a.FileName,
                Action = action, UserId = MyUserId, UserName = MyUsername,
                Ip = HttpContext.Connection.RemoteIpAddress?.ToString(), At = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch { /* گردانه نباید جریان اصلی را متوقف کند */ }
    }

    /// <summary>
    /// ساخت متن واترمارک: «نام نمایشی کاربر — تاریخ شمسی ساعت» (منطق سمت سرور؛ نه متنی که
    /// کلاینت می‌فرستد). این رشته روی خودِ بایت فایل حک می‌شود تا در دانلود/چاپ/عکس‌برداری بماند.
    /// </summary>
    private async Task<string?> BuildWatermarkLineAsync()
    {
        try
        {
            var fullName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == MyUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync();
            var display = string.IsNullOrWhiteSpace(fullName) ? MyUsername : fullName;
            return $"{display} — {PersianDate.ToShortFa(DateTime.Now)} {Fa.Digits(DateTime.Now.ToString("HH:mm"))}";
        }
        catch
        {
            return null;
        }
    }

    [HttpGet("{module}/{refId:int}")]
    public async Task<IActionResult> List(string module, int refId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var access = await _guard.CheckAsync(module, refId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به پیوست‌های این مورد دسترسی ندارید." });

        var docVersion = string.Equals(module, "DocVersion", StringComparison.OrdinalIgnoreCase);
        var canModify = docVersion ? await CanWriteDocVersion(refId) : access == AttachmentAccess.Download;
        var manager = docVersion ? await IsDocArchiveManagerAsync() : IsAdmin;
        var rows = await _db.AppAttachments.Where(a => a.Module == module && a.RefId == refId).OrderBy(x => x.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.UploaderName, a.UploaderUserId, a.UploadedAt, a.FilePath, a.Data })
            .ToPageListAsync(pagination);

        // اگر مدرک فلگ‌های امنیتی (محرمانه/واترمارک) داشته باشد، کلاینت از این فیلدها برای UX متناسب استفاده می‌کند
        var flags = await DocSecurityFlagsAsync(module, refId);
        var needsConfirm = flags is { RequireConfirm: true } f0 && !_confirm.IsConfirmed(MyUserId, f0.DocId);

        return Ok(pagination.Result(rows.Select(a =>
        {
            var ct = GuessContentType(a.FileName, a.ContentType);
            return new
            {
                a.Id, a.FileName, a.UploaderName, a.UploaderUserId, a.UploadedAt,
                Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length,
                ContentType = ct,
                CanPreview = IsPreviewable(a.FileName, ct),
                CanDownload = access == AttachmentAccess.Download,
                CanDelete = canModify && (a.UploaderUserId == MyUserId || manager),
                DocumentId = flags is { RequireConfirm: true } confidential ? confidential.DocId : 0,
                NeedsConfirm = needsConfirm,
                Watermark = flags?.Watermark ?? false
            };
        })));
    }

    [HttpPost("{module}/{refId:int}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(string module, int refId, IFormFile file)
    {
        if (string.Equals(module, "DocVersion", StringComparison.OrdinalIgnoreCase))
        {
            if (!await CanWriteDocVersion(refId))
                return StatusCode(403, new { message = "افزودن پیوست نیازمند دسترسی نوشتن و نسخه خارج از گردش تأیید است." });
        }
        else if (await _guard.CheckAsync(module, refId, MyUserId, IsAdmin) != AttachmentAccess.Download)
            return StatusCode(403, new { message = "شما اجازه افزودن پیوست به این مورد را ندارید." });

        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        string relPath;
        try
        {
            relPath = await _store.SaveAsync(module, refId, ms, file.FileName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // روی IIS معمولاً یعنی هویت Application Pool اجازهٔ نوشتن روی پوشهٔ ذخیره را ندارد
            HttpContext?.RequestServices?.GetService<ILogger<AttachmentsController>>()?.LogError(ex,
                "ذخیرهٔ پیوست روی دیسک سرور ناموفق بود (ماژول {Module}، رکورد {RefId}).", module, refId);
            return StatusCode(500, new { message = "ذخیرهٔ فایل روی سرور ناموفق بود؛ مدیر سیستم دسترسی نوشتن پوشهٔ ذخیره را بررسی کند." });
        }

        var att = new AppAttachment
        {
            Module = module, RefId = refId,
            // نام اصلی فایل (حتی اگر مرورگر مسیر ویندوزی مثل C:\fakepath\a.pdf بفرستد)
            FileName = OriginalFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = MyUsername, UploaderUserId = MyUserId
        };
        _db.AppAttachments.Add(att);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // رکورد ثبت نشد → فایل بی‌صاحب روی دیسک سرور نماند
            _store.Delete(relPath);
            throw;
        }

        if (string.Equals(module, "DocVersion", StringComparison.OrdinalIgnoreCase))
        {
            await _docIndex.QueueAttachmentIndexingAsync(att.Id);
        }

        return Ok();
    }

    /// <summary>
    /// نام اصلی فایل برای ستون FileName (حداکثر ۲۵۵ کاراکتر): مسیر ویندوزی مرورگر (C:\fakepath\…) حذف
    /// و در صورت نیاز کوتاه می‌شود بدون اینکه پسوند از دست برود (پیش‌نمایش به پسوند وابسته است).
    /// </summary>
    private static string OriginalFileName(string? raw)
    {
        var name = (raw ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        if (name.Length == 0) name = "file";
        if (name.Length <= 255) return name;
        var ext = Path.GetExtension(name);
        return ext.Length is > 0 and <= 16 ? name[..(255 - ext.Length)] + ext : name[..255];
    }

    /// <summary>دانلود پیوست — نیازمند احراز هویت و دسترسی دانلود روی رکورد صاحبِ پیوست.</summary>
    [HttpGet("download/{id:int}")]
    public async Task<IActionResult> Download(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var access = await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به این فایل دسترسی ندارید." });
        if (access == AttachmentAccess.PreviewOnly)
            return StatusCode(403, new { message = "شما اجازه دانلود این فایل را ندارید؛ فقط امکان مشاهده دارید." });

        // مدرک محرمانه: دانلود فقط با اعطای «تایید مجدد رمز» معتبر
        var flags = await DocSecurityFlagsAsync(a.Module, a.RefId);
        if (flags is { RequireConfirm: true } fl && !_confirm.IsConfirmed(MyUserId, fl.DocId))
            return StatusCode(403, new
            {
                code = "PASSWORD_CONFIRM_REQUIRED",
                documentId = fl.DocId,
                message = "این مدرک محرمانه است؛ برای دانلود فایل‌های آن تایید مجدد رمز لازم است."
            });

        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        var ct = GuessContentType(a.FileName, a.ContentType);
        var fileName = a.FileName;

        // واترمارک سمت سرور: مدرک با فلگ واترمارک → بایت حک‌شده تحویل داده می‌شود
        // (فایل اصلی در حافظه دست‌نخورده می‌ماند؛ فقط همین پاسخِ دانلود واترمارک می‌خورد).
        if (flags is { Watermark: true })
        {
            var wmLine = await BuildWatermarkLineAsync();
            if (!string.IsNullOrWhiteSpace(wmLine) && _watermark.CanStamp(ct, fileName))
            {
                var stamped = _watermark.Stamp(bytes, ct, fileName, wmLine!);
                if (stamped is not null)
                {
                    bytes = stamped.Bytes;
                    ct = stamped.ContentType;
                    fileName = stamped.FileName;
                }
            }
        }

        await LogAccessAsync(a, "Download");
        return File(bytes, ct, fileName);
    }

    /// <summary>
    /// پیش‌نمایش داخل برنامه — فایل به‌صورت inline برگردانده می‌شود (بدون هدر دانلود).
    /// برای کاربرانی که فقط حق «مشاهده» دارند هم کار می‌کند.
    /// </summary>
    [HttpGet("preview/{id:int}")]
    public async Task<IActionResult> Preview(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var access = await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به این فایل دسترسی ندارید." });

        var ct = GuessContentType(a.FileName, a.ContentType);
        if (!IsPreviewable(a.FileName, ct))
            return BadRequest(new { message = "این نوع فایل قابل پیش‌نمایش نیست." });

        // مدرک محرمانه: مشاهده هم فقط با اعطای «تایید مجدد رمز» معتبر
        var flags = await DocSecurityFlagsAsync(a.Module, a.RefId);
        if (flags is { RequireConfirm: true } fl && !_confirm.IsConfirmed(MyUserId, fl.DocId))
            return StatusCode(403, new
            {
                code = "PASSWORD_CONFIRM_REQUIRED",
                documentId = fl.DocId,
                message = "این مدرک محرمانه است؛ برای مشاهده فایل‌های آن تایید مجدد رمز لازم است."
            });

        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        // پیش‌فرض: همان فایلِ inline (تصویر/PDF/متن)
        var servedCt = AttachmentPreviewFormats.Kind(a.FileName, ct) == "text" ? "text/plain; charset=utf-8" : ct;
        var previewName = "preview";

        // TIFF / HEIC (یا هر image/ خارج از لیست inline) را به PNG تبدیل کن تا در همه مرورگرها دیده شود
        if (NeedsImageConvert(a.FileName, ct) || (ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !InlineTypes.Contains(ct)))
        {
            try
            {
                var info = Image.Identify(bytes);
                if (info == null || (long)info.Width * info.Height > 50_000_000)
                    return BadRequest(new { code = "IMAGE_TOO_LARGE", message = "ابعاد تصویر برای پیش‌نمایش ایمن بیش از حد مجاز است." });
                using var image = Image.Load(bytes);
                image.Mutate(x => x.AutoOrient());
                if (image.Width > 2400 || image.Height > 2400)
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(2400, 2400)
                    }));
                }
                using var outMs = new MemoryStream();
                image.Save(outMs, new PngEncoder());
                bytes = outMs.ToArray();
                servedCt = "image/png";
                previewName = "preview.png";
            }
            catch (Exception)
            {
                return BadRequest(new { code = "IMAGE_FORMAT_UNSUPPORTED", message = "این تصویر خراب است یا رمزگشای فرمت آن روی سرور موجود نیست؛ برای پیش‌نمایش یک نسخه PNG یا JPEG تهیه کنید. HEIC/HEIF بدون رمزگشای اضافی تضمین نمی‌شود." });
            }
        }
        else if (!InlineTypes.Contains(servedCt.Split(';')[0]) && !servedCt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "این نوع فایل را از پیش‌نمایش HTML باز کنید." });
        }

        // واترمارک سمت سرور: روی بایتِ همین پاسخ پیش‌نمایش حک می‌شود تا حتی اگر کسی
        // آدرس فایل را کپی یا فایل را از حافظه مرورگر بیرون بکشد، نام و زمان او رویش باشد.
        if (flags is { Watermark: true })
        {
            var wmLine = await BuildWatermarkLineAsync();
            if (!string.IsNullOrWhiteSpace(wmLine) && _watermark.CanStamp(servedCt, previewName))
            {
                var stamped = _watermark.Stamp(bytes, servedCt, previewName, wmLine!);
                if (stamped is not null)
                {
                    bytes = stamped.Bytes;
                    servedCt = stamped.ContentType;
                    previewName = Path.GetFileName(stamped.FileName);
                }
            }
        }

        await LogAccessAsync(a, "Preview");
        Response.Headers["Cache-Control"] = "no-store, private";
        // SVG must not become an active same-origin document if the API URL is opened directly.
        if (servedCt == "image/svg+xml")
            Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'";

        // inline تا مرورگر داخل iframe/img نمایش دهد و پنجره دانلود باز نشود
        Response.Headers["Content-Disposition"] = "inline; filename=\"" + previewName + "\"";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, servedCt);
    }

    /// <summary>
    /// پیش‌نمایش Word/Excel/PowerPoint به‌صورت HTML (جداول و پاراگراف‌ها، بدون نیاز به دانلود).
    /// </summary>
    [HttpGet("preview-html/{id:int}")]
    public async Task<IActionResult> PreviewHtml(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var access = await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به این فایل دسترسی ندارید." });

        var flags = await DocSecurityFlagsAsync(a.Module, a.RefId);
        if (flags is { RequireConfirm: true } confidential && !_confirm.IsConfirmed(MyUserId, confidential.DocId))
            return StatusCode(403, new { code = "PASSWORD_CONFIRM_REQUIRED", documentId = confidential.DocId, message = "برای مشاهده فایل محرمانه، تأیید رمز لازم است." });
        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        if (!OfficePreviewHtml.CanHandle(a.FileName, a.ContentType))
            return BadRequest(new { code = "PREVIEW_FORMAT_UNSUPPORTED", message = "این فایل از خانوادهٔ آفیس نیست." });
        if (!OfficePreviewHtml.TryBuild(bytes, a.FileName, a.ContentType, out var html, out var error))
            return BadRequest(new { code = "OFFICE_PREVIEW_UNSUPPORTED", message = error ?? "تبدیل پیش‌نمایش ممکن نشد." });
        if (flags is { Watermark: true })
        {
            var line = await BuildWatermarkLineAsync();
            if (!string.IsNullOrWhiteSpace(line)) html = OfficePreviewHtml.AddWatermark(html, line);
        }
        await LogAccessAsync(a, "Preview");
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";
        Response.Headers["Content-Disposition"] = "inline; filename=\"preview.html\"";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>حذف — فقط آپلودکننده یا مدیر.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var docVersion = string.Equals(a.Module, "DocVersion", StringComparison.OrdinalIgnoreCase);
        if (docVersion)
        {
            if (!await CanWriteDocVersion(a.RefId))
                return StatusCode(403, new { message = "حذف پیوست نیازمند دسترسی نوشتن و نسخه خارج از گردش تأیید است." });
        }
        else if (await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin) != AttachmentAccess.Download)
            return StatusCode(403, new { message = "شما اجازه حذف پیوست این مورد را ندارید." });
        var manager = docVersion ? await IsDocArchiveManagerAsync() : IsAdmin;
        if (a.UploaderUserId != MyUserId && !manager)
            return StatusCode(403, new { message = "فقط آپلودکننده یا مدیر مجاز می‌تواند این پیوست را حذف کند." });
        _store.Delete(a.FilePath);
        if (string.Equals(a.Module, "DocVersion", StringComparison.OrdinalIgnoreCase))
        {
            var oldExtracted = await _db.DocExtractedTexts.Where(x => x.AttachmentId == a.Id).ToListAsync();
            _db.DocExtractedTexts.RemoveRange(oldExtracted);
            _db.DocIndexJobs.RemoveRange(await _db.DocIndexJobs.Where(j => j.AttachmentId == a.Id).ToListAsync());
        }
        _db.AppAttachments.Remove(a);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
