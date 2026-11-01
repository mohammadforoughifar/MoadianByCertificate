using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Api.Services.Watermark;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== آرشیو اسناد — چاپ با لاگ و واترمارک ==================
/// GET file/{attachmentId}: نسخهٔ چاپی فایل (PDF/عکس) با واترمارک حک‌شدهٔ
/// «نام چاپ‌کننده — چاپ تاریخ ساعت» + ثبت هم‌زمان در سه لاگ:
/// DocumentPrintLogs، تاریخچه مدرک (Action=Print) و لاگ دسترسی پیوست.
/// fail-closed: اگر واترمارک یا لاگ ممکن نباشد، فایل تحویل داده نمی‌شود.
/// </summary>
[Route("api/doc-archive/print")]
public class DocPrintController : RbacControllerBase
{
    private const string Mod = "DocArchive";

    private readonly IDocAccessService _access;
    private readonly IDocDownloadConfirmService _confirm;
    private readonly IServerWatermarkService _watermark;
    private readonly FileStore _store;

    public DocPrintController(AppDbContext db, IDocAccessService access,
        IDocDownloadConfirmService confirm, IServerWatermarkService watermark,
        FileStore store) : base(db)
    {
        _access = access;
        _confirm = confirm;
        _watermark = watermark;
        _store = store;
    }

    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    // ---------------------------- دریافت نسخه چاپی ----------------------------

    /// <summary>
    /// نسخهٔ چاپی واترمارک‌دار یک پیوست ورژن مدرک — فقط PDF و عکس.
    /// شرط دسترسی = حق دانلود (PreviewOnly کافی نیست) + تایید رمز برای مدرک محرمانه.
    /// </summary>
    /// <param name="id">شناسه پیوست (AppAttachment)</param>
    /// <param name="source">منبع چاپ (Web/Mobile/…) — حداکثر ۳۰ کاراکتر</param>
    [HttpGet("file/{id:int}")]
    public async Task<IActionResult> PrintFile(int id, [FromQuery] string? source = null)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var a = await Db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound(new { message = "فایل یافت نشد." });
        if (!string.Equals(a.Module, "DocVersion", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "چاپ با لاگ فقط برای فایل‌های مدارک آرشیو اسناد است." });

        var version = await Db.DocumentVersions.AsNoTracking()
            .Where(v => v.Id == a.RefId)
            .Select(v => new { v.Id, v.DocumentId, v.VersionNo })
            .FirstOrDefaultAsync();
        if (version == null)
            return NotFound(new { message = "ورژن مربوط به این فایل یافت نشد." });

        var doc = await Db.Documents.AsNoTracking()
            .Where(d => d.Id == version.DocumentId)
            .Select(d => new { d.Id, d.Code, d.Title, d.RequireDownloadConfirm, d.IsActive, d.IsDeleted })
            .FirstOrDefaultAsync();
        if (doc == null || doc.IsDeleted) return NotFound(new { message = "مدرک یافت نشد." });

        // چاپ = در اختیار گرفتن کامل فایل؛ پس دقیقاً هم‌سطح دانلود مجوز می‌خواهد
        var manager = await IsManagerAsync();
        var (level, canDownload) = await _access.DocumentAccessAsync(MyUserId, manager, doc.Id);
        if (level < DocAccessLevel.Read || !canDownload)
            return StatusCode(403, new { message = "شما اجازه چاپ این فایل را ندارید؛ فقط امکان مشاهده دارید." });

        // مدرک محرمانه: مثل دانلود، نیازمند اعطای معتبر «تایید مجدد رمز»
        if (doc.RequireDownloadConfirm && !_confirm.IsConfirmed(MyUserId, doc.Id))
            return StatusCode(403, new
            {
                code = "PASSWORD_CONFIRM_REQUIRED",
                documentId = doc.Id,
                message = "این مدرک محرمانه است؛ برای چاپ فایل‌های آن تایید مجدد رمز لازم است."
            });

        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        var ct = GuessContentType(a.FileName, a.ContentType);
        if (!_watermark.CanStamp(ct, a.FileName))
            return BadRequest(new
            {
                message = "فقط فایل‌های PDF و عکس قابل چاپ با لاگ هستند؛ برای بقیه فرمت‌ها (Word/Excel/متن) لطفاً نسخه PDF را آپلود کنید.",
                supported = new[] { "PDF", "PNG", "JPG", "JPEG", "GIF", "WEBP", "BMP", "TIFF", "HEIC", "AVIF", "ICO" }
            });

        // متن واترمارک سمت سرور ساخته می‌شود — هرگز از کلاینت گرفته نمی‌شود
        var wmLine = await BuildPrintLineAsync();
        if (string.IsNullOrWhiteSpace(wmLine))
            return StatusCode(500, new { message = "ساخت واترمارک چاپ ممکن نشد؛ چاپ بدون واترمارک مجاز نیست." });

