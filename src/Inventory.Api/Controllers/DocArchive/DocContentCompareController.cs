using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Controllers.DocArchive;
[Route("api/doc-archive/documents/{id:int}")]
public class DocContentCompareController(AppDbContext db, IDocAccessService access, IDocDownloadConfirmService confirm, FileStore files, IDocIndexService index) : RbacControllerBase(db)
{
    private async Task<IActionResult?> Guard(int id, bool content)
    {
        var doc = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (doc == null) return NotFound();
        if ((await access.DocumentAccessAsync(MyUserId, await HasAsync("DocArchive", "Manage"), id)).Level < DocAccessLevel.Read) return Forbid();
        if (content && doc.RequireDownloadConfirm && !confirm.IsConfirmed(MyUserId, id)) return StatusCode(403, new { message = "برای مقایسه محتوای محرمانه، تأیید رمز لازم است.", code = "PASSWORD_CONFIRM_REQUIRED" });
        return null;
    }
    [HttpGet("compare-files/{versionId:int}")]
    public async Task<IActionResult> Attachments(int id, int versionId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await Guard(id, false) is { } denied) return denied;
        if (!await Db.DocumentVersions.AnyAsync(v => v.Id == versionId && v.DocumentId == id)) return NotFound();
        return Ok(await Paging.ResultAsync(Db.AppAttachments.Where(a => a.Module == "DocVersion" && a.RefId == versionId).OrderBy(a => a.Id)
            .Select(a => new LookupItem { Id = a.Id, Name = a.FileName }), skip, take));
    }
    [HttpPost("compare-content")]
    public async Task<IActionResult> Compare(int id, DocContentCompareRequest request)
    {
        if (await Guard(id, true) is { } denied) return denied;
        var rows = await (from a in Db.AppAttachments
                          join v in Db.DocumentVersions on a.RefId equals v.Id
                          where a.Module == "DocVersion" && v.DocumentId == id && (a.Id == request.LeftAttachmentId || a.Id == request.RightAttachmentId)
                          select new { a.Id, a.FileName, a.FilePath, a.Data, v.VersionNo }).ToListAsync();
        if (rows.Count != 2 || rows[0].VersionNo == rows[1].VersionNo) return BadRequest(new { message = "دو فایل از دو نسخه متفاوت همین مدرک انتخاب کنید." });
        var left = rows.Single(a => a.Id == request.LeftAttachmentId); var right = rows.Single(a => a.Id == request.RightAttachmentId);
        DocContentCompareDto result;
        if (Path.GetExtension(left.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(right.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            if ((left.FilePath != null && files.Size(left.FilePath) > 20_000_000) || (right.FilePath != null && files.Size(right.FilePath) > 20_000_000)) return BadRequest(new { message = "سقف فایل مقایسه Excel بیست مگابایت است." });
            try { result = DocContentDiff.Spreadsheet(files.ReadBytes(left.FilePath) ?? left.Data, files.ReadBytes(right.FilePath) ?? right.Data); }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Xml.XmlException) { return BadRequest(new { message = "مقایسه Excel ممکن نیست؛ ساختار یا حجم فایل را بررسی کنید." }); }
        }
        else
        {
            var texts = await Db.DocExtractedTexts.AsNoTracking().Where(t => t.DocumentId == id && (t.AttachmentId == left.Id || t.AttachmentId == right.Id) && t.Status == "Indexed")
                .Select(t => new { t.AttachmentId, t.ExtractedText, t.SourceType }).ToListAsync();
            var l = texts.FirstOrDefault(t => t.AttachmentId == left.Id); var r = texts.FirstOrDefault(t => t.AttachmentId == right.Id);
            if (l == null || r == null)
            {
                if (l == null && !await Db.DocIndexJobs.AnyAsync(j => j.AttachmentId == left.Id && (j.Status == "Working" || j.Status == "Pending"))) await index.QueueAttachmentIndexingAsync(left.Id);
                if (r == null && !await Db.DocIndexJobs.AnyAsync(j => j.AttachmentId == right.Id && (j.Status == "Working" || j.Status == "Pending"))) await index.QueueAttachmentIndexingAsync(right.Id);
                return Conflict(new { message = "متن فایل هنوز آماده نیست؛ پردازش در صف قرار گرفت. پس از اتمام دوباره مقایسه کنید." });
            }
            if (string.IsNullOrWhiteSpace(l.ExtractedText) || string.IsNullOrWhiteSpace(r.ExtractedText))
                return UnprocessableEntity(new { message = "متن قابل مقایسه در یکی از فایل‌ها موجود نیست؛ نتیجه یکسان محسوب نمی‌شود." });
            result = DocContentDiff.Text(l.ExtractedText, r.ExtractedText);
            if (l.SourceType.Contains("Ocr", StringComparison.OrdinalIgnoreCase) || r.SourceType.Contains("Ocr", StringComparison.OrdinalIgnoreCase))
                result.Notice = (result.Notice ?? "") + " نتیجه بر پایه OCR است؛ خطای خواندن اسکن ممکن است تفاوت کاذب ایجاد کند.";
        }
        // Recheck after work: an expired or revoked grant must not deliver new content.
        if (await Guard(id, true) is { } expired) return expired;
        result.LeftName = left.FileName; result.RightName = right.FileName;
        Db.DocumentLogs.Add(new DocumentLog { DocumentId = id, UserId = MyUserId, UserName = MyUsername, Action = "ContentCompare", Detail = $"پیوست {left.Id} و {right.Id}" });
        await Db.SaveChangesAsync();
        return Ok(result);
    }
}
