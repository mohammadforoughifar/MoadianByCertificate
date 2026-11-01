using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Outgoing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Inventory.Api.Services.Office.Email;

// ============================================================
//  سرویس ایمیل سازمانی — ارسال با SMTP و دریافت با IMAP (MailKit)
//  • حساب‌های شخصی هر کاربر + حساب‌های رسمی دبیرخانه (IsDabirkhane)
//  • پشتیبانی Gmail / Yahoo / Outlook / وب‌میل هاست / سفارشی
//  • پیوست‌ها روی دیسک: wwwroot/فایل های ایمیل/{Inbox|Sent}_{id}/
//  • ارسال نامه صادره از دبیرخانه با پست الکترونیک (PDF نامه روی سربرگ + پیوست‌ها)
// ============================================================

public interface IEmailService
{
    // ---------- حساب‌ها ----------
    Task<List<EmailAccountDto>> GetMyAccountsAsync(int userId, bool isDabirkhaneAdmin, Paging.Request? pagination = null);
    Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync(Paging.Request? pagination = null);
    Task<int> SaveAccountAsync(SaveEmailAccountDto dto, int userId, bool isDabirkhaneAdmin);
    Task DeleteAccountAsync(int emailId, int userId);
    Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto);

    // ---------- ارسال ----------
    Task<int> SendAsync(EmailComposeDto dto, int userId, bool isDabirkhaneAdmin, (string Name, string ContentType, byte[] Data)[] attachments);
    Task<int> SendLetterEmailAsync(int letterId, DabirkhaneRegisterDto dto, string userName);

    // ---------- دریافت ----------
    /// <summary>همگام‌سازیِ یک حساب و یک صندوق مشخص (Inbox | Sent | Junk)</summary>
    Task<EmailSyncResultDto> SyncAsync(int emailId, int userId, bool isDabirkhaneAdmin, string box = "Inbox");

    /// <summary>همگام‌سازیِ افزایشیِ صندوقِ دریافتیِ همه‌ی حساب‌های کاربر</summary>
    Task<EmailSyncResultDto> SyncInboxAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false);

    /// <summary>همگام‌سازیِ افزایشیِ صندوقِ ارسالیِ همه‌ی حساب‌های کاربر</summary>
    Task<EmailSyncResultDto> SyncSentAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false);

    /// <summary>همگام‌سازیِ افزایشیِ پوشه‌ی هرزنامه (Junk / Spam) همه‌ی حساب‌های کاربر</summary>
    Task<EmailSyncResultDto> SyncJunkAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false);

    /// <summary>صندوقِ دریافتی — صفحه‌بندی کاملاً سمت سرور.
    /// personalOnly: فقط حساب‌های شخصیِ خود کاربر (بدون حساب‌های رسمی دبیرخانه)</summary>
    Task<EmailListPageDto> GetInboxAsync(int userId, int? emailId, string? search, bool? unreadOnly,
        bool isDabirkhaneAdmin, bool personalOnly, int page = 1, int pageSize = 25,
        int? folderId = null, bool spamOnly = false, bool starredOnly = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null);

    /// <summary>صندوقِ ارسالی — صفحه‌بندی کاملاً سمت سرور</summary>
    Task<EmailListPageDto> GetSentAsync(int userId, int? emailId, string? search,
        bool isDabirkhaneAdmin, bool personalOnly, int page = 1, int pageSize = 25,
        int? folderId = null, bool starredOnly = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null);

    /// <summary>بایگانی (دریافتی + ارسالیِ داخل پوشه) — صفحه‌بندی سمت سرور
    /// با همان فیلترهای تاریخ/خوانده‌شدن/پیوستِ صندوق‌ها (نوار فیلترِ مشترکِ صفحه).</summary>
    Task<EmailListPageDto> GetArchiveAsync(int userId, int? emailId, string? search,
        bool isDabirkhaneAdmin, bool personalOnly, int page = 1, int pageSize = 25,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null,
        bool? unreadOnly = null, bool? isRead = null);

    Task<EmailMessageDetailDto> GetMessageAsync(string box, int id, int userId, bool markRead, bool isDabirkhaneAdmin);
    Task MarkReadAsync(string box, int id, int userId, bool isDabirkhaneAdmin);
    Task<bool> ToggleNeshanAsync(string box, int id, int userId, bool isDabirkhaneAdmin);

    /// <summary>نشان‌کردنِ یک ایمیل به‌عنوان هرزنامه / یا خروج از هرزنامه</summary>
    Task<bool> MarkSpamAsync(string box, int id, int userId, bool isDabirkhaneAdmin, bool spam);

    Task ArchiveAsync(EmailArchiveDto dto, int userId, bool isDabirkhaneAdmin);
    Task<byte[]?> ReadAttachmentAsync(int attachmentId, int userId, bool isDabirkhaneAdmin);

    // ---------- پوشه‌های بایگانی ----------
    Task<List<EmailFolderDto>> GetFoldersAsync(int userId, Paging.Request? pagination = null);
    Task<int> SaveFolderAsync(SaveEmailFolderDto dto, int userId);
    Task DeleteFolderAsync(int folderId, int userId);
}