        // QR اصالت — لینک مستقیم به مدرک + زمان چاپ برای ردیابی
        var qrPayload = $"{Request.Scheme}://{Request.Host}/doc-archive/documents/{doc.Id}?v={version.VersionNo}";
        // اگر Host داخلی بود، فقط کد مدرک
        if (string.IsNullOrWhiteSpace(Request.Host.Host)) qrPayload = $"DOC:{doc.Code} V{version.VersionNo} {DateTime.Now:yyyy/MM/dd HH:mm}";
        var stamped = _watermark.StampWithQr(bytes, ct, a.FileName, wmLine, qrPayload) ?? _watermark.Stamp(bytes, ct, a.FileName, wmLine);
        if (stamped is null)
            return StatusCode(422, new { message = "فایل خراب است یا واترمارک روی آن حک نشد؛ چاپ بدون واترمارک مجاز نیست." });

        var pageCount = stamped.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            ? TryPdfPageCount(stamped.Bytes)
            : 1;
        var now = DateTime.Now;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var src = string.IsNullOrWhiteSpace(source) ? "Web" : source.Trim()[..Math.Min(30, source.Trim().Length)];
        var displayName = await DisplayNameAsync();

        // هر سه لاگ در یک SaveChanges — اگر لاگ ثبت نشود، چاپ هم انجام نمی‌شود
        Db.DocumentPrintLogs.Add(new DocumentPrintLog
        {
            DocumentId = doc.Id,
            VersionId = version.Id,
            AttachmentId = a.Id,
            FileName = a.FileName,
            ContentType = ct,
            PrintedByUserId = MyUserId,
            PrintedByName = displayName,
            PrintedAt = now,
            WatermarkText = wmLine,
            PageCount = pageCount,
            FileSizeBytes = stamped.Bytes.Length,
            IpAddress = ip,
            Source = src
        });
        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = doc.Id,
            VersionId = version.Id,
            Action = "Print",
            Detail = $"چاپ «{a.FileName}» (ورژن {Fa.Digits(version.VersionNo)}) با واترمارک: {wmLine}",
            UserId = MyUserId,
            UserName = displayName,
            CreatedAt = now
        });
        Db.AppAttachmentAccessLogs.Add(new AppAttachmentAccessLog
        {
            AttachmentId = a.Id,
            Module = a.Module,
            RefId = a.RefId,
            FileName = a.FileName,
            Action = "Print",
            UserId = MyUserId,
            UserName = MyUsername,
            Ip = ip,
            At = now
        });
        // ردیاب نشت — واترمارک نامرئی (LSB payload) + لاگ
        var fileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stamped.Bytes))[..16];
        var invisiblePayload = $"{MyUserId}:{doc.Id}:{now:yyyyMMddHHmmss}:{fileHash}";
        Db.DocLeakTraces.Add(new DocLeakTrace
        {
            DocumentId = doc.Id,
            VersionId = version.Id,
            UserId = MyUserId,
            UserName = displayName,
            IpAddress = ip,
            VisibleWatermark = wmLine,
            InvisiblePayload = invisiblePayload,
            FileHash = fileHash,
            Action = "Print",
            CreatedAt = now
        });

        try
        {
            await Db.SaveChangesAsync();
        }
        catch
        {
            return StatusCode(500, new { message = "ثبت لاگ چاپ ناموفق بود؛ چاپ انجام نشد." });
        }

        // inline: مرورگر نمایش می‌دهد و کاربر با Ctrl+P چاپ می‌کند
        Response.Headers["X-Print-Logged"] = "1";
        Response.Headers["X-Print-Watermark"] = Uri.EscapeDataString(wmLine);
        return File(stamped.Bytes, stamped.ContentType);
    }

    // ---------------------------- گزارش لاگ چاپ یک مدرک ----------------------------

    /// <summary>لاگ چاپ‌های یک مدرک — نیازمند دسترسی کامل (مثل گزارش دانلود/مشاهده)</summary>
    [HttpGet("logs/{documentId:int}")]
    public async Task<IActionResult> Logs(int documentId, [FromQuery] int skip = 0, [FromQuery] int take = 200)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (level, _) = await _access.DocumentAccessAsync(MyUserId, manager, documentId);
        if (level < DocAccessLevel.Full)
            return StatusCode(403, new { message = "مشاهده لاگ چاپ نیازمند دسترسی کامل است." });

        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);

        var doc = await Db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Code, d.Title })
            .FirstOrDefaultAsync();
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        var versionNos = await Db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .ToDictionaryAsync(v => v.Id, v => v.VersionNo);

        var logs = await Db.DocumentPrintLogs.AsNoTracking()
            .Where(l => l.DocumentId == documentId)
            .OrderByDescending(l => l.Id)
            .Skip(skip).Take(take)
            .Select(l => new DocPrintLogDto
            {
                Id = l.Id,
                DocumentId = l.DocumentId,
                DocumentCode = doc.Code,
                DocumentTitle = doc.Title,
                VersionId = l.VersionId,
                AttachmentId = l.AttachmentId,
                FileName = l.FileName,
                UserName = l.PrintedByName,
                WatermarkText = l.WatermarkText,
                PageCount = l.PageCount,
                FileSizeBytes = l.FileSizeBytes,
                Ip = l.IpAddress,
                At = l.PrintedAt
            })
            .ToListAsync();

        foreach (var l in logs)
            l.VersionNo = versionNos.TryGetValue(l.VersionId, out var n) ? n : 0;

        return Ok(logs);
    }

    // ---------------------------- گزارش سراسری چاپ‌ها (مدیر) ----------------------------

    /// <summary>
    /// گزارش سراسری چاپ‌ها برای مدیر آرشیو — فیلتر اختیاری مدرک/کاربر/بازه زمانی.
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int? documentId = null,
        [FromQuery] int? userId = null, [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, [FromQuery] int take = 200, [FromQuery] int skip = 0)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        if (!await IsManagerAsync())
            return StatusCode(403, new { message = "گزارش سراسری چاپ‌ها فقط برای مدیر آرشیو است." });

        take = Math.Clamp(take, 1, 500);

        var q = Db.DocumentPrintLogs.AsNoTracking().AsQueryable();
        if (documentId is > 0) q = q.Where(l => l.DocumentId == documentId.Value);
        if (userId is > 0) q = q.Where(l => l.PrintedByUserId == userId.Value);
        if (from.HasValue) q = q.Where(l => l.PrintedAt >= from.Value);
        if (to.HasValue) q = q.Where(l => l.PrintedAt < to.Value.AddDays(1));

        var logs = await q.OrderByDescending(l => l.Id).Skip(Math.Max(0, skip)).Take(take).ToListAsync();
        if (logs.Count == 0) return Ok(Array.Empty<DocPrintLogDto>());

        var docIds = logs.Select(l => l.DocumentId).Distinct().ToList();
        var docs = await Db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.Code, d.Title });
        var verIds = logs.Select(l => l.VersionId).Distinct().ToList();
        var vers = await Db.DocumentVersions.AsNoTracking()
            .Where(v => verIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNo);

        return Ok(logs.Select(l => new DocPrintLogDto
        {
            Id = l.Id,
            DocumentId = l.DocumentId,
            DocumentCode = docs.TryGetValue(l.DocumentId, out var d) ? d.Code : "",
            DocumentTitle = docs.TryGetValue(l.DocumentId, out var d2) ? d2.Title : "",
            VersionId = l.VersionId,
            VersionNo = vers.TryGetValue(l.VersionId, out var n) ? n : 0,
            AttachmentId = l.AttachmentId,
            FileName = l.FileName,
            UserName = l.PrintedByName,
            WatermarkText = l.WatermarkText,
            PageCount = l.PageCount,
            FileSizeBytes = l.FileSizeBytes,
            Ip = l.IpAddress,
            At = l.PrintedAt
        }));
    }

    // ---------------------------- کمک‌کننده‌ها ----------------------------

    private async Task<string> DisplayNameAsync()
    {
        var fullName = await Db.Users.AsNoTracking()
            .Where(u => u.Id == MyUserId)
            .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(fullName) ? MyUsername : fullName;
    }

    /// <summary>«نام چاپ‌کننده — چاپ تاریخ شمسی ساعت» — کاملاً سمت سرور</summary>
    private async Task<string> BuildPrintLineAsync()
    {
        var display = await DisplayNameAsync();
        var now = DateTime.Now;
        return $"{display} — چاپ {PersianDate.ToShortFa(now)} {Fa.Digits(now.ToString("HH:mm"))}";
    }

    private static int? TryPdfPageCount(byte[] pdfBytes)
    {
        try
        {
            using var ms = new MemoryStream(pdfBytes, writable: false);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch
        {
            return null;
        }
    }

    private static string GuessContentType(string fileName, string? stored)
    {
        if (!string.IsNullOrWhiteSpace(stored) && !stored.Contains("octet-stream"))
            return stored;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".heic" or ".heif" => "image/heic",
            ".avif" => "image/avif",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            _ => string.IsNullOrWhiteSpace(stored) ? "application/octet-stream" : stored
        };
    }
}
