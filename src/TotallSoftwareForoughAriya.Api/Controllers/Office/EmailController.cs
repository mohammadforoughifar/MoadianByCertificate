using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Office;

/// <summary>
/// ایمیل سازمانی (پست الکترونیک) — ماژول دسترسی: Email
/// حساب‌های شخصی هر کاربر + حساب‌های رسمی دبیرخانه (جدا از شخصی)
/// • حساب‌ها: CRUD + تست اتصال (SMTP/IMAP)
/// • ارسال با SMTP | دریافت با IMAP | بایگانی پوشه‌ای | نشان‌کردن
/// </summary>
[Route("api/email")]
public class EmailController : RbacControllerBase
{
    private const string Module = "Email";
    private readonly IEmailService _email;

    public EmailController(AppDbContext db, IEmailService email) : base(db)
    {
        _email = email;
    }

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");

    // دسترسی «ایمیل شخصی» جدا از دسترسی حساب‌های رسمی/دبیرخانه است.
    private async Task<IActionResult?> PersonalAccessAsync(bool personal)
    {
        if (personal && !await HasAsync(Module, "Personal") && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما به ایمیل‌های شخصی دسترسی ندارید." });
        return null;
    }

    /// <summary>دسترسی دبیرخانه صادره — برای مدیریت حساب‌های رسمی دبیرخانه</summary>
    /// <summary>
    /// تجزیهٔ تاریخِ فیلتر مستقل از کالچرِ سرور.
    /// قبلاً این پارامتر مستقیماً DateTime? بود و اگر سیستم‌عامل/سرویس با کالچرِ
    /// غیرمیلادی اجرا می‌شد، «2026-10-25» بی‌صدا null می‌شد و فیلترِ تاریخ
    /// هیچ اثری نداشت. حالا فقط قالب‌های صریح پذیرفته می‌شوند (yyyy-MM-dd یا ISO).
    /// </summary>
    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var t = s.IndexOf('T');
        if (t > 0) s = s[..t];
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var exact)) return exact;
        return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var loose) ? loose : null;
    }

    private async Task<bool> IsDabirkhaneAsync() =>
        await HasAsync("OutgoingLetters", "Dabirkhane") || await HasAsync(Module, "Delete");

    // ==================== حساب‌های ایمیل ====================

    /// <summary>حساب‌های من (شخصی) + حساب‌های دبیرخانه در صورت داشتن دسترسی</summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _email.GetMyAccountsAsync(MyUserId, await IsDabirkhaneAsync(), pagination: pagination), skip, take));
    }

    /// <summary>حساب‌های فعال دبیرخانه — برای انتخاب در ثبت دبیرخانه نامه صادره</summary>
    [HttpGet("dabirkhane-accounts")]
    public async Task<IActionResult> DabirkhaneAccounts([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به حساب‌های دبیرخانه دسترسی ندارید." });
        return Ok(await Paging.ResultAsync(async pagination => await _email.GetDabirkhaneAccountsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> SaveAccount([FromBody] SaveEmailAccountDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _email.SaveAccountAsync(dto, MyUserId, await IsDabirkhaneAsync());
        return Ok(new { id, message = "حساب ایمیل ذخیره شد." });
    }

    [HttpDelete("accounts/{emailId:int}")]
    public async Task<IActionResult> DeleteAccount(int emailId)
    {
        await _email.DeleteAccountAsync(emailId, MyUserId);
        return Ok(new { message = "حساب ایمیل حذف شد." });
    }

    /// <summary>تست اتصال SMTP و IMAP — بدون ذخیره حساب</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] SaveEmailAccountDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        return Ok(await _email.TestAccountAsync(dto));
    }

    // ==================== ارسال و دریافت ====================

    /// <summary>ارسال ایمیل از حساب کاربر (یا حساب دبیرخانه) — multipart با پیوست</summary>
    [HttpPost("send")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Send([FromForm] int emailAccountId, [FromForm] string to, [FromForm] string? cc,
        [FromForm] string subject, [FromForm] string? body, List<IFormFile> files)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;

        var attachments = new List<(string, string, byte[])>();
        foreach (var f in files ?? new List<IFormFile>())
        {
            if (f == null || f.Length <= 0) continue;
            if (f.Length > 20 * 1024 * 1024)
                return BadRequest(new { message = $"حجم فایل {f.FileName} بیش از ۲۰ مگابایت است." });
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            attachments.Add((f.FileName, f.ContentType ?? "application/octet-stream", ms.ToArray()));
        }

        await _email.SendAsync(new EmailComposeDto
        {
            EmailAccountId = emailAccountId,
            To = to,
            Cc = cc,
            Subject = subject,
            Body = body ?? ""
        }, MyUserId, await IsDabirkhaneAsync(), attachments.ToArray());
        return Ok(new { message = "ایمیل ارسال شد." });
    }

    /// <summary>همگام‌سازی صندوق (IMAP) — box: Inbox | Sent | Junk (پیش‌فرض: Inbox)</summary>
    [HttpPost("accounts/{emailId:int}/sync")]
    public async Task<IActionResult> Sync(int emailId, [FromQuery] string? box = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var r = await _email.SyncAsync(emailId, MyUserId, await IsDabirkhaneAsync(), box ?? "Inbox");
        return Ok(r);
    }

    /// <summary>همگام‌سازیِ افزایشیِ صندوقِ دریافتیِ همه‌ی حساب‌های کاربر.
    /// بارِ اول همه‌ی پیام‌ها را در بانک می‌نشاند و از بارِ دوم فقط پیام‌های جدید را می‌گیرد.</summary>
    [HttpPost("sync-inbox")]
    public async Task<IActionResult> SyncInbox([FromQuery] bool personal = false)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (await PersonalAccessAsync(personal) is { } personalForbid) return personalForbid;
        return Ok(await _email.SyncInboxAsync(MyUserId, await IsDabirkhaneAsync(), personal));
    }

    /// <summary>همگام‌سازیِ افزایشیِ صندوقِ ارسالیِ همه‌ی حساب‌های کاربر</summary>
    [HttpPost("sync-sent")]
    public async Task<IActionResult> SyncSent([FromQuery] bool personal = false)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (await PersonalAccessAsync(personal) is { } personalForbid) return personalForbid;
        return Ok(await _email.SyncSentAsync(MyUserId, await IsDabirkhaneAsync(), personal));
    }

    /// <summary>همگام‌سازیِ افزایشیِ پوشه‌ی هرزنامه (Junk/Spam) روی سرورِ IMAP</summary>
    [HttpPost("sync-junk")]
    public async Task<IActionResult> SyncJunk([FromQuery] bool personal = false)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (await PersonalAccessAsync(personal) is { } personalForbid) return personalForbid;
        return Ok(await _email.SyncJunkAsync(MyUserId, await IsDabirkhaneAsync(), personal));
    }

    /// <summary>
    /// صندوقِ دریافتی — صفحه‌بندی کاملاً سمت سرور (Skip/Take روی بانک).
    /// folderId = 0 → بایگانی‌نشده | -1 → بایگانی‌شده در هر پوشه‌ای | n → پوشه n | خالی → همه
    /// spam=true → فقط هرزنامه‌ها | starred=true → فقط نشان‌شده‌ها | personal=true → فقط حساب‌های شخصی
    /// </summary>
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox([FromQuery] int? emailId, [FromQuery] string? search, [FromQuery] bool? unreadOnly,
        [FromQuery] int? folderId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] bool spam = false, [FromQuery] bool starred = false, [FromQuery] bool personal = false,
        [FromQuery] string? dateFrom = null, [FromQuery] string? dateTo = null, [FromQuery] bool? hasAttachment = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (await PersonalAccessAsync(personal) is { } personalForbid) return personalForbid;
        return Ok(await _email.GetInboxAsync(MyUserId, emailId, search, unreadOnly, await IsDabirkhaneAsync(),
            personal, page, pageSize, folderId, spam, starred, ParseDate(dateFrom), ParseDate(dateTo), hasAttachment, isRead));
    }

    /// <summary>صندوقِ ارسالی — صفحه‌بندی کاملاً سمت سرور</summary>
    [HttpGet("sent")]
    public async Task<IActionResult> Sent([FromQuery] int? emailId, [FromQuery] string? search,
        [FromQuery] int? folderId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] bool starred = false, [FromQuery] bool personal = false,
        [FromQuery] string? dateFrom = null, [FromQuery] string? dateTo = null, [FromQuery] bool? hasAttachment = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (await PersonalAccessAsync(personal) is { } personalForbid) return personalForbid;
        return Ok(await _email.GetSentAsync(MyUserId, emailId, search, await IsDabirkhaneAsync(),
            personal, page, pageSize, folderId, starred, ParseDate(dateFrom), ParseDate(dateTo), hasAttachment, isRead));
    }

    /// <summary>بایگانی (دریافتی + ارسالیِ داخل پوشه‌ها) — صفحه‌بندی کاملاً سمت سرور</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] int? emailId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] bool personal = false,
        [FromQuery] string? dateFrom = null, [FromQuery] string? dateTo = null,
        [FromQuery] bool? hasAttachment = null, [FromQuery] bool? unreadOnly = null, [FromQuery] bool? isRead = null,
        [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (await PersonalAccessAsync(personal) is { } personalForbid) return personalForbid;
        return Ok(await _email.GetArchiveAsync(MyUserId, emailId, search, await IsDabirkhaneAsync(),
            personal, page, pageSize, ParseDate(dateFrom), ParseDate(dateTo), hasAttachment, unreadOnly, isRead));
    }

    /// <summary>جزئیات ایمیل — box: Inbox یا Sent (دریافتی خودکار خوانده‌شده می‌شود)</summary>
    [HttpGet("message/{box}/{id:int}")]
    public async Task<IActionResult> Message(string box, int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _email.GetMessageAsync(box, id, MyUserId, markRead: true, await IsDabirkhaneAsync()));
    }

    [HttpPost("message/{box}/{id:int}/read")]
    public async Task<IActionResult> MarkRead(string box, int id)
    {
        await _email.MarkReadAsync(box, id, MyUserId, await IsDabirkhaneAsync());
        return Ok();
    }

    /// <summary>نشان‌کردن (ستاره) ایمیل</summary>
    [HttpPost("message/{box}/{id:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(string box, int id)
    {
        var isNeshan = await _email.ToggleNeshanAsync(box, id, MyUserId, await IsDabirkhaneAsync());
        return Ok(new { isNeshan });
    }

    /// <summary>نشان‌کردنِ ایمیل به‌عنوان هرزنامه — body: {"spam": true|false}</summary>
    [HttpPost("message/{box}/{id:int}/spam")]
    public async Task<IActionResult> MarkSpam(string box, int id, [FromBody] EmailSpamDto dto)
    {
        var isSpam = await _email.MarkSpamAsync(box, id, MyUserId, await IsDabirkhaneAsync(), dto?.Spam ?? true);
        return Ok(new { isSpam });
    }

    /// <summary>بایگانی ایمیل در پوشه — FolderId=0 یعنی خروج از بایگانی</summary>
    [HttpPost("archive")]
    public async Task<IActionResult> Archive([FromBody] EmailArchiveDto dto)
    {
        try
        {
            await _email.ArchiveAsync(dto, MyUserId, await IsDabirkhaneAsync());
            return Ok(new { message = dto.FolderId > 0 ? "ایمیل بایگانی شد." : "از بایگانی خارج شد." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception) { return BadRequest(new { message = "بایگانی ایمیل انجام نشد؛ ایمیل یا پوشه انتخابی را بررسی کنید." }); }
    }

    /// <summary>دانلود پیوست ایمیل — فقط صاحب حساب (یا دبیرخانه برای حساب رسمی)</summary>
    [HttpGet("attachments/{attachmentId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int attachmentId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var bytes = await _email.ReadAttachmentAsync(attachmentId, MyUserId, await IsDabirkhaneAsync());
        if (bytes == null) return NotFound(new { message = "فایل پیوست پیدا نشد." });
        var att = await Db.OtoEmailAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);
        return File(bytes, "application/octet-stream", att?.AttachmentRealName ?? "attachment");
    }

    // ==================== پوشه‌های بایگانی ====================

    [HttpGet("folders")]
    public async Task<IActionResult> Folders([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _email.GetFoldersAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpPost("folders")]
    public async Task<IActionResult> SaveFolder([FromBody] SaveEmailFolderDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _email.SaveFolderAsync(dto, MyUserId);
        return Ok(new { id, message = "پوشه ذخیره شد." });
    }

    [HttpDelete("folders/{folderId:int}")]
    public async Task<IActionResult> DeleteFolder(int folderId)
    {
        await _email.DeleteFolderAsync(folderId, MyUserId);
        return Ok(new { message = "پوشه حذف شد." });
    }
}
