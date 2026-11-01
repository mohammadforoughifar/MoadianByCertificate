using System.Security.Claims;
using System.Security.Cryptography;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>اعلان‌های کاربر جاری — لیست، شمارش نخوانده و علامت‌گذاری با تفکیک و ایزولاسیون کامل کاربران.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessengerService _messenger;
    private readonly IMessengerLinkCodes _codes;
    private readonly IPushService _push;
    private readonly PushKeyStore _keys;
    private readonly PushSettings _pushSettings;
    public NotificationsController(AppDbContext db, IMessengerService messenger, IMessengerLinkCodes codes, IPushService push, PushKeyStore keys, PushSettings pushSettings)
    {
        _db = db;
        _messenger = messenger;
        _codes = codes;
        _push = push;
        _keys = keys;
        _pushSettings = pushSettings;
    }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) && v > 0 ? v : 0;

    /// <summary>
    /// اعلان‌های کاربر جاری (فهرست ساده — سازگار با فراخوان‌های قبلی).
    /// <paramref name="unreadOnly"/> فقط نخوانده‌ها را برمی‌گرداند و سقف صفحه از ۱۰۰ به ۲۰۰
    /// رسیده تا مشتری‌هایی که کل فهرست را می‌خواهند، بی‌صدا بریده نشوند.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> My([FromQuery] int take = 50, [FromQuery] int skip = 0, [FromQuery] bool unreadOnly = false)
    {
        if (MyUserId <= 0) return Unauthorized();

        var q = _db.AppNotifications.AsNoTracking().Where(n => n.UserId == MyUserId);
        if (unreadOnly) q = q.Where(n => !n.IsRead);

        var list = await q
            .OrderByDescending(n => n.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 200))
            .Select(n => new { n.Id, n.Title, n.Body, n.FromName, n.FormName, n.Link, n.IsRead, n.CreatedAt })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// صفحهٔ اعلان‌ها همراه با شمارش‌ها — تا شمارندهٔ زنگوله (که همهٔ نخوانده‌ها را می‌شمارد)
    /// با فهرست نمایش‌داده‌شده ناهم‌خوان نشود. پیش‌تر فهرست فقط ۵۰/۱۵ ردیف اول را می‌داد و
    /// کاربر با «+۹۹» فقط چند اعلان می‌دید.
    /// </summary>
    [HttpGet("page")]
    public async Task<IActionResult> Page([FromQuery] int take = 25, [FromQuery] int skip = 0, [FromQuery] bool unreadOnly = false)
    {
        if (MyUserId <= 0) return Unauthorized();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var q = _db.AppNotifications.AsNoTracking().Where(n => n.UserId == MyUserId);
        var total = await q.CountAsync();
        var unread = await q.CountAsync(n => !n.IsRead);
        if (unreadOnly) q = q.Where(n => !n.IsRead);

        var filteredTotal = unreadOnly ? unread : total;

        var items = await q
            .OrderByDescending(n => n.Id)
            .Skip(skip)
            .Take(take)
            .Select(n => new { n.Id, n.Title, n.Body, n.FromName, n.FormName, n.Link, n.IsRead, n.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            items,
            total,
            unread,
            filteredTotal,
            skip,
            take,
            hasMore = skip + items.Count < filteredTotal
        });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        if (MyUserId <= 0) return Unauthorized();

        var count = await _db.AppNotifications.AsNoTracking()
            .CountAsync(n => n.UserId == MyUserId && !n.IsRead);

        return Ok(new { count });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (MyUserId <= 0) return Unauthorized();

        var n = await _db.AppNotifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == MyUserId);
        if (n == null) return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== پیام‌رسان‌ها: بله و ایتا ==================

    /// <summary>وضعیت اتصال پیام‌رسان‌ها برای کاربر جاری (برای نمایش در پنل اعلان‌ها).</summary>
    [HttpGet("messenger-status")]
    public async Task<IActionResult> MessengerStatus()
    {
        if (MyUserId <= 0) return Unauthorized();

        var settings = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == MyUserId)
            .Select(u => new { u.BaleChatId, u.EitaaChatId, u.Mobile })
            .FirstOrDefaultAsync();
        if (user == null) return NotFound();

        return Ok(new
        {
            baleConfigured = !string.IsNullOrWhiteSpace(settings?.BaleBotToken),
            eitaaConfigured = !string.IsNullOrWhiteSpace(settings?.EitaaToken),
            baleLinked = !string.IsNullOrWhiteSpace(user.BaleChatId),
            eitaaLinked = !string.IsNullOrWhiteSpace(user.EitaaChatId),
            hasMobile = !string.IsNullOrWhiteSpace(user.Mobile),
            ackEitaaUserId = user.EitaaChatId,
            senderNumber = settings?.MessengerSenderNumber ?? "09111189771",
            isAdmin = await IsPushAdminAsync(),
            eitaaCodeActive = _codes.HasActive(MyUserId)
        });
    }

    /// <summary>همگام‌سازی بله — تطبیق مخاطبین ربات با موبایل کاربران (فقط مدیر سامانه).</summary>
    [HttpPost("sync-bale")]
    public async Task<IActionResult> SyncBale()
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsPushAdminAsync())
            return StatusCode(403, new { message = "تنها مدیر سامانه مجاز به همگام‌سازی بله است." });

        var (matched, message) = await _messenger.SyncBaleAsync();
        return Ok(new { matched, message });
    }

    /// <summary>ارسال پیام آزمایشی بله/ایتا به کاربر جاری — نتیجه‌ی هر پیام‌رسان برگردانده می‌شود.</summary>
    [HttpPost("test-messenger")]
    public async Task<IActionResult> TestMessenger()
    {
        if (MyUserId <= 0) return Unauthorized();

        var r = await _messenger.SendTestAsync(MyUserId);
        if (!r.Linked)
            return Ok(new { posted = false, message = "حساب شما هنوز به بله یا ایتا متصل نشده است — ابتدا از همین پنجره اتصال را انجام دهید." });

        var sentTo = new List<string>();
        if (r.BaleSent) sentTo.Add("بله");
        if (r.EitaaSent) sentTo.Add("ایتا");

        var problems = new List<string>();
        if (r.BaleError != null) problems.Add("بله: " + r.BaleError);
        if (r.EitaaError != null) problems.Add("ایتا: " + r.EitaaError);

        var message = sentTo.Count > 0
            ? $"پیام آزمایشی به {string.Join(" و ", sentTo)} ارسال شد."
            : "ارسال انجام نشد." + (problems.Count > 0 ? " " + string.Join(" | ", problems) : "");
        if (sentTo.Count > 0 && problems.Count > 0) message += " " + string.Join(" | ", problems);

        return Ok(new { posted = sentTo.Count > 0, bale = r.BaleError, eitaa = r.EitaaError, message });
    }

    /// <summary>ساخت کد اتصال ایتا برای کاربر جاری — کاربر این کد را داخل «برنامه» ایتا وارد می‌کند.</summary>
    [HttpPost("eitaa-link-code")]
    public async Task<IActionResult> CreateEitaaLinkCode()
    {
        if (MyUserId <= 0) return Unauthorized();
        return Ok(await BuildEitaaCodeAsync(MyUserId));
    }

    /// <summary>ساخت کد اتصال ایتا برای یک کاربر دیگر (فقط مدیر — برای راه‌اندازی توسط پشتیبانی).</summary>
    [HttpPost("eitaa-link-code/{userId:int}")]
    public async Task<IActionResult> CreateEitaaLinkCodeFor(int userId)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsPushAdminAsync())
            return StatusCode(403, new { message = "تنها مدیر سامانه مجاز به ساخت کد اتصال برای دیگران است." });
        if (!await _db.Users.AnyAsync(u => u.Id == userId)) return NotFound(new { message = "کاربر پیدا نشد." });

        return Ok(await BuildEitaaCodeAsync(userId));
    }

    private async Task<object> BuildEitaaCodeAsync(int userId)
    {
        var settings = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        var configured = !string.IsNullOrWhiteSpace(settings?.EitaaToken);
        var (code, expires) = _codes.Create(userId);
        return new
        {
            code,
            expiresAt = expires,
            configured,
            message = configured
                ? "این کد را در برنامه‌ی سامانه در ایتا وارد کنید."
                : "توکن برنامه ایتا در «تنظیمات ← پیام‌رسان‌ها» وارد نشده است؛ ابتدا آن را تنظیم کنید."
        };
    }

    /// <summary>
    /// پایان‌یافتن اتصال ایتا: صفحه‌ی برنامک، initData ایتا را همراه کد اتصال به این مسیر می‌فرستد.
    /// اعتبارسنجی امضا با HMAC-SHA256 و توکن برنامه انجام می‌شود (بدون نیاز به احراز هویت، چون داخل ایتا اجرا می‌شود).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("eitaa-link")]
    public async Task<IActionResult> LinkEitaa([FromBody] EitaaLinkInput? input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.InitData))
            return Ok(new { ok = false, message = "داده‌ای از ایتا دریافت نشد؛ لطفاً این صفحه را از داخل برنامه‌ی ایتا باز کنید." });

        var (ok, message) = await _messenger.LinkEitaaAsync(input.Code ?? "", input.InitData);
        return Ok(new { ok, message });
    }

    public class EitaaLinkInput
    {
        public string? Code { get; set; }
        public string? InitData { get; set; }
    }

    /// <summary>پاک کردن همه اعلان‌های کاربر جاری.</summary>
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        if (MyUserId <= 0) return Unauthorized();

        var list = await _db.AppNotifications.Where(n => n.UserId == MyUserId).ToListAsync();
        _db.AppNotifications.RemoveRange(list);
        await _db.SaveChangesAsync();
        return Ok(new { removed = list.Count });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        if (MyUserId <= 0) return Unauthorized();

        var list = await _db.AppNotifications.Where(n => n.UserId == MyUserId && !n.IsRead).ToListAsync();
        foreach (var n in list) n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== نوتیفیکیشن گوشی/تبلت (Web Push) ==================

    /// <summary>کلید عمومی VAPID — برای ثبت اشتراک push در مرورگر/دستگاه.</summary>
    [HttpGet("push-vapid-key")]
    public IActionResult VapidKey() => Ok(new { publicKey = _push.VapidPublicKey, configured = _push.IsConfigured, source = _push.Source });

    /// <summary>
    /// وضعیت کامل اعلان گوشی/مرورگر (فقط مدیر): فعال بودن کلیدها، منبع آن‌ها و تعداد دستگاه‌های ثبت‌شده.
    /// </summary>
    [HttpPost("push-overview")]
    public async Task<IActionResult> PushOverview()
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsPushAdminAsync()) return StatusCode(403, new { message = "تنها مدیر سامانه مجاز است." });
        var devices = await _db.PushSubscriptions.AsNoTracking().CountAsync();
        var users = await _db.PushSubscriptions.AsNoTracking().Select(s => s.UserId).Distinct().CountAsync();
        var pending = await _db.PushDeliveries.AsNoTracking().CountAsync(j => j.Status == "Pending" || j.Status == "Working");
        var failed = await _db.PushDeliveries.AsNoTracking().CountAsync(j => j.Status == "Failed");
        var last = await _db.PushDeliveries.AsNoTracking().OrderByDescending(j => j.Id)
            .Select(j => new { status = j.Status, errorCode = j.ErrorCode, createdAtUtc = j.CreatedAtUtc }).FirstOrDefaultAsync();
        return Ok(new { configured = _push.IsConfigured, source = _push.Source, publicKey = _push.VapidPublicKey, devices, users, pending, failed, last });
    }

    public sealed class VapidApplyInput
    {
        public string? PublicKey { get; set; }
        public string? PrivateKey { get; set; }
        public string? Subject { get; set; }
    }

    /// <summary>
    /// ساخت (یا ثبت) کلید اعلان و <b>فعال‌سازی زنده</b> از داخل خود نرم‌افزار (فقط مدیر):
    /// بدون ویرایش appsettings.json و بدون ری‌استارت سرویس. کلیدها در App_Data/push-vapid.json ذخیره می‌شوند.
    /// اگر بدنه شامل PublicKey/PrivateKey باشد، همان‌ها ذخیره می‌شوند؛ در غیر این صورت یک جفت‌کلید تازه ساخته می‌شود.
    /// اگر کلید عمومی تغییر کند، اشتراک دستگاه‌های قبلی باطل است و پاک می‌شود (کاربران باید اعلان را یک‌بار دوباره فعال کنند).
    /// </summary>
    [HttpPost("push-vapid-generate")]
    public async Task<IActionResult> GenerateVapid([FromBody] VapidApplyInput? input = null)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsPushAdminAsync())
            return StatusCode(403, new { message = "تنها مدیر سامانه مجاز به تولید کلید اعلان است." });

        var previousKey = _push.VapidPublicKey;
        string publicKey, privateKey, subject;
        var manual = !string.IsNullOrWhiteSpace(input?.PublicKey) || !string.IsNullOrWhiteSpace(input?.PrivateKey);
        if (manual)
        {
            publicKey = (input!.PublicKey ?? "").Trim();
            privateKey = (input.PrivateKey ?? "").Trim();
            subject = (input.Subject ?? "").Trim();
            if (string.IsNullOrWhiteSpace(subject)) subject = DefaultSubject();
            if (!PushSettings.Validate(publicKey, privateKey, subject))
                return BadRequest(new { message = "جفت‌کلید وارد‌شده معتبر نیست؛ کلید عمومی باید ۶۵ بایت (شروع 0x04) و کلید خصوصی ۳۲ بایت باشد." });
        }
        else
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = ecdsa.ExportParameters(true);
            var spki = ecdsa.ExportSubjectPublicKeyInfo(); // برای P-256، ۶۵ بایتِ انتهای DER نقطه‌ی فشرده‌نشده (04||X||Y) است
            publicKey = B64Url(spki[^65..]);
            privateKey = B64Url(p.D!);
            subject = string.IsNullOrWhiteSpace(input?.Subject) ? DefaultSubject() : input!.Subject!.Trim();
        }

        _keys.Save(new PushKeyFile { PublicKey = publicKey, PrivateKey = privateKey, Subject = subject });
        if (!_pushSettings.Apply(publicKey, privateKey, subject, out var error))
        {
            _keys.Delete();
            return StatusCode(500, new { message = error });
        }

        var cleared = 0;
        if (!string.IsNullOrEmpty(previousKey) && previousKey != publicKey) cleared = await _push.ClearSubscriptionsAsync();

        var message = cleared > 0
            ? $"کلید اعلان ساخته و فعال شد؛ {cleared} اشتراک دستگاه قبلی باطل و پاک شد — کاربران باید اعلان را یک‌بار دوباره فعال کنند."
            : "کلید اعلان ساخته و فعال شد؛ نیازی به ویرایش فایل یا ری‌استارت نیست. کاربران اکنون می‌توانند اعلان گوشی را فعال کنند.";
        return Ok(new { publicKey, privateKey, subject, applied = true, source = _push.Source, cleared, message });
    }

    /// <summary>حذف کلیدهای ساخته‌شده در نرم‌افزار و بازگشت به کلیدهای فایل/متغیر محیطی سرور (فقط مدیر).</summary>
    [HttpPost("push-vapid-reset")]
    public async Task<IActionResult> ResetVapid()
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsPushAdminAsync()) return StatusCode(403, new { message = "تنها مدیر سامانه مجاز است." });
        var previousKey = _push.VapidPublicKey;
        var removed = _keys.Delete();
        _pushSettings.ResetToServerConfig();
        var cleared = 0;
        if (_push.VapidPublicKey != previousKey) cleared = await _push.ClearSubscriptionsAsync();
        var message = _push.IsConfigured
            ? "کلیدهای ساخته‌شده در نرم‌افزار حذف شد و کلیدهای فایل سرور فعال شد."
            : "کلیدهای ساخته‌شده حذف شد؛ اکنون هیچ کلید فعالی نیست و اعلان گوشی خاموش است.";
        return Ok(new { applied = removed, configured = _push.IsConfigured, publicKey = _push.VapidPublicKey, source = _push.Source, cleared, message });
    }

    private string DefaultSubject()
    {
        var host = Request.Host.Host;
        return $"mailto:admin@{(string.IsNullOrWhiteSpace(host) ? "example.com" : host)}";
    }

    private static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>مدیر = نقش قدیمی Admin یا مجوز RBAC «Settings.Manage».</summary>
    private async Task<bool> IsPushAdminAsync()
    {
        if (User.IsInRole("Admin")) return true;
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId);
        if (!hasRoles) return false;
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, pm => pm.Id, (pid, pm) => pm)
            .AnyAsync(pm => pm.Module == "Settings" && pm.Action == "Manage");
    }

    /// <summary>ثبت اشتراک push این دستگاه برای کاربر جاری.</summary>
    [HttpPost("push-subscribe")]
    public async Task<IActionResult> PushSubscribe([FromBody] PushSubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (input == null || string.IsNullOrWhiteSpace(input.Endpoint))
            return BadRequest(new { message = "اندپوینت اشتراک ارسال نشده است." });

        if (!_push.IsConfigured) return StatusCode(503, new { message = "کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند." });
        if (!PushEndpointPolicy.ValidEndpoint(input.Endpoint) || !PushEndpointPolicy.ValidKeys(input.P256DH ?? "", input.Auth ?? ""))
            return BadRequest(new { message = "اشتراک دستگاه معتبر نیست؛ مرورگر را به‌روز کنید." });
        if (!await _db.Users.AnyAsync(u => u.Id == MyUserId && u.IsActive)) return Forbid();
        var ua = Request.Headers.UserAgent.ToString();
        await _push.SaveSubscriptionAsync(MyUserId, input.Endpoint, input.P256DH ?? "", input.Auth ?? "", ua);
        return Ok(new { ok = true, message = "اشتراک نوتیفیکیشن دستگاه ثبت شد." });
    }

    /// <summary>لغو اشتراک push یک دستگاه (کلیر از سمت کاربر).</summary>
    [HttpPost("push-unsubscribe")]
    public async Task<IActionResult> PushUnsubscribe([FromBody] PushUnsubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (input != null && !string.IsNullOrWhiteSpace(input.Endpoint))
            await _push.RemoveSubscriptionAsync(MyUserId, input.Endpoint);
        return Ok(new { ok = true });
    }

    /// <summary>ارسال پیام آزمایشی push به کاربر جاری (برای تست تنظیمات).</summary>
    [HttpPost("test-push")]
    public async Task<IActionResult> TestPush([FromBody] PushUnsubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!_push.IsConfigured) return StatusCode(503, new { message = "کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند." });
        var endpoint = input?.Endpoint ?? "";
        var sub = await _db.PushSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == MyUserId && s.Endpoint == endpoint);
        if (sub == null) return BadRequest(new { message = "ابتدا اعلان این دستگاه را فعال کنید." });
        var recent = DateTime.UtcNow.AddSeconds(-30);
        if (await _db.PushDeliveries.AnyAsync(j => j.UserId == MyUserId && j.Tag.StartsWith("test-") && j.CreatedAtUtc > recent))
            return StatusCode(429, new { message = "برای آزمایش بعدی ۳۰ ثانیه صبر کنید." });
        var now = DateTime.UtcNow;
        _db.PushDeliveries.Add(new PushDelivery { UserId = MyUserId, SubscriptionId = sub.Id, Tag = "test-" + Guid.NewGuid().ToString("N"),
            Title = "آزمایش اعلان گوشی", Body = "اعلان آزمایشی سامانه دریافت شد.", Link = "/", CreatedAtUtc = now, NextAttemptAtUtc = now, ExpiresAtUtc = now.AddMinutes(10) });
        await _db.SaveChangesAsync();
        return Accepted(new { message = "اعلان در صف ارسال قرار گرفت؛ دریافت آن را در نوار اعلان گوشی بررسی کنید." });
    }

    [HttpPost("push-status")]
    public async Task<IActionResult> PushStatus([FromBody] PushUnsubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        var sub = await _db.PushSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == MyUserId && s.Endpoint == (input == null ? "" : input.Endpoint));
        var last = sub == null ? null : await _db.PushDeliveries.AsNoTracking().Where(j => j.UserId == MyUserId && j.SubscriptionId == sub.Id).OrderByDescending(j => j.Id)
            .Select(j => new { status = j.Status, errorCode = j.ErrorCode, attempts = j.Attempts }).FirstOrDefaultAsync();
        return Ok(new { configured = _push.IsConfigured, registered = sub != null, last });
    }

    /// <summary>
    /// صفحه‌ی برنامک ایتا (Mini App) — «آدرس مقصد» در پنل توسعه‌دهندگان ایتا باید همین مسیر باشد:
    ///    https://آدرس-سرور/eitaa-app
    /// کاربر این صفحه را از داخل ایتا باز می‌کند، کد اتصال را وارد می‌کند و initData برای اعتبارسنجی ارسال می‌شود.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/eitaa-app")]
    public IActionResult EitaaApp() => Content(EitaaAppHtml, "text/html; charset=utf-8");

    private const string EitaaAppHtml = """
<!DOCTYPE html>
<html lang="fa" dir="rtl">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1">
<title>اتصال اعلان‌های سامانه به ایتا</title>
<script src="https://developer.eitaa.com/eitaa-web-app.js"></script>
<style>
  :root { --brand:#0d6efd; --ok:#15803d; --err:#b91c1c; }
  * { box-sizing:border-box; }
  body { margin:0; padding:16px; font-family:'Vazirmatn','Shabnam',Tahoma,sans-serif;
         background:#f7f8fa; color:#1f2937; line-height:1.9; }
  .card { max-width:520px; margin:0 auto; background:#fff; border-radius:16px; padding:20px;
          box-shadow:0 6px 24px rgba(15,23,42,.08); }
  h1 { font-size:1.15rem; margin:0 0 6px; }
  p { font-size:.86rem; color:#4b5563; margin:.35rem 0; }
  ol { font-size:.86rem; color:#4b5563; padding-right:1.1rem; margin:.4rem 0 .8rem; }
  label { display:block; font-size:.85rem; font-weight:700; margin:10px 0 6px; }
  input { width:100%; padding:12px; font-size:1.3rem; letter-spacing:.35em; text-align:center;
          border:2px solid #d1d5db; border-radius:12px; text-transform:uppercase; direction:ltr; }
  input:focus { outline:none; border-color:var(--brand); }
  button { width:100%; margin-top:14px; padding:13px; font-size:1rem; font-weight:700; color:#fff;
           background:var(--brand); border:0; border-radius:12px; cursor:pointer; }
  button:disabled { opacity:.6; cursor:default; }
  .note { font-size:.8rem; color:#6b7280; margin-top:10px; }
  .box { display:none; margin-top:14px; padding:12px; border-radius:12px; font-size:.88rem; }
  .box.show { display:block; }
  .box.ok { background:#ecfdf5; color:var(--ok); border:1px solid #a7f3d0; }
  .box.err { background:#fef2f2; color:var(--err); border:1px solid #fecaca; }
  .badge { display:inline-block; font-size:.75rem; padding:2px 8px; border-radius:999px;
           background:#eef2ff; color:#3730a3; margin-inline-start:6px; }
</style>
</head>
<body>
<div class="card">
  <h1>اتصال اعلان‌های سامانه به ایتا <span class="badge">سامانه انبار و فروش</span></h1>
  <p>با انجام این اتصال، اعلان‌ها و یادآوری‌های سامانه به‌صورت پیام در ایتا برای شما ارسال می‌شود.</p>
  <ol>
    <li>در نرم‌افزار سامانه، از پنجره‌ی «اعلان‌ها» دکمه‌ی «اتصال ایتا» را بزنید و کد ۶ حرفی را بگیرید.</li>
    <li>همان کد را در کادر زیر وارد کنید و دکمه‌ی «اتصال» را بزنید.</li>
  </ol>
  <label for="code">کد اتصال</label>
  <input id="code" maxlength="8" autocomplete="off" inputmode="text" placeholder="مثلاً K7M2Q9">
  <button id="btn" type="button">اتصال</button>
  <div id="box" class="box"></div>
  <p class="note" id="note"></p>
</div>
<script>
  var box = document.getElementById('box');
  var btn = document.getElementById('btn');
  var input = document.getElementById('code');
  var note = document.getElementById('note');

  function show(kind, text) {
    box.className = 'box show ' + kind;
    box.textContent = text;
  }
  function sdk() { try { return window.Eitaa && window.Eitaa.WebApp ? window.Eitaa.WebApp : null; } catch (e) { return null; } }

  var webApp = sdk();
  if (webApp) {
    if (webApp.expand) { try { webApp.expand(); } catch (e) { } }
    input.focus();
  } else {
    show('err', 'این صفحه باید از داخل برنامه‌ی ایتا باز شود. لطفاً ایتا را باز کنید، وارد بخش «برنامه‌ها» شوید و برنامه‌ی سامانه را اجرا کنید.');
    note.textContent = 'باز کردن در مرورگر، امکان اتصال را نمی‌دهد (برای امنیت، شناسه‌ی ایتا فقط از داخل خودِ ایتا خوانده می‌شود).';
    btn.disabled = true;
  }

  async function connect() {
    var code = (input.value || '').trim().toUpperCase();
    if (code.length < 6) { show('err', 'کد اتصال را کامل وارد کنید (۶ حرف).'); return; }

    btn.disabled = true;
    show('ok', 'در حال بررسی…');
    box.className = 'box show';
    box.style.background = '#f3f4f6'; box.style.color = '#374151'; box.style.border = '1px solid #e5e7eb';

    try {
      var res = await fetch('api/notifications/eitaa-link', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: code, initData: (webApp ? webApp.initData : '') })
      });
      var data = await res.json();
      if (data && data.ok) {
        show('ok', data.message || 'اتصال انجام شد.');
        btn.textContent = 'بستن';
        btn.disabled = false;
        btn.onclick = function () { try { if (webApp && webApp.close) webApp.close(); } catch (e) { } };
        return;
      }
      show('err', (data && data.message) || 'اتصال انجام نشد.');
    } catch (e) {
      show('err', 'ارتباط با سرور برقرار نشد: ' + (e && e.message ? e.message : e));
    }
    btn.disabled = false;
  }

  btn.addEventListener('click', connect);
  input.addEventListener('keydown', function (e) { if (e.key === 'Enter') connect(); });
</script>
</body>
</html>
""";

    public class PushSubscribeInput
    {
        public string? Endpoint { get; set; }
        public string? P256DH { get; set; }
        public string? Auth { get; set; }
    }
    public class PushUnsubscribeInput { public string? Endpoint { get; set; } }
}