public class EmailService : IEmailService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailService> _log;
    private readonly IOutgoingLetterPrintService _print;

    /// <summary>پوشه ذخیره پیوست‌های ایمیل تحت wwwroot/uploads/office/email</summary>
    private const string AttachmentFolder = "uploads/office/email";

    /// <summary>بیشینه پیام‌های خوانده‌شده از یک حساب در هر نوبت.
    /// برای جلوگیری از قطع‌شدن درخواست؛ نوبتِ بعدی از همان شناسه ادامه می‌دهد.</summary>
    private const int MaxMessagesPerAccountPerSync = 700;

    /// <summary>سقفِ پیام‌هایی که در یک درخواستِ همگام‌سازی پردازش می‌شود.
    /// بقیه در نوبت‌های بعد (از همان UID ادامه) گرفته می‌شوند تا درخواست بی‌پاسخ نماند.</summary>
    private const int MaxMessagesPerSyncRequest = 400;

    /// <summary>تعداد پیام در هر دسته‌ی ذخیره‌سازی — به‌جای دو SaveChanges به‌ازای هر پیام</summary>
    private const int SyncSaveBatchSize = 25;

    public EmailService(AppDbContext db, IWebHostEnvironment env, ILogger<EmailService> log, IOutgoingLetterPrintService print)
    {
        _db = db;
        _env = env;
        _log = log;
        _print = print;
    }

    // ==================== ابزارهای مشترک ====================

    /// <summary>
    /// تاریخِ پیام‌ها بر پایه‌ی ساعتِ ایران ذخیره و مقایسه می‌شود، نه ساعتِ سرور.
    /// اگر تاریخ با ساعتِ سرور (مثلاً UTC) ذخیره شود، ایمیل‌های بعد از نیمه‌شبِ تهران
    /// در روزِ قبل می‌افتند و فیلترِ تاریخ (و ستونِ تاریخ) یک روز جابه‌جا می‌شود.
    /// </summary>
    private static readonly TimeZoneInfo IranTz = ResolveIranTz();

    private static TimeZoneInfo ResolveIranTz()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Iran+0330", TimeSpan.FromMinutes(210), "Iran", "Iran");
    }

    /// <summary>تبدیل زمانِ پیام (DateTimeOffset) به ساعتِ ایران</summary>
    private static DateTime IranTime(DateTimeOffset dto) =>
        TimeZoneInfo.ConvertTime(dto, IranTz).DateTime;

    /// <summary>اکنون به وقتِ ایران</summary>
    private static DateTime IranNow() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IranTz).DateTime;

    private static string NormalizeBox(string? box) => box?.Trim() switch
    {
        "Sent" or "sent" => "Sent",
        "Junk" or "junk" or "Spam" or "spam" => "Junk",
        _ => "Inbox"
    };

    /// <summary>قفلِ همگام‌سازیِ هر حساب — دو مسئولِ دبیرخانه نباید هم‌زمان یک صندوق را
    /// بخوانند (وگرنه پیامِ تکراری یا UID پرش‌کرده پیش می‌آید).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> SyncGates = new();

    private static SemaphoreSlim SyncGateOf(int emailId) =>
        SyncGates.GetOrAdd(emailId, _ => new SemaphoreSlim(1, 1));

    private string AttachmentRoot =>
        Path.Combine(_env.ContentRootPath, "wwwroot", AttachmentFolder);

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in Path.GetFileName(name ?? "file"))
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var s = sb.ToString();
        return string.IsNullOrWhiteSpace(s) ? "file" : s.Length > 100 ? s[..100] : s;
    }

    private static string MimeToExtensionType(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)) return contentType;
        var ext = (Path.GetExtension(fileName) ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    /// <summary>گزینه‌های امنیتی SMTP بر اساس پورت/تنظیم کاربر</summary>
    private static SecureSocketOptions SmtpOptions(int port, bool ssl) =>
        !ssl ? SecureSocketOptions.None
        : port == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTlsWhenAvailable;

    /// <summary>تجزیه رشته گیرندگان «a@b.com، c@d.com» به لیست آدرس معتبر</summary>
    private static List<MailboxAddress> ParseAddresses(string? raw)
    {
        var list = new List<MailboxAddress>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        var parts = raw.Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (s.Length == 0) continue;
            if (MailboxAddress.TryParse(s, out var addr)) list.Add(addr);
        }
        return list;
    }

    private static string? ExtractAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var at = raw.IndexOf('@');
        if (at < 0) return raw.Trim();
        var start = raw.LastIndexOf('<', at);
        var end = raw.IndexOf('>', at);
        if (start >= 0 && end > start) return raw[(start + 1)..end].Trim();
        return raw.Trim();
    }

    /// <summary>آیا کاربر اجازه دسترسی به این حساب را دارد؟ (مالک، یا حساب دبیرخانه با دسترسی دبیرخانه)</summary>
    private async Task<OtoEmail?> AccessibleAccountAsync(int emailId, int userId, bool isDabirkhaneAdmin)
    {
        var acc = await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == emailId);
        if (acc == null) return null;
        if (acc.UserId == userId) return acc;
        if (acc.IsDabirkhane && isDabirkhaneAdmin) return acc;
        return null;
    }

    // ==================== حساب‌ها ====================

    public async Task<List<EmailAccountDto>> GetMyAccountsAsync(int userId, bool isDabirkhaneAdmin, Paging.Request? pagination = null)
    {
        // حساب‌های شخصی کاربر + (برای دستیار دبیرخانه) حساب‌های رسمی دبیرخانه
        var q = _db.OtoEmails.AsNoTracking()
            .Include(e => e.User)
            .Where(e => e.UserId == userId || (e.IsDabirkhane && isDabirkhaneAdmin));

        return await q.OrderBy(e => e.IsDabirkhane).ThenBy(e => e.EmailId)
            .Select(e => new EmailAccountDto
            {
                EmailId = e.EmailId,
                EmailAddress = e.EmailAddress,
                Smtp = e.Smtp,
                Imap = e.Imap,
                SmtpPort = e.SmtpPort,
                SmtpSsl = e.SmtpSsl,
                ImapPort = e.ImapPort,
                ImapSsl = e.ImapSsl,
                EmailType = e.EmailType,
                IsActive = e.IsActive,
                IsDabirkhane = e.IsDabirkhane,
                DisplayName = e.DisplayName,
                LastSync = e.LastSync,
                // تازه‌ترین پیامِ حساب (دریافتی یا ارسالی) — مبنای انتخابِ پیش‌فرضِ حساب در صفحهٔ ایمیل.
                // هر دو عدد در همان کوئری محاسبه می‌شوند تا برای هر حساب رفت‌وبرگشت جدا لازم نباشد.
                LastMessageAt = _db.OtoInboxEmails.Where(m => m.EmailId == e.EmailId)
                                   .Max(m => (DateTime?)m.Date)
                               ?? _db.OtoSentEmails.Where(m => m.EmailId == e.EmailId)
                                   .Max(m => (DateTime?)m.Date),
                MessageCount = _db.OtoInboxEmails.Count(m => m.EmailId == e.EmailId)
                             + _db.OtoSentEmails.Count(m => m.EmailId == e.EmailId),
                UserId = e.UserId,
                UserName = e.User != null
                    ? (string.IsNullOrWhiteSpace((e.User.FirstName ?? "") + (e.User.LastName ?? "")) ? e.User.Username : ((e.User.FirstName ?? "") + " " + (e.User.LastName ?? "")).Trim())
                    : "",
                IsMine = e.UserId == userId
            }).ToPageListAsync(pagination);
    }

    public async Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync(Paging.Request? pagination = null)
    {
        return await _db.OtoEmails.AsNoTracking()
            .Where(e => e.IsDabirkhane && e.IsActive)
            .OrderBy(e => e.EmailId)
            .Select(e => new EmailAccountDto
            {
                EmailId = e.EmailId,
                EmailAddress = e.EmailAddress,
                EmailType = e.EmailType,
                DisplayName = e.DisplayName,
                IsActive = e.IsActive,
                IsDabirkhane = true
            }).ToPageListAsync(pagination);
    }

    public async Task<int> SaveAccountAsync(SaveEmailAccountDto dto, int userId, bool isDabirkhaneAdmin)
    {
        if (string.IsNullOrWhiteSpace(dto.EmailAddress)) throw new Exception("آدرس ایمیل الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Password) && dto.EmailId == 0)
            throw new Exception("رمز عبور (App Password) الزامی است.");

        // هشدار مهم: رمز حساب باید App Password باشد نه رمز اصلی جیمیل/یاهو
        OtoEmail acc;
        if (dto.EmailId > 0)
        {
            acc = await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == dto.EmailId)
                ?? throw new Exception("حساب ایمیل پیدا نشد.");
            if (acc.UserId != userId) throw new Exception("فقط مالک حساب می‌تواند آن را ویرایش کند.");
        }
        else
        {
            // جلوگیری از ثبت تکراری آدرس برای یک کاربر
            var dup = await _db.OtoEmails.AnyAsync(e => e.UserId == userId && e.EmailAddress == dto.EmailAddress.Trim());
            if (dup) throw new Exception("این آدرس ایمیل قبلاً برای شما ثبت شده است.");
            acc = new OtoEmail { UserId = userId };
            _db.OtoEmails.Add(acc);
        }

        acc.EmailAddress = dto.EmailAddress.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Password)) acc.Password = dto.Password.Trim();
        acc.EmailType = string.IsNullOrWhiteSpace(dto.EmailType) ? "Custom" : dto.EmailType.Trim();
        acc.Smtp = dto.Smtp?.Trim() ?? "";
        acc.Imap = dto.Imap?.Trim() ?? "";
        acc.SmtpPort = dto.SmtpPort > 0 ? dto.SmtpPort : 587;
        acc.SmtpSsl = dto.SmtpSsl;
        acc.ImapPort = dto.ImapPort > 0 ? dto.ImapPort : 993;
        acc.ImapSsl = dto.ImapSsl;
        acc.DisplayName = dto.DisplayName?.Trim();
        acc.IsActive = dto.IsActive;
        // تفکیکِ ایمیل سازمانیِ دبیرخانه از ایمیل شخصی:
        // نشانِ «حساب رسمی دبیرخانه» تنها زمانی پذیرفته می‌شود که کاربر واقعاً
        // دسترسیِ دبیرخانه را داشته باشد؛ در غیر این صورت حساب شخصیِ او می‌ماند
        // و هرگز در فهرستِ حساب‌های رسمیِ دبیرخانه ظاهر نمی‌شود.
        acc.IsDabirkhane = isDabirkhaneAdmin && dto.IsDabirkhane;
        acc.ActivationCode = Guid.NewGuid().ToString("N")[..16];

        await _db.SaveChangesAsync();
        return acc.EmailId;
    }

    public async Task DeleteAccountAsync(int emailId, int userId)
    {
        var acc = await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == emailId)
            ?? throw new Exception("حساب ایمیل پیدا نشد.");
        if (acc.UserId != userId) throw new Exception("فقط مالک حساب می‌تواند آن را حذف کند.");

        // پیوست‌های روی دیسک هم پاک می‌شوند
        var ids = new { inboxIds = await _db.OtoInboxEmails.Where(i => i.EmailId == emailId).Select(i => i.InboxId).ToListAsync(),
                        sentIds = await _db.OtoSentEmails.Where(s => s.EmailId == emailId).Select(s => s.SentId).ToListAsync() };
        foreach (var i in ids.inboxIds) DeleteAttachmentFiles("Inbox", i);
        foreach (var s in ids.sentIds) DeleteAttachmentFiles("Sent", s);

        // ردیف‌های پیوست (بدون FK) هم حذف می‌شوند — پیام‌ها با Cascade حذف می‌شوند
        await _db.OtoEmailAttachments
            .Where(a => (a.Type == "Inbox" && ids.inboxIds.Contains(a.EmailId)) ||
                        (a.Type == "Sent" && ids.sentIds.Contains(a.EmailId)))
            .ExecuteDeleteAsync();

        _db.OtoEmails.Remove(acc);
        await _db.SaveChangesAsync();
    }

    /// <summary>تست اتصال SMTP و IMAP — قبل از ذخیره، مقادیر پیشنهادی فرم را تست می‌کند</summary>
    public async Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto)
    {
        var result = new EmailTestResultDto();
        var smtpHost = dto.Smtp?.Trim();
        var imapHost = dto.Imap?.Trim();

        // ---------- SMTP ----------
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            result.Message = "سرور SMTP مشخص نشده است.";
            return result;
        }
        try
        {
            using var client = new SmtpClient { Timeout = 15000 };
            await client.ConnectAsync(smtpHost, dto.SmtpPort > 0 ? dto.SmtpPort : 587, SmtpOptions(dto.SmtpPort, dto.SmtpSsl));
            await client.AuthenticateAsync(dto.EmailAddress.Trim(), dto.Password);
            await client.DisconnectAsync(true);
            result.SmtpOk = true;
        }
        catch (Exception ex)
        {
            result.Message = $"SMTP: {ex.Message}";
            return result;
        }

        // ---------- IMAP (اختیاری) ----------
        if (!string.IsNullOrWhiteSpace(imapHost))
        {
            try
            {
                using var client = new ImapClient { Timeout = 15000 };
                await client.ConnectAsync(imapHost, dto.ImapPort > 0 ? dto.ImapPort : 993,
                    dto.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None);
                await client.AuthenticateAsync(dto.EmailAddress.Trim(), dto.Password);
                await client.DisconnectAsync(true);
                result.ImapOk = true;
            }
            catch (Exception ex)
            {
                result.Message = $"SMTP سالم است — IMAP: {ex.Message}";
                return result;
            }
        }
        else
        {
            result.ImapOk = true; // دریافت تنظیم نشده — فقط ارسال
        }

        result.Message = "اتصال موفق بود ✓" +
            (dto.EmailType == "Gmail" ? " — یادآوری: برای Gmail از App Password استفاده کنید." : "");
        return result;
    }

    // ==================== ارسال ایمیل (SMTP) ====================

    public async Task<int> SendAsync(EmailComposeDto dto, int userId, bool isDabirkhaneAdmin, (string Name, string ContentType, byte[] Data)[] attachments)
    {
        var acc = await AccessibleAccountAsync(dto.EmailAccountId, userId, isDabirkhaneAdmin)
            ?? throw new Exception("حساب ایمیل فرستنده پیدا نشد یا به شما تعلق ندارد.");
        if (!acc.IsActive) throw new Exception("حساب ایمیل غیرفعال است.");

        var toList = ParseAddresses(dto.To);
        if (toList.Count == 0) throw new Exception("هیچ آدرس گیرنده معتبری وارد نشده است.");

        var sent = new OtoSentEmail
        {
            EmailId = acc.EmailId,
            UId = "",
            ToDisplay = string.Join("، ", toList.Select(t => t.Address)),
            Subject = dto.Subject?.Trim() ?? "",
            Date = IranNow(),
            Body = dto.Body ?? "",
            IsAttachment = false
        };
        _db.OtoSentEmails.Add(sent);
        await _db.SaveChangesAsync();

        var files = await BuildAndSendAsync(acc, toList, ParseAddresses(dto.Cc), dto.Subject?.Trim() ?? "", dto.Body ?? "",
            attachments ?? Array.Empty<(string, string, byte[])>(), sent.SentId);

        sent.UId = files.uid;
        sent.IsAttachment = files.hasAtt;
        await _db.SaveChangesAsync();

        return sent.SentId;
    }

    /// <summary>ساخت پیام Mime و ارسال با SMTP + ذخیره پیوست روی دیسک</summary>
    private async Task<(string uid, bool hasAtt)> BuildAndSendAsync(
        OtoEmail acc, List<MailboxAddress> to, List<MailboxAddress> cc,
        string subject, string htmlBody, (string Name, string ContentType, byte[] Data)[] attachments, int sentId)
    {
        var msg = new MimeMessage();
        var fromName = string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.EmailAddress : acc.DisplayName;
        msg.From.Add(new MailboxAddress(fromName, acc.EmailAddress));
        msg.To.AddRange(to);
        if (cc.Count > 0) msg.Cc.AddRange(cc);
        msg.Subject = subject;
        msg.MessageId = $"<{Guid.NewGuid():N}@autmation.local>";

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var (name, ct, data) in attachments)
        {
            builder.Attachments.Add(name, data, ContentType.Parse(MimeToExtensionType(name, ct)));
        }
        msg.Body = builder.ToMessageBody();

        if (string.IsNullOrWhiteSpace(acc.Smtp))
            throw new Exception("سرور SMTP حساب ایمیل تنظیم نشده است — از بخش تنظیمات ایمیل تکمیل کنید.");

        string uid;
        using (var client = new SmtpClient { Timeout = 30000 })
        {
            await client.ConnectAsync(acc.Smtp, acc.SmtpPort, SmtpOptions(acc.SmtpPort, acc.SmtpSsl));
            await client.AuthenticateAsync(acc.EmailAddress, acc.Password);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
        uid = msg.MessageId ?? Guid.NewGuid().ToString("N");

        // ذخیره پیوست‌های ارسالی روی دیسک + جدول Oto_TBL_EmailAttachments
        foreach (var (name, ct, data) in attachments)
        {
            var dir = Path.Combine(AttachmentRoot, $"Sent_{sentId}");
            Directory.CreateDirectory(dir);
            var saved = $"{Guid.NewGuid():N}_{SafeName(name)}";
            await File.WriteAllBytesAsync(Path.Combine(dir, saved), data);
            _db.OtoEmailAttachments.Add(new OtoEmailAttachment
            {
                EmailId = sentId,
                UId = uid,
                Type = "Sent",
                AttachmentRealName = name,
                AttachmentSavedName = saved,
                FilePath = $"{AttachmentFolder}/Sent_{sentId}/{saved}"
            });
        }
        await _db.SaveChangesAsync();

        return (uid, attachments.Length > 0);
    }

    /// <summary>حذف فایل‌های پیوست یک پیام از دیسک</summary>
    private void DeleteAttachmentFiles(string box, int messageId)
    {
        var dir = Path.Combine(AttachmentRoot, $"{box}_{messageId}");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    // ==================== ارسال نامه صادره از دبیرخانه با پست الکترونیک ====================

    /// <summary>
    /// ارسال نامه صادره امضا شده به ایمیل مقصد:
    /// متن نامه در بدنه + PDF چاپ‌شده روی سربرگ شرکت + پیوست‌های نامه
    /// </summary>
    public async Task<int> SendLetterEmailAsync(int letterId, DabirkhaneRegisterDto dto, string userName)
    {
        var destEmail = dto.DestEmail?.Trim();
        if (string.IsNullOrWhiteSpace(destEmail))
            throw new Exception("آدرس ایمیل مقصد برای ارسال نامه الزامی است.");
        var toList = ParseAddresses(destEmail);
        if (toList.Count == 0) throw new Exception("آدرس ایمیل مقصد معتبر نیست.");

        // ---------- حساب دبیرخانه (جدا از ایمیل شخصی) ----------
        var acc = dto.EmailAccountId is > 0
            ? await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == dto.EmailAccountId && e.IsActive)
              ?? throw new Exception("حساب ایمیل دبیرخانه انتخاب‌شده پیدا نشد یا غیرفعال است.")
            : await _db.OtoEmails.FirstOrDefaultAsync(e => e.IsDabirkhane && e.IsActive)
              ?? throw new Exception("هیچ حساب ایمیل دبیرخانه فعالی ثبت نشده است — ابتدا از «تنظیمات ایمیل» حساب دبیرخانه را اضافه کنید.");
        if (!acc.IsDabirkhane)
            throw new Exception("برای ارسال نامه باید حساب رسمی دبیرخانه (IsDabirkhane) انتخاب شود — ایمیل شخصی مجاز نیست.");

        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Source)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");
        if (string.IsNullOrWhiteSpace(letter.SadereNumber))
            throw new Exception("فقط نامه‌های امضا شده (دارای شماره صادره) با ایمیل ارسال می‌شوند.");

        // ---------- بدنه ایمیل ----------
        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ToListAsync();
        static string Name(Data.User? u) => u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username ?? "" : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();

        var sb = new System.Text.StringBuilder();
        sb.Append(letter.Text ?? "");
        sb.Append("<hr style=\"border:none;border-top:1px solid #ccc;margin:18px 0\"/>");
        sb.Append("<div style=\"font-family:Tahoma;font-size:12px;color:#333;direction:rtl\">");
        sb.Append($"<p><b>شماره صادره:</b> {letter.SadereNumber} — <b>تاریخ صدور:</b> {PersianDate.ToShortDateTime(letter.DateSadere ?? letter.DateSabt)}</p>");
        sb.Append($"<p><b>گیرنده:</b> {letter.ReceiverOrganization}{(string.IsNullOrWhiteSpace(letter.ReceiverName) ? "" : " — " + letter.ReceiverName)}</p>");
        if (signers.Count > 0)
        {
            sb.Append("<p><b>امضا کنندگان:</b> ");
            sb.Append(string.Join("، ", signers.Select(s => Name(s.User) + (s.IsSigned ? " ✓" : ""))));
            sb.Append("</p>");
        }
        sb.Append("<p style=\"color:#777\">این نامه از طریق سامانه اتوماسیون اداری و درگاه ایمیل دبیرخانه ارسال شده است.</p>");
        sb.Append("</div>");

        // ---------- پیوست‌ها: PDF نامه + پیوست‌های نامه ----------
        var attachments = new List<(string, string, byte[])>();
        try
        {
            var pdf = await _print.GeneratePdfAsync(letterId, "A4");
            if (pdf is { Length: > 0 })
            {
                var pdfName = $"نامه-{letter.SadereNumber.Replace('/', '-')}.pdf";
                attachments.Add((pdfName, "application/pdf", pdf));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ساخت PDF نامه صادره {LetterId} برای ایمیل ناموفق بود", letterId);
        }

        var letterAtts = await _db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "OutgoingLetters" && a.RefId == letterId)
            .ToListAsync();
        foreach (var a in letterAtts)
        {
            var bytes = a.Data is { Length: > 0 } ? a.Data : ReadDiskFile(a.FilePath);
            if (bytes is { Length: > 0 }) attachments.Add((a.FileName, a.ContentType, bytes));
        }

        // ---------- ثبت و ارسال ----------
        var sent = new OtoSentEmail
        {
            EmailId = acc.EmailId,
            UId = "",
            ToDisplay = string.Join("، ", toList.Select(t => t.Address)),
            Subject = $"نامه شماره {letter.SadereNumber} — {letter.Title}",
            Date = DateTime.Now,
            Body = sb.ToString(),
            IsAttachment = attachments.Count > 0,
            LetterSourceId = letterId
        };
        _db.OtoSentEmails.Add(sent);
        await _db.SaveChangesAsync();

        var (uid, _) = await BuildAndSendAsync(acc, toList, new List<MailboxAddress>(),
            sent.Subject, sent.Body, attachments.ToArray(), sent.SentId);
        sent.UId = uid;
        await _db.SaveChangesAsync();
        return sent.SentId;
    }

    /// <summary>
    /// ریشه‌های محتمل wwwroot — در استقرار ویندوز سرور ممکن است «ریشهٔ محتوا» بین
    /// اجراها عوض شود (اجرا از پوشهٔ publish، Task Scheduler با CWD متفاوت، سرویس ویندوز)،
    /// در حالی که پیوست‌ها در ریشهٔ قبلی ذخیره شده‌اند. برای اینکه دانلود پیوست‌های
    /// قبلی با «پیوست پیدا نشد» شکست نخورد، همه ریشه‌های محتمل را — به ترتیب — می‌آزماییم.
    /// فقط ریشه‌های موجود و بدون تکرار برمی‌گردد.
    /// </summary>
    private IEnumerable<string> WwwrootCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = new List<string?>
        {
            _env.WebRootPath,
            Path.Combine(_env.ContentRootPath, "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
        };
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            var full = Path.GetFullPath(r);
            if (seen.Add(full) && Directory.Exists(full))
                yield return full;
        }
    }

    private string? ResolveDiskPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        // انواع مسیر نسبی: خود مسیر + تبدیل دوطرفهٔ پوشهٔ قدیمی («فایل های ایمیل») و جدید
        const string legacyPrefix = "فایل های ایمیل/";
        const string modernPrefix = "uploads/office/email/";
        var variants = new List<string> { clean };
        if (clean.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            variants.Add(modernPrefix + clean[legacyPrefix.Length..]);
        if (clean.StartsWith(modernPrefix, StringComparison.OrdinalIgnoreCase))
            variants.Add(legacyPrefix + clean[modernPrefix.Length..]);

        // ۱) جستجوی مستقیم در «همهٔ» ریشه‌های محتمل و همهٔ انواع مسیر
        foreach (var root in WwwrootCandidates())
        foreach (var v in variants)
        {
            var full = Path.GetFullPath(Path.Combine(root, v));
            if (full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full))
                return full;
        }

        // ۲) مسیر مجاز پیش‌فرض زیر ریشهٔ اصلی (قرارداد قبلی: حتی اگر فایل نباشد مسیر برگردد)
        var primary = WwwrootCandidates().FirstOrDefault()
                      ?? Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot"));
        var fallback = Path.GetFullPath(Path.Combine(primary, clean));
        return fallback.StartsWith(primary, StringComparison.Ordinal) ? fallback : null;
    }

    private byte[]? ReadDiskFile(string? relativePath)
    {
        var full = ResolveDiskPath(relativePath);
        if (full is null || !File.Exists(full)) return null;
        try { return File.ReadAllBytes(full); } catch { return null; }
    }

    // ==================== دریافت ایمیل (IMAP) ====================

    /// <summary>
    /// همگام‌سازیِ افزایشیِ صندوقِ دریافتیِ همه‌ی حساب‌های کاربر.
    /// بارِ اول همه‌ی پیام‌ها را می‌گیرد و در بانک می‌نشاند؛ از بارِ دوم،
    /// به‌کمک Last_Inbox_Uid فقط پیام‌های جدیدتر از آخرین نوبت خوانده می‌شوند.
    /// </summary>
    public async Task<EmailSyncResultDto> SyncInboxAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false)
        => await SyncAllAccountsAsync(userId, isDabirkhaneAdmin, "Inbox", personalOnly);

    /// <summary>مانند SyncInboxAsync ولی برای صندوقِ ارسالی (پیشرفت در Last_Sent_Uid)</summary>
    public async Task<EmailSyncResultDto> SyncSentAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false)
        => await SyncAllAccountsAsync(userId, isDabirkhaneAdmin, "Sent", personalOnly);

    /// <summary>
    /// همگام‌سازیِ افزایشیِ پوشه‌ی هرزنامه (Junk / Spam) همه‌ی حساب‌های کاربر.
    /// پیام‌ها در همان جدولِ دریافتی و با نشانِ Is_Spam ذخیره می‌شوند تا بشود
    /// آن‌ها را در بخش «اسپم» دید و در صورت اشتباه به صندوقِ دریافتی برگرداند.
    /// </summary>
    public async Task<EmailSyncResultDto> SyncJunkAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false)
        => await SyncAllAccountsAsync(userId, isDabirkhaneAdmin, "Junk", personalOnly);

    /// <summary>
    /// همگام‌سازیِ دستیِ یک حساب و یک صندوق (دکمه‌ی «دریافت ایمیل‌ها»).
    /// box: Inbox | Sent | Junk — قبلاً این دکمه همیشه صندوقِ دریافتی را همگام می‌کرد،
    /// پس در تبِ «ارسالی» و «اسپم» هیچ نتیجه‌ای دیده نمی‌شد.
    /// </summary>
    public async Task<EmailSyncResultDto> SyncAsync(int emailId, int userId, bool isDabirkhaneAdmin, string box = "Inbox")
    {
        var acc = await AccessibleAccountAsync(emailId, userId, isDabirkhaneAdmin)
            ?? throw new Exception("حساب ایمیل پیدا نشد یا به شما تعلق ندارد.");
        if (!acc.IsActive) throw new Exception("حساب ایمیل غیرفعال است.");

        box = NormalizeBox(box);
        var gate = SyncGateOf(acc.EmailId);
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(1)))
            return new EmailSyncResultDto
            {
                AccountCount = 0,
                Message = "همگام‌سازیِ این حساب همین حالا در حال اجراست؛ چند لحظه بعد دوباره تلاش کنید."
            };
        try
        {
            var r = await SyncAccountBoxAsync(acc, box, box == "Junk");
            var boxName = BoxTitle(box);
            if (string.IsNullOrWhiteSpace(r.Message))
                r.Message = r.NewCount > 0
                    ? $"{r.NewCount} ایمیل جدید از صندوق {boxName} دریافت شد."
                    : $"ایمیل جدیدی در صندوق {boxName} نبود.";
            return r;
        }
        finally { gate.Release(); }
    }

    /// <summary>همگام‌سازیِ همه‌ی حساب‌های در دسترس.
    /// personalOnly=true → صفحهٔ «ایمیل شخصی»: فقط حساب‌های شخصیِ خودِ کاربر
    /// (حساب‌های رسمیِ دبیرخانه همگام نمی‌شوند تا بار و دادهٔ اضافه نیاید).</summary>
    private async Task<EmailSyncResultDto> SyncAllAccountsAsync(int userId, bool isDabirkhaneAdmin, string box, bool personalOnly = false)
    {
        var result = new EmailSyncResultDto();
        var accIds = await MyAccountIdsAsync(userId, isDabirkhaneAdmin, personalOnly);
        if (accIds.Count == 0)
        {
            result.Message = "هیچ حساب ایمیلی برای شما تعریف نشده است.";
            return result;
        }

        var accounts = await _db.OtoEmails
            .Where(e => accIds.Contains(e.EmailId) && e.IsActive)
            .ToListAsync();

        // پوشه‌ی هرزنامه در خیلی از سرورها اصلاً وجود ندارد — نبودنش خطا نیست
        var tolerateMissing = box == "Junk";

        var errors = new List<string>();
        foreach (var acc in accounts)
        {
            // اگر مسئولِ دیگری همین حالا این حساب را همگام می‌کند، رد می‌شویم (جلوگیری از پیامِ تکراری)
            var gate = SyncGateOf(acc.EmailId);
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(1))) continue;
            try
            {
                var r = await SyncAccountBoxAsync(acc, box, tolerateMissing);
                result.NewCount += r.NewCount;
                result.TotalCount = r.TotalCount;
                result.AccountCount++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "همگام‌سازی {Box} برای حساب {Account} ناموفق بود", box, acc.EmailAddress);
                errors.Add($"{acc.EmailAddress}: {ex.Message}");
            }
            finally { gate.Release(); }
        }

        var boxName = box == "Sent" ? "ارسالی" : box == "Junk" ? "هرزنامه" : "دریافتی";
        if (errors.Count == 0)
        {
            result.Message = result.NewCount > 0
                ? $"{result.NewCount} ایمیل جدید از صندوق {boxName} دریافت شد."
                : $"ایمیل جدیدی در صندوق {boxName} نبود.";
        }
        else
        {
            result.Message = string.Join(" | ", errors);
        }
        return result;
    }

    /// <summary>
    /// هسته‌ی همگام‌سازیِ یک صندوق از یک حساب.
    /// • یک اتصالِ IMAP برای کلِ صندوق باز می‌شود (قبلاً برای هر دستهٔ ۷۰۰تایی اتصالِ تازه ساخته می‌شد)
    /// • ذخیره‌سازی دسته‌ای است (قبلاً هر پیام دو SaveChanges داشت و همگام‌سازی را چند برابر کند می‌کرد)
    /// • در هر درخواست حداکثر MaxMessagesPerSyncRequest پیام پردازش می‌شود تا کاربر معطل نماند
    /// box: Inbox | Sent | Junk (هرزنامه در جدولِ دریافتی با نشانِ Is_Spam ذخیره می‌شود)
    /// </summary>
    private async Task<EmailSyncResultDto> SyncAccountBoxAsync(OtoEmail acc, string box, bool tolerateMissing = false)
    {
        var res = new EmailSyncResultDto { AccountCount = 1 };
        box = NormalizeBox(box);
        // پیوست‌های هرزنامه در جدولِ پیوست با Type = Inbox ذخیره می‌شوند (مثل قبل)
        var attBox = box == "Sent" ? "Sent" : "Inbox";

        if (string.IsNullOrWhiteSpace(acc.Imap))
            throw new Exception("سرور IMAP این حساب تنظیم نشده است — از تنظیمات ایمیل تکمیل کنید.");

        using var client = new ImapClient { Timeout = 30000 };
        await client.ConnectAsync(acc.Imap, acc.ImapPort,
            acc.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None);
        await client.AuthenticateAsync(acc.EmailAddress, acc.Password);

        var folder = OpenFolder(client, box);
        if (folder == null)
        {
            await client.DisconnectAsync(true);
            if (tolerateMissing) return res;
            throw new Exception($"پوشه‌ی {BoxTitle(box)} در این حساب یافت نشد.");
        }
        await folder.OpenAsync(FolderAccess.ReadOnly);

        res.TotalCount = folder.Count;
        long lastUid = box == "Sent" ? acc.LastSentUid : box == "Junk" ? acc.LastJunkUid : acc.LastInboxUid;

        // همه‌ی شناسه‌های صندوق؛ فقط آن‌هایی که از آخرین نوبت جدیدترند انتخاب می‌شوند.
        // مرتب‌سازی صعودی باعث می‌شود اگر در نوبتی به سقف برسیم، نوبتِ بعد دقیقاً از همان‌جا ادامه یابد.
        var summaries = await folder.FetchAsync(0, -1, MessageSummaryItems.UniqueId);
        var pending = summaries
            .Select(x => x.UniqueId)
            .Where(u => lastUid <= 0 || u.Id > (uint)lastUid)
            .OrderBy(u => u.Id)
            .Take(MaxMessagesPerAccountPerSync)
            .ToList();

        var existing = await ExistingKeysAsync(acc.EmailId, box);
        long maxUid = lastUid;
        int added = 0;
        int handled = 0;
        var batch = new List<PendingMessage>();

        foreach (var uid in pending)
        {
            if (handled >= MaxMessagesPerSyncRequest) break;
            handled++;

            MimeMessage msg;
            try { msg = await folder.GetMessageAsync(uid); }
            catch (Exception ex)
            {
                // خطای موقت IMAP نباید UID را جلو ببرد؛ در Sync بعدی دوباره تلاش می‌شود.
                _log.LogWarning(ex, "دریافت ایمیل با UID {Uid} ناموفق بود؛ در Sync بعدی دوباره امتحان می‌شود.", uid.Id);
                continue;
            }

            // شناسه‌ی یکتا برای جلوگیری از دریافتِ تکراری.
            // پیشوندِ junk- مخصوصِ پوشه‌ی هرزنامه است تا شماره‌های UID دو پوشه
            // (که مستقل از هم‌اند) با هم تداخل نکنند.
            var key = !string.IsNullOrWhiteSpace(msg.MessageId)
                ? msg.MessageId
                : box == "Junk" ? $"junk-uid-{acc.EmailId}-{uid.Id}" : $"uid-{acc.EmailId}-{uid.Id}";
            if (key.Length > 300) key = key[^300..];

            if (existing.Contains(key))
            {
                // اگر ایمیل قبلاً ثبت شده اما پیوست‌هایش در یک Sync قبلی ذخیره نشده‌اند،
                // آن را دوباره از IMAP ترمیم می‌کنیم؛ صرفاً وجود رکورد ایمیل کافی نیست.
                if (HasExtractableAttachment(msg)
                    && await ExistingMessageIdAsync(acc.EmailId, box, key) is int existingId
                    && !await _db.OtoEmailAttachments.AnyAsync(x => x.EmailId == existingId && x.Type == attBox))
                {
                    await SaveAttachmentsAsync(msg, attBox, existingId, key);
                    _log.LogInformation("پیوست‌های ایمیل موجود ترمیم شد؛ حساب {EmailId}، صندوق {Box}، شناسه {MessageId}",
                        acc.EmailId, box, existingId);
                }
                // بدنهٔ ایمیل‌هایی که پیش از پشتیبانیِ تصویر درون‌متن دریافت شده‌اند هنوز
                // «cid:…» دارند و تصویرشان نمایش داده نمی‌شود؛ همان‌جا (پیام از IMAP
                // خوانده شده است) ارجاع‌ها به آدرسِ پیوست تبدیل می‌شوند.
                try { await RepairInlineBodyAsync(msg, box, await ExistingMessageIdAsync(acc.EmailId, box, key) ?? 0, key); }
                catch (Exception ex) { _log.LogWarning(ex, "ترمیم تصاویر درون‌متن ایمیل ناموفق بود؛ شناسه {MessageId}", key); }

                if (uid.Id > maxUid) maxUid = uid.Id;
                continue;
            }

            var body = msg.HtmlBody ?? BuildTextBodyHtml(msg.TextBody);
            // تاریخِ پیام به وقتِ ایران ذخیره می‌شود (نه ساعتِ سرور) تا فیلترِ تاریخ و
            // ستونِ تاریخ همان روزی را نشان دهند که کاربر در تقویمِ شمسی می‌بیند.
            var date = msg.Date == default(DateTimeOffset) ? IranNow() : IranTime(msg.Date);
            // پرچمِ «پیوست دارد» فقط برای پیوست واقعی؛ تصاویر درونِ متن نباید در فهرست پیوست‌ها بیایند
            var hasAttach = HasRealAttachment(msg);
            var subject = Trunc(msg.Subject ?? "", 500);

            if (box == "Sent")
            {
                var sent = new OtoSentEmail
                {
                    EmailId = acc.EmailId,
                    UId = key,
                    Subject = subject,
                    Date = date,
                    Body = body,
                    ToDisplay = RecipientsOf(msg),
                    IsAttachment = hasAttach
                };
                _db.OtoSentEmails.Add(sent);
                batch.Add(new PendingMessage(msg, key, null, sent));
            }
            else
            {
                var inbox = new OtoInboxEmail
                {
                    EmailId = acc.EmailId,
                    UId = key,
                    Subject = subject,
                    Date = date,
                    IsRead = false,
                    Body = body,
                    FromAddress = Trunc(ExtractAddress(msg.From.ToString()) ?? "", 250),
                    IsAttachment = hasAttach,
                    // پیام‌های پوشه‌ی هرزنامه با نشانِ اسپم ذخیره می‌شوند
                    IsSpam = box == "Junk"
                };
                _db.OtoInboxEmails.Add(inbox);
                batch.Add(new PendingMessage(msg, key, inbox, null));
            }

            existing.Add(key);
            added++;
            if (uid.Id > maxUid) maxUid = uid.Id;

            if (batch.Count >= SyncSaveBatchSize) await FlushPendingAsync(batch, attBox);
        }

        await FlushPendingAsync(batch, attBox);
        await client.DisconnectAsync(true);

        acc.LastSync = IranNow();
        if (box == "Sent") acc.LastSentUid = maxUid;
        else if (box == "Junk") acc.LastJunkUid = maxUid;
        else acc.LastInboxUid = maxUid;
        await _db.SaveChangesAsync();

        res.NewCount = added;
        if (pending.Count > handled)
            res.Message = $"{FaDigits(pending.Count - handled)} ایمیل دیگر در صف است — دوباره «دریافت» را بزنید.";
        return res;
    }

    /// <summary>یک پیامِ در انتظارِ ذخیره (برای ذخیره‌سازیِ دسته‌ای)</summary>
    private sealed class PendingMessage
    {
        public PendingMessage(MimeMessage msg, string key, OtoInboxEmail? inbox, OtoSentEmail? sent)
        {
            Msg = msg; Key = key; Inbox = inbox; Sent = sent;
        }

        public MimeMessage Msg { get; }
        public string Key { get; }
        public OtoInboxEmail? Inbox { get; }
        public OtoSentEmail? Sent { get; }
    }

    /// <summary>کلیدهای یکتای ذخیره‌شده‌ی یک حساب و صندوق — فقط ستون UId خوانده می‌شود</summary>
    private async Task<HashSet<string>> ExistingKeysAsync(int emailId, string box) =>
        box == "Sent"
            ? new HashSet<string>(await _db.OtoSentEmails.AsNoTracking()
                .Where(x => x.EmailId == emailId).Select(x => x.UId).ToListAsync())
            : new HashSet<string>(await _db.OtoInboxEmails.AsNoTracking()
                .Where(x => x.EmailId == emailId).Select(x => x.UId).ToListAsync());

    private async Task<int?> ExistingMessageIdAsync(int emailId, string box, string key) =>
        box == "Sent"
            ? await _db.OtoSentEmails.AsNoTracking()
                .Where(x => x.EmailId == emailId && x.UId == key).Select(x => (int?)x.SentId).FirstOrDefaultAsync()
            : await _db.OtoInboxEmails.AsNoTracking()
                .Where(x => x.EmailId == emailId && x.UId == key).Select(x => (int?)x.InboxId).FirstOrDefaultAsync();

    /// <summary>
    /// ذخیره‌ی یک دسته پیام + پیوست‌هایش با حداقل رفت‌وبرگشت به بانک:
    /// ابتدا همه‌ی پیام‌های دسته ذخیره می‌شوند (شناسه‌ها ساخته می‌شوند)، بعد پیوست‌ها.
    /// </summary>
    private async Task FlushPendingAsync(List<PendingMessage> batch, string attBox)
    {
        if (batch.Count == 0) return;
        await _db.SaveChangesAsync();
        foreach (var p in batch)
        {
            if (p.Sent != null)
            {
                var inline = await SaveAttachmentsAsync(p.Msg, "Sent", p.Sent.SentId, p.Key);
                if (inline.Count > 0) p.Sent.Body = ReplaceInlineImageReferences(p.Sent.Body, inline);
            }
            else if (p.Inbox != null)
            {
                var inline = await SaveAttachmentsAsync(p.Msg, attBox, p.Inbox.InboxId, p.Key);
                if (inline.Count > 0) p.Inbox.Body = ReplaceInlineImageReferences(p.Inbox.Body, inline);
            }
        }
        await _db.SaveChangesAsync();
        batch.Clear();
    }

    /// <summary>ارقامِ فارسی برای متنِ پیام‌ها (بدون وابستگی به کلاینت)</summary>
    private static string FaDigits(int n) =>
        n.ToString(System.Globalization.CultureInfo.InvariantCulture)
         .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
         .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');

    /// <summary>عنوان فارسیِ صندوق — برای پیام‌های خطا</summary>
    private static string BoxTitle(string box) => box switch
    {
        "Sent" => "ارسالی",
        "Junk" => "هرزنامه",
        _ => "دریافتی"
    };

    /// <summary>
    /// یافتنِ پوشه‌ی درخواستی:
    /// • دریافتی همان Inbox است
    /// • ارسالی با نشانِ استاندارد \Sent یا نام‌های متداول پیدا می‌شود
    /// • هرزنامه با نشانِ استاندارد \Junk یا نام‌های متداول (Spam / Junk / Bulk…)
    /// </summary>
    private static IMailFolder? OpenFolder(ImapClient client, string box)
    {
        if (box is not ("Sent" or "Junk")) return client.Inbox;

        var wanted = box == "Sent" ? FolderAttributes.Sent : FolderAttributes.Junk;

        // ۱) پوشه‌ای که نشانِ استاندارد را دارد (\Sent یا \Junk)
        try
        {
            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            foreach (var f in personal.GetSubfolders(false))
                if ((f.Attributes & wanted) != 0) return f;
        }
        catch { }

        // ۲) نشانِ استانداردِ پیش‌فرضِ کتابخانه (مثل All → Junk)
        try
        {
            var special = box == "Sent" ? client.GetFolder(SpecialFolder.Sent) : client.GetFolder(SpecialFolder.Junk);
            if (special != null) return special;
        }
        catch { }

        // ۳) نام‌های متداول در سرورهای مختلف (Gmail، Outlook، هاست‌های cPanel و…)
        var names = box == "Sent"
            ? new[] { "Sent", "Sent Items", "Sent Mail", "INBOX.Sent", "INBOX/Sent", "[Gmail]/Sent Mail" }
            : new[] { "Junk", "Junk E-mail", "Junk Email", "Spam", "INBOX.Spam", "INBOX.Junk", "INBOX/Junk",
                      "Bulk Mail", "[Gmail]/Spam" };
        foreach (var name in names)
        {
            try { return client.GetFolder(name); }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// ترمیمِ بدنهٔ ذخیره‌شده: تبدیل ارجاع‌های <c>cid:</c> به آدرسِ پیوستِ روی دیسک.
    /// برای ایمیل‌هایی است که پیش از پشتیبانی از تصویرِ درون‌متن دریافت شده‌اند
    /// (پیوست‌ها ذخیره شده‌اند ولی بدنه هنوز cid: دارد و تصویر دیده نمی‌شود).
    /// نگاشتِ Content-ID به پیوست از روی «نام فایل» انجام می‌شود، چون جدول پیوست
    /// ستونِ ContentId ندارد و اسکیما عوض نمی‌شود.
    /// </summary>
    private async Task RepairInlineBodyAsync(MimeMessage msg, string box, int messageId, string key)
    {
        if (messageId <= 0) return;

        string? body = box == "Sent"
            ? await _db.OtoSentEmails.Where(x => x.SentId == messageId).Select(x => x.Body).FirstOrDefaultAsync()
            : await _db.OtoInboxEmails.Where(x => x.InboxId == messageId).Select(x => x.Body).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(body)) return;
        if (body.IndexOf("cid:", StringComparison.OrdinalIgnoreCase) < 0) return;

        var atts = await _db.OtoEmailAttachments.AsNoTracking()
            .Where(a => a.Type == box && a.EmailId == messageId && a.UId == key)
            .Select(a => new { a.AttachmentId, a.AttachmentRealName })
            .ToListAsync();
        if (atts.Count == 0) return;

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in msg.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrWhiteSpace(p.ContentId)) continue;
            var name = string.IsNullOrWhiteSpace(p.FileName) ? "attachment" : Path.GetFileName(p.FileName!);
            var hit = atts.FirstOrDefault(a => string.Equals(a.AttachmentRealName, name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) map[p.ContentId.Trim('<', '>')] = hit.AttachmentId;
        }
        if (map.Count == 0) return;

        var fixedBody = ReplaceInlineImageReferences(body, map);
        if (string.Equals(fixedBody, body, StringComparison.Ordinal)) return;

        if (box == "Sent")
        {
            var row = await _db.OtoSentEmails.FirstOrDefaultAsync(x => x.SentId == messageId);
            if (row != null) { row.Body = fixedBody; await _db.SaveChangesAsync(); }
        }
        else
        {
            var row = await _db.OtoInboxEmails.FirstOrDefaultAsync(x => x.InboxId == messageId);
            if (row != null) { row.Body = fixedBody; await _db.SaveChangesAsync(); }
        }
        _log.LogInformation("ارجاع تصاویر درون‌متن ایمیل ترمیم شد؛ صندوق {Box}، شناسه {MessageId}، تعداد {Count}",
            box, messageId, map.Count);
    }

    private static string ReplaceInlineImageReferences(string body, Dictionary<string, int> inline)
    {
        foreach (var pair in inline)
        {
            body = body.Replace($"cid:{pair.Key}", $"/api/email/attachments/{pair.Value}/download", StringComparison.OrdinalIgnoreCase);
            body = body.Replace($"&lt;{pair.Key}&gt;", $"/api/email/attachments/{pair.Value}/download", StringComparison.OrdinalIgnoreCase);
        }
        return body;
    }

    /// <summary>
    /// تصویرِ درونِ متن (inline) — مثل لوگو/امضای ایمیل که با <c>cid:</c> در بدنه ارجاع داده می‌شود.
    /// این‌ها «پیوستِ واقعی» نیستند: باید داخل بدنه دیده شوند و در فهرست پیوست‌ها شمرده نشوند.
    /// </summary>
    private static bool IsInlineBodyPart(MimeEntity entity) =>
        entity is MimePart p
        && !p.IsAttachment                                   // disposition=attachment ⇒ پیوست واقعی
        && (!string.IsNullOrWhiteSpace(p.ContentId) || string.Equals(p.ContentDisposition?.Disposition, "inline", StringComparison.OrdinalIgnoreCase))
        && p.ContentType.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>هر بخشی که باید روی دیسک ذخیره شود (پیوست واقعی + تصویر درون‌متن)</summary>
    private static bool ShouldExtractPart(MimeEntity entity) =>
        entity is MessagePart ||
        entity is MimePart p && (p.IsAttachment || !string.IsNullOrWhiteSpace(p.FileName) || !string.IsNullOrWhiteSpace(p.ContentId));

    /// <summary>پیوستِ واقعی (قابلِ نمایش در فهرست پیوست‌ها) — تصاویر درون‌متن حساب نمی‌شوند</summary>
    private static bool HasRealAttachment(MimeMessage msg) =>
        msg.BodyParts.Any(x => ShouldExtractPart(x) && !IsInlineBodyPart(x));

    private static bool HasExtractableAttachment(MimeMessage msg) => msg.BodyParts.Any(ShouldExtractPart);

    private async Task<Dictionary<string, int>> SaveAttachmentsAsync(MimeMessage msg, string box, int messageId, string uid)
    {
        var inline = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.Combine(AttachmentRoot, $"{box}_{messageId}");
        var pendingInline = new List<(string ContentId, OtoEmailAttachment Attachment)>();
        var entities = msg.BodyParts.Where(ShouldExtractPart);

        foreach (var entity in entities)
        {
            try
            {
                string name;
                byte[] data;
                string? contentId = null;
                if (entity is MimePart part)
                {
                    if (part.Content is null) continue;
                    name = string.IsNullOrWhiteSpace(part.FileName) ? "attachment" : part.FileName;
                    using var ms = new MemoryStream();
                    await part.Content.DecodeToAsync(ms);
                    data = ms.ToArray();
                    contentId = part.ContentId;
                }
                else if (entity is MessagePart messagePart)
                {
                    // MessagePart (message/rfc822) از MimeEntity ارث‌بری می‌کند و FileName ندارد؛
                    // نام فایل فقط در هدرِ Content-Disposition قرار می‌گیرد.
                    var embeddedMessage = messagePart.Message;
                    if (embeddedMessage is null) continue;

                    var embeddedName = messagePart.ContentDisposition?.FileName;
                    if (string.IsNullOrWhiteSpace(embeddedName))
                        embeddedName = messagePart.ContentType.Name;
                    name = string.IsNullOrWhiteSpace(embeddedName) ? "attached-message.eml" : embeddedName;

                    using var ms = new MemoryStream();
                    await embeddedMessage.WriteToAsync(ms);
                    data = ms.ToArray();
                }
                else continue;

                if (data.Length == 0) continue;
                name = Path.GetFileName(name);

                // ⚠️ قبلاً این‌جا پیوست‌هایی که «نام یکسان» داشتند حذف می‌شدند
                // (x.AttachmentRealName == name ⇒ continue). نتیجه: اگر ایمیل دو فایل
                // هم‌نام داشت (مثلاً دو «image.png» یا دو «scan.jpg»)، فقط اولی ذخیره
                // می‌شد و دومی در «تبدیل به نامه» هم غایب بود. رفع: تکرارِ نام مجاز است
                // (روی دیسک با GUID ذخیره می‌شود) و کنترل تکرارِ واقعی سمت فراخوان انجام
                // می‌شود (مسیر ترمیم، فقط وقتی هیچ پیوستی برای پیام ثبت نشده باشد).

                Directory.CreateDirectory(dir);
                var saved = $"{Guid.NewGuid():N}_{SafeName(name)}";
                await File.WriteAllBytesAsync(Path.Combine(dir, saved), data);
                var attachment = new OtoEmailAttachment
                {
                    EmailId = messageId,
                    UId = uid,
                    Type = box,
                    AttachmentRealName = name,
                    AttachmentSavedName = saved,
                    FilePath = $"{AttachmentFolder}/{box}_{messageId}/{saved}"
                };
                _db.OtoEmailAttachments.Add(attachment);
                if (!string.IsNullOrWhiteSpace(contentId))
                    pendingInline.Add((contentId.Trim('<', '>'), attachment));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "ذخیره پیوست ایمیل ناموفق بود؛ صندوق {Box}، شناسه {MessageId}", box, messageId);
            }
        }
        await _db.SaveChangesAsync();
        foreach (var item in pendingInline)
            if (item.Attachment.AttachmentId > 0)
                inline[item.ContentId] = item.Attachment.AttachmentId;
        return inline;
    }

    /// <summary>نمایشِ گیرنده‌ها برای صندوقِ ارسالی — نام اگر داشت، وگرنه نشانی</summary>
    private static string RecipientsOf(MimeMessage msg)
    {
        try
        {
            var s = string.Join(", ", msg.To.Mailboxes
                .Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : m.Name));
            if (string.IsNullOrWhiteSpace(s)) s = msg.To.ToString();
            return Trunc(s, 1000);
        }
        catch { return ""; }
    }

    private static string Trunc(string v, int max)
        => string.IsNullOrEmpty(v) || v.Length <= max ? v : v[..max];

    // ==================== لیست‌ها / جزئیات ====================

    /// <summary>بیشینه‌ی تعداد سطرهای یک صفحه — جلوگیری از درخواست‌های سنگین</summary>
    private const int MaxPageSize = 100;

    /// <summary>نرمال‌سازیِ شماره‌ی صفحه و اندازه‌ی صفحه</summary>
    private static (int Page, int Size) Norm(int page, int pageSize)
        => (Math.Max(1, page), pageSize <= 0 ? 25 : Math.Min(pageSize, MaxPageSize));

    public async Task<EmailListPageDto> GetInboxAsync(int userId, int? emailId, string? search, bool? unreadOnly,
        bool isDabirkhaneAdmin, bool personalOnly, int page = 1, int pageSize = 25,
        int? folderId = null, bool spamOnly = false, bool starredOnly = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null)
    {
        var (pg, size) = Norm(page, pageSize);
        var result = new EmailListPageDto { Page = pg, PageSize = size };

        var myIds = await MyAccountIdsAsync(userId, isDabirkhaneAdmin, personalOnly);
        if (myIds.Count == 0) return result;
        var ids = emailId is > 0 && myIds.Contains(emailId.Value) ? new List<int> { emailId.Value } : myIds;

        var q = _db.OtoInboxEmails.AsNoTracking()
            .Include(i => i.Email)
            .Where(i => ids.Contains(i.EmailId));

        // هرزنامه‌ها پیش‌فرض در صندوقِ دریافتی دیده نمی‌شوند و فقط در بخش «اسپم» می‌آیند
        q = spamOnly ? q.Where(i => i.IsSpam) : q.Where(i => !i.IsSpam);

        // فیلترِ خوانده/خوانده‌نشده — isRead = true یعنی «فقط خوانده‌شده».
        // قبلاً فقط حالتِ «خوانده‌نشده» از کلاینت به سرور می‌رسید و انتخابِ
        // «خوانده‌شده» هیچ اثری نداشت (فهرست مثل حالتِ «همه» می‌ماند).
        if (isRead.HasValue) q = q.Where(i => i.IsRead == isRead.Value);
        else if (unreadOnly == true) q = q.Where(i => !i.IsRead);
        if (starredOnly) q = q.Where(i => i.IsNeshan);
        // بازهٔ تاریخ «کاملِ روز» است: تا تاریخ ۱۴۰۵/۰۶/۳۱ باید همهٔ ایمیل‌های همان
        // روز را هم بیاورد، نه فقط تا ساعت ۰۰:۰۰ آن روز (مقایسهٔ <= تاریخِ بدونِ
        // ساعت، تمامِ آن روز را حذف می‌کرد).
        if (dateFrom.HasValue) q = q.Where(i => i.Date >= dateFrom.Value.Date);
        if (dateTo.HasValue) { var to = dateTo.Value.Date.AddDays(1); q = q.Where(i => i.Date < to); }
        if (hasAttachment.HasValue) q = q.Where(i => i.IsAttachment == hasAttachment.Value);

        // folderId = 0 → بایگانی‌نشده | -1 → بایگانی‌شده در هر پوشه‌ای | n → پوشه n | خالی → همه
        if (folderId is > 0) q = q.Where(i => i.IsInFolder == folderId.Value);
        else if (folderId == 0) q = q.Where(i => i.IsInFolder == 0);
        else if (folderId == -1) q = q.Where(i => i.IsInFolder > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i => i.Subject.Contains(s) || i.FromAddress.Contains(s) || i.Body.Contains(s));
        }

        result.TotalCount = await q.CountAsync();
        result.UnreadCount = await q.CountAsync(i => !i.IsRead);

        // شمارشِ هرزنامه‌ها همیشه روی «همه‌ی پیام‌های غیراسپم/اسپمِ همین حساب‌ها» حساب می‌شود
        result.SpamCount = await _db.OtoInboxEmails.AsNoTracking()
            .CountAsync(i => ids.Contains(i.EmailId) && i.IsSpam);

        var rows = await q.OrderByDescending(i => i.Date)
            .Skip((pg - 1) * size).Take(size)
            .Select(i => new EmailMessageListItemDto
            {
                Id = i.InboxId,
                Box = "Inbox",
                EmailId = i.EmailId,
                AccountAddress = i.Email != null ? i.Email.EmailAddress : "",
                FromOrTo = i.FromAddress,
                Subject = i.Subject,
                Date = i.Date,
                IsRead = i.IsRead,
                IsNeshan = i.IsNeshan,
                IsAttachment = i.IsAttachment,
                IsInFolder = i.IsInFolder,
                IsSpam = i.IsSpam
            }).ToListAsync();

        await FillFolderTitlesAsync(rows);
        result.Items = rows;
        return result;
    }

    public async Task<EmailListPageDto> GetSentAsync(int userId, int? emailId, string? search,
        bool isDabirkhaneAdmin, bool personalOnly, int page = 1, int pageSize = 25,
        int? folderId = null, bool starredOnly = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null)
    {
        var (pg, size) = Norm(page, pageSize);
        var result = new EmailListPageDto { Page = pg, PageSize = size };

        var myIds = await MyAccountIdsAsync(userId, isDabirkhaneAdmin, personalOnly);
        if (myIds.Count == 0) return result;
        var ids = emailId is > 0 && myIds.Contains(emailId.Value) ? new List<int> { emailId.Value } : myIds;

        var q = _db.OtoSentEmails.AsNoTracking()
            .Include(s => s.Email)
            .Where(s => ids.Contains(s.EmailId));

        if (starredOnly) q = q.Where(x => x.IsNeshan);
        if (dateFrom.HasValue) q = q.Where(x => x.Date >= dateFrom.Value.Date);
        if (dateTo.HasValue) { var to = dateTo.Value.Date.AddDays(1); q = q.Where(x => x.Date < to); }
        if (hasAttachment.HasValue) q = q.Where(x => x.IsAttachment == hasAttachment.Value);

        if (folderId is > 0) q = q.Where(x => x.IsInFolder == folderId.Value);
        else if (folderId == 0) q = q.Where(x => x.IsInFolder == 0);
        else if (folderId == -1) q = q.Where(x => x.IsInFolder > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Subject.Contains(s) || x.ToDisplay.Contains(s) || x.Body.Contains(s));
        }

        result.TotalCount = await q.CountAsync();
        result.SpamCount = await _db.OtoInboxEmails.AsNoTracking()
            .CountAsync(i => ids.Contains(i.EmailId) && i.IsSpam);

        var rows = await q.OrderByDescending(x => x.Date)
            .Skip((pg - 1) * size).Take(size)
            .Select(x => new EmailMessageListItemDto
            {
                Id = x.SentId,
                Box = "Sent",
                EmailId = x.EmailId,
                AccountAddress = x.Email != null ? x.Email.EmailAddress : "",
                FromOrTo = x.ToDisplay,
                Subject = x.Subject,
                Date = x.Date,
                IsRead = true,
                IsNeshan = x.IsNeshan,
                IsAttachment = x.IsAttachment,
                IsInFolder = x.IsInFolder,
                IsSpam = false
            }).ToListAsync();

        await FillFolderTitlesAsync(rows);
        result.Items = rows;
        return result;
    }

    /// <summary>
    /// بایگانی: مجموعِ ایمیل‌های دریافتی و ارسالی که در یک پوشه قرار دارند.
    /// چون دو جدولِ متفاوت در میان است، ادغام به‌صورتِ پنجره‌ای انجام می‌شود:
    /// از هر جدول به اندازه‌ی «تا انتهای صفحه‌ی جاری» سطر خوانده و بعد از
    /// ادغام و مرتب‌سازی در حافظه، تنها سطرهای همین صفحه برگردانده می‌شود.
    /// حجمِ خوانده‌شده همواره در حدِ page × pageSize می‌ماند.
    /// </summary>
    public async Task<EmailListPageDto> GetArchiveAsync(int userId, int? emailId, string? search,
        bool isDabirkhaneAdmin, bool personalOnly, int page = 1, int pageSize = 25,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null,
        bool? unreadOnly = null, bool? isRead = null)
    {
        var (pg, size) = Norm(page, pageSize);
        var result = new EmailListPageDto { Page = pg, PageSize = size };

        var myIds = await MyAccountIdsAsync(userId, isDabirkhaneAdmin, personalOnly);
        if (myIds.Count == 0) return result;
        var ids = emailId is > 0 && myIds.Contains(emailId.Value) ? new List<int> { emailId.Value } : myIds;

        var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var take = pg * size; // پنجره‌ی مورد نیاز برای رسیدن به صفحه‌ی جاری

        var inQ = _db.OtoInboxEmails.AsNoTracking().Include(i => i.Email)
            .Where(i => ids.Contains(i.EmailId) && i.IsInFolder > 0 && !i.IsSpam);
        var outQ = _db.OtoSentEmails.AsNoTracking().Include(x => x.Email)
            .Where(x => ids.Contains(x.EmailId) && x.IsInFolder > 0);

        // همان فیلترهای نوارِ پیشرفته (تاریخ/خوانده‌شدن/پیوست) روی بایگانی هم اعمال می‌شوند؛
        // قبلاً این نوار در تب بایگانی نمایش داده می‌شد ولی هیچ اثری روی نتیجه نداشت.
        if (dateFrom.HasValue)
        {
            var from = dateFrom.Value.Date;
            inQ = inQ.Where(i => i.Date >= from);
            outQ = outQ.Where(x => x.Date >= from);
        }
        if (dateTo.HasValue)
        {
            var to = dateTo.Value.Date.AddDays(1);
            inQ = inQ.Where(i => i.Date < to);
            outQ = outQ.Where(x => x.Date < to);
        }
        if (hasAttachment.HasValue)
        {
            inQ = inQ.Where(i => i.IsAttachment == hasAttachment.Value);
            outQ = outQ.Where(x => x.IsAttachment == hasAttachment.Value);
        }
        // «خوانده‌نشده» فقط برای دریافتی معنا دارد؛ ارسالی همیشه خوانده‌شده است
        var filterRead = isRead ?? (unreadOnly.HasValue ? !unreadOnly.Value : (bool?)null);
        if (filterRead.HasValue) inQ = inQ.Where(i => i.IsRead == filterRead.Value);
        if (s != null)
        {
            inQ = inQ.Where(i => i.Subject.Contains(s) || i.FromAddress.Contains(s) || i.Body.Contains(s));
            outQ = outQ.Where(x => x.Subject.Contains(s) || x.ToDisplay.Contains(s) || x.Body.Contains(s));
        }

        result.TotalCount = await inQ.CountAsync() + await outQ.CountAsync();
        result.SpamCount = await _db.OtoInboxEmails.AsNoTracking()
            .CountAsync(i => ids.Contains(i.EmailId) && i.IsSpam);

        var inboxRows = await inQ.OrderByDescending(i => i.Date).Take(take)
            .Select(i => new EmailMessageListItemDto
            {
                Id = i.InboxId, Box = "Inbox", EmailId = i.EmailId,
                AccountAddress = i.Email != null ? i.Email.EmailAddress : "",
                FromOrTo = i.FromAddress, Subject = i.Subject, Date = i.Date,
                IsRead = i.IsRead, IsNeshan = i.IsNeshan, IsAttachment = i.IsAttachment,
                IsInFolder = i.IsInFolder, IsSpam = i.IsSpam
            }).ToListAsync();

        var sentRows = await outQ.OrderByDescending(x => x.Date).Take(take)
            .Select(x => new EmailMessageListItemDto
            {
                Id = x.SentId, Box = "Sent", EmailId = x.EmailId,
                AccountAddress = x.Email != null ? x.Email.EmailAddress : "",
                FromOrTo = x.ToDisplay, Subject = x.Subject, Date = x.Date,
                IsRead = true, IsNeshan = x.IsNeshan, IsAttachment = x.IsAttachment,
                IsInFolder = x.IsInFolder, IsSpam = false
            }).ToListAsync();

        var rows = inboxRows.Concat(sentRows)
            .OrderByDescending(x => x.Date)
            .Skip((pg - 1) * size).Take(size)
            .ToList();

        await FillFolderTitlesAsync(rows);
        result.Items = rows;
        return result;
    }

    /// <summary>
    /// حساب‌های قابل مشاهده کاربر: حساب‌های شخصی + (برای دبیرخانه) حساب‌های رسمی دبیرخانه
    /// personalOnly=true → فقط حساب‌هایی که خودِ کاربر ساخته و رسمیِ دبیرخانه نیستند
    /// (صفحه‌ی «ایمیل شخصی»)
    /// </summary>
    private async Task<List<int>> MyAccountIdsAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly = false)
    {
        if (personalOnly)
        {
            return await _db.OtoEmails.AsNoTracking()
                .Where(e => e.UserId == userId && !e.IsDabirkhane)
                .Select(e => e.EmailId).ToListAsync();
        }

        var ids = await _db.OtoEmails.AsNoTracking()
            .Where(e => e.UserId == userId).Select(e => e.EmailId).ToListAsync();
        if (isDabirkhaneAdmin)
        {
            var dab = await _db.OtoEmails.AsNoTracking()
                .Where(e => e.IsDabirkhane).Select(e => e.EmailId).ToListAsync();
            ids.AddRange(dab);
        }
        return ids.Distinct().ToList();
    }

    private async Task FillFolderTitlesAsync(List<EmailMessageListItemDto> rows)
    {
        var folderIds = rows.Where(r => r.IsInFolder > 0).Select(r => r.IsInFolder).Distinct().ToList();
        if (folderIds.Count == 0) return;
        var titles = await _db.OtoEmailFolders.AsNoTracking()
            .Where(f => folderIds.Contains(f.EmailFolderId) && f.IsFolder)
            .ToDictionaryAsync(f => f.EmailFolderId, f => f.Title);
        foreach (var r in rows)
            if (r.IsInFolder > 0 && titles.TryGetValue(r.IsInFolder, out var t)) r.FolderTitle = t;
    }

    public async Task<EmailMessageDetailDto> GetMessageAsync(string box, int id, int userId, bool markRead, bool isDabirkhaneAdmin)
    {
        if (string.Equals(box, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            var s = await _db.OtoSentEmails.AsNoTracking().Include(x => x.Email)
                .FirstOrDefaultAsync(x => x.SentId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception($"ایمیل پیدا نشد (ارسالی، شناسه {id}). اگر تازه ارسال شده، «دریافت» را بزنید.");

            var attsSent = await AttachmentDtosAsync("Sent", s.SentId);
            var bodySent = FixLegacyInlineRefs(s.Body, attsSent);
            MarkInlineAttachments(attsSent, bodySent);

            return new EmailMessageDetailDto
            {
                Id = s.SentId, Box = "Sent", EmailId = s.EmailId,
                AccountAddress = s.Email?.EmailAddress ?? "",
                FromOrTo = s.ToDisplay, Subject = s.Subject, Date = s.Date,
                IsRead = true, IsNeshan = s.IsNeshan, Body = bodySent, IsInFolder = s.IsInFolder, IsSpam = false,
                Attachments = attsSent
            };
        }

        var i = await _db.OtoInboxEmails.Include(x => x.Email)
            .FirstOrDefaultAsync(x => x.InboxId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
            ?? throw new Exception($"ایمیل پیدا نشد (دریافتی، شناسه {id}). اگر تازه رسیده، «دریافت» را بزنید.");
        if (markRead && !i.IsRead) { i.IsRead = true; await _db.SaveChangesAsync(); }

        var attsInbox = await AttachmentDtosAsync("Inbox", i.InboxId);
        var bodyInbox = FixLegacyInlineRefs(i.Body, attsInbox);
        MarkInlineAttachments(attsInbox, bodyInbox);
        return new EmailMessageDetailDto
        {
            Id = i.InboxId, Box = "Inbox", EmailId = i.EmailId,
            AccountAddress = i.Email?.EmailAddress ?? "",
            FromOrTo = i.FromAddress, Subject = i.Subject, Date = i.Date,
            IsRead = true, IsNeshan = i.IsNeshan, Body = bodyInbox, IsInFolder = i.IsInFolder, IsSpam = i.IsSpam,
            Attachments = attsInbox
        };
    }

    /// <summary>نشان‌کردنِ یک ایمیل به‌عنوان هرزنامه / یا خروج از هرزنامه (فقط صندوقِ دریافتی)</summary>
    public async Task<bool> MarkSpamAsync(string box, int id, int userId, bool isDabirkhaneAdmin, bool spam)
    {
        if (!string.Equals(box, "Inbox", StringComparison.OrdinalIgnoreCase))
            throw new Exception("تنها ایمیل‌های دریافتی را می‌توان هرزنامه نشان کرد.");

        var i = await _db.OtoInboxEmails
            .FirstOrDefaultAsync(x => x.InboxId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
            ?? throw new Exception("ایمیل پیدا نشد یا به شما تعلق ندارد.");

        i.IsSpam = spam;
        await _db.SaveChangesAsync();
        return spam;
    }

    /// <summary>
    /// علامت‌زدنِ «تصویر درونِ متن»: هر پیوستی که آدرسش داخل بدنه آمده، بخشی از
    /// بدنه است و نباید به‌عنوان فایل پیوست نمایش/منتقل شود.
    /// </summary>
    private static void MarkInlineAttachments(List<EmailAttachmentDto> atts, string? body)
    {
        foreach (var a in atts)
            a.IsInline = body != null &&
                body.Contains($"/api/email/attachments/{a.AttachmentId}/download", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ایمیل‌هایی که پیش از پشتیبانیِ تصویر درون‌متن دریافت شده‌اند، بدنه‌شان هنوز
    /// <c>cid:</c> دارد. چون Content-ID در جدول پیوست ذخیره نمی‌شود، نگاشت از روی
    /// «نام فایل» انجام می‌شود (بیشتر سرورها Content-ID را با نام فایل می‌سازند:
    /// <c>cid:image001.png@01DA…</c>). این اصلاح فقط در خروجی انجام می‌شود و
    /// داده‌ای را بازنویسی نمی‌کند؛ ترمیمِ دائمی هنگام Sync انجام می‌گیرد.
    /// </summary>
    private static string FixLegacyInlineRefs(string? body, List<EmailAttachmentDto> atts)
    {
        if (string.IsNullOrWhiteSpace(body)) return body ?? "";
        if (body.IndexOf("cid:", StringComparison.OrdinalIgnoreCase) < 0) return body!;
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in atts)
        {
            if (string.IsNullOrWhiteSpace(a.RealName)) continue;
            var name = a.RealName;
            if (body.Contains("cid:" + name, StringComparison.OrdinalIgnoreCase)
                || body.Contains("cid:" + Uri.EscapeDataString(name), StringComparison.OrdinalIgnoreCase))
                map[name] = a.AttachmentId;
        }
        return map.Count == 0 ? body! : ReplaceInlineImageReferences(body!, map);
    }

    /// <summary>پیوست‌ها بدون علامتِ inline — علامت بعد از آماده‌سازی بدنه زده می‌شود</summary>
    private async Task<List<EmailAttachmentDto>> AttachmentDtosAsync(string box, int messageId)
    {
        var list = await _db.OtoEmailAttachments.AsNoTracking()
            .Where(a => a.Type == box && a.EmailId == messageId)
            .ToListAsync();
        return list.Select(a => new EmailAttachmentDto
        {
            AttachmentId = a.AttachmentId,
            RealName = a.AttachmentRealName,
            Size = DiskFileSize(a.FilePath),
            // جدول پیوست ستون ContentType ندارد؛ نوع از پسوند فایل استنباط می‌شود
            // (همان نگاشتی که هنگام ارسال ایمیل به کار می‌رود) تا کلاینت در تبدیل
            // «ایمیل → نامه وارده» نوع درست را همراه فایل بارگذاری کند.
            ContentType = MimeToExtensionType(a.AttachmentRealName, "")
        }).ToList();
    }

    private long DiskFileSize(string? relativePath)
    {
        var full = ResolveDiskPath(relativePath);
        if (full is null || !File.Exists(full)) return 0;
        try { return new FileInfo(full).Length; } catch { return 0; }
    }

    /// <summary>
    /// علامت‌زدن ایمیل به‌عنوان «خوانده‌شده».
    /// نکته: پیش‌تر این متد فقط برای صاحبِ حساب کار می‌کرد و روی حساب‌های
    /// رسمیِ دبیرخانه (که بین چند دستیار مشترک است) خطا می‌داد؛ حالا دستیارِ
    /// دبیرخانه هم اجازهٔ علامت‌زدن دارد تا ایمیلِ «مشاهده‌شده» بعد از «دریافت»
    /// دوباره به‌صورت خوانده‌نشده (بولد) برنگردد. اگر پیام در دسترس نبود،
    /// خطا نمی‌دهیم چون نتیجهٔ موردنظر (خوانده‌شدن) از قبل برقرار است.
    /// </summary>
    public async Task MarkReadAsync(string box, int id, int userId, bool isDabirkhaneAdmin)
    {
        if (string.Equals(box, "Sent", StringComparison.OrdinalIgnoreCase)) return;
        var i = await _db.OtoInboxEmails.Include(x => x.Email)
            .FirstOrDefaultAsync(x => x.InboxId == id &&
                (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)));
        if (i is null) return;
        if (i.IsRead) return;
        i.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ToggleNeshanAsync(string box, int id, int userId, bool isDabirkhaneAdmin)
    {
        if (string.Equals(box, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            var s = await _db.OtoSentEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.SentId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            s.IsNeshan = !s.IsNeshan;
            await _db.SaveChangesAsync();
            return s.IsNeshan;
        }
        var i = await _db.OtoInboxEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.InboxId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
            ?? throw new Exception("ایمیل پیدا نشد.");
        i.IsNeshan = !i.IsNeshan;
        await _db.SaveChangesAsync();
        return i.IsNeshan;
    }

    public async Task ArchiveAsync(EmailArchiveDto dto, int userId, bool isDabirkhaneAdmin)
    {
        if (dto.FolderId > 0)
        {
            _ = await _db.OtoEmailFolders.FirstOrDefaultAsync(f => f.EmailFolderId == dto.FolderId && f.UserId == userId && f.IsFolder)
                ?? throw new Exception("پوشه بایگانی پیدا نشد.");
        }
        if (string.Equals(dto.Box, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            var s = await _db.OtoSentEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.SentId == dto.Id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            s.IsInFolder = dto.FolderId;
        }
        else
        {
            var i = await _db.OtoInboxEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.InboxId == dto.Id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            i.IsInFolder = dto.FolderId;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<byte[]?> ReadAttachmentAsync(int attachmentId, int userId, bool isDabirkhaneAdmin)
    {
        var a = await _db.OtoEmailAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.AttachmentId == attachmentId)
            ?? throw new Exception("پیوست پیدا نشد.");

        bool allowed;
        if (string.Equals(a.Type, "Sent", StringComparison.OrdinalIgnoreCase))
            allowed = await _db.OtoSentEmails.AnyAsync(s => s.SentId == a.EmailId && (s.Email!.UserId == userId || (isDabirkhaneAdmin && s.Email!.IsDabirkhane)));
        else
            allowed = await _db.OtoInboxEmails.AnyAsync(i => i.InboxId == a.EmailId && (i.Email!.UserId == userId || (isDabirkhaneAdmin && i.Email!.IsDabirkhane)));
        if (!allowed) throw new Exception("این پیوست متعلق به حساب شما نیست.");

        var full = ResolveDiskPath(a.FilePath);
        // سازگاری با رکوردهای قدیمی که FilePath نداشتند اما نام فایل ذخیره‌شده داشتند.
        if ((full is null || !File.Exists(full)) && !string.IsNullOrWhiteSpace(a.AttachmentSavedName))
        {
            var candidates = new[] { a.Type, "Inbox", "Sent", "Junk", "Received" }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var type in candidates)
            {
                var legacyRelative = $"{AttachmentFolder}/{type}_{a.EmailId}/{SafeName(a.AttachmentSavedName)}";
                full = ResolveDiskPath(legacyRelative);
                if (full is not null && File.Exists(full)) break;
            }
        }
        // در نسخه‌های قدیمی ممکن است FilePath یا نام پوشه با حروف متفاوت ذخیره شده باشد؛
        // جستجوی بازگشتی — این‌بار در «همهٔ» ریشه‌های محتمل wwwroot و هر دو پوشهٔ
        // قدیمی («فایل های ایمیل») و جدید، چون در استقرار ویندوز ریشه ممکن است جابه‌جا شده باشد.
        if ((full is null || !File.Exists(full)) && !string.IsNullOrWhiteSpace(a.AttachmentSavedName))
        {
            var safe = SafeName(a.AttachmentSavedName);
            var searchRoots = WwwrootCandidates()
                .SelectMany(root => new[] { Path.Combine(root, AttachmentFolder), Path.Combine(root, "فایل های ایمیل") })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists)
                .ToList();
            foreach (var dir in searchRoots)
            {
                full = Directory.EnumerateFiles(dir, safe, SearchOption.AllDirectories).FirstOrDefault();
                if (full is not null) break;
            }
        }
        if (full is null || !File.Exists(full)) return null;
        return await File.ReadAllBytesAsync(full);
    }

    // ==================== پوشه‌های بایگانی ====================

    public async Task<List<EmailFolderDto>> GetFoldersAsync(int userId, Paging.Request? pagination = null) =>
        await _db.OtoEmailFolders.AsNoTracking()
            .Where(f => f.UserId == userId && f.IsFolder && !f.IsDelete)
            .OrderBy(f => f.EmailFolderId)
            .Select(f => new EmailFolderDto { EmailFolderId = f.EmailFolderId, Title = f.Title, ParentId = f.ParentId, TypeEmail = f.TypeEmail })
            .ToPageListAsync(pagination);

    public async Task<int> SaveFolderAsync(SaveEmailFolderDto dto, int userId)
    {
        OtoEmailFolder f;
        if (dto.EmailFolderId > 0)
        {
            f = await _db.OtoEmailFolders.FirstOrDefaultAsync(x => x.EmailFolderId == dto.EmailFolderId && x.UserId == userId && x.IsFolder)
                ?? throw new Exception("پوشه پیدا نشد.");
            f.Title = dto.Title.Trim();
        }
        else
        {
            f = new OtoEmailFolder { UserId = userId, IsFolder = true, EmailId = 0 };
            _db.OtoEmailFolders.Add(f);
            f.Title = dto.Title.Trim();
            f.ParentId = dto.ParentId;
            f.TypeEmail = dto.TypeEmail;
        }
        await _db.SaveChangesAsync();
        return f.EmailFolderId;
    }

    public async Task DeleteFolderAsync(int folderId, int userId)
    {
        var f = await _db.OtoEmailFolders.FirstOrDefaultAsync(x => x.EmailFolderId == folderId && x.UserId == userId && x.IsFolder)
            ?? throw new Exception("پوشه پیدا نشد.");
        f.IsDelete = true;
        // ایمیل‌های داخل پوشه فقط از بایگانی خارج می‌شوند
        await _db.OtoInboxEmails.Where(i => i.IsInFolder == folderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsInFolder, 0));
        await _db.OtoSentEmails.Where(s => s.IsInFolder == folderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsInFolder, 0));
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// بدنهٔ متنیِ (text/plain) ایمیل را به HTML امن تبدیل می‌کند.
    /// <para>
    /// بعضی کلاینت‌ها در بخش متنی هم تگ HTML می‌گذارند (مثل Outlook: «From: … Sent: …» و
    /// سپس تگ‌های پاراگراف). اگر آن متن خام encode شود، همان تگ‌ها بعداً در متن نامه و
    /// دستور کار به شکل &amp;lt;p&amp;gt; دیده می‌شوند؛ پس اول تگ‌ها حذف و موجودیت‌ها
    /// رمزگشایی می‌شوند، بعد متن در بلوک white-space:pre-wrap قرار می‌گیرد تا شکست خط بماند.
    /// </para>
    /// </summary>
    private static string BuildTextBodyHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var t = text!;

        if (System.Text.RegularExpressions.Regex.IsMatch(
                t, @"<\s*/?\s*(p|br|div|span|table|tr|td|html|body|font|pre)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            t = System.Text.RegularExpressions.Regex.Replace(t, @"<\s*br\s*/?\s*>", "\n",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(
                    t, @"</\s*(p|div|tr|li|table|blockquote|pre|h[1-6])\s*>", "\n",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"<\s*/?\s*[a-zA-Z][^>]*>", " ");
            t = System.Net.WebUtility.HtmlDecode(t);
            t = System.Net.WebUtility.HtmlDecode(t);
        }

        return "<div style=\"white-space:pre-wrap\">" + System.Net.WebUtility.HtmlEncode(t) + "</div>";
    }
}
