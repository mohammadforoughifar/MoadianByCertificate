using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// راه‌اندازی HTTPS برای اعلان سیستمی (مرورگرها اعلان و Service Worker را فقط روی HTTPS می‌دهند).
/// صفحهٔ <c>/https-setup</c> و دانلود <c>/ca.crt</c> بدون احراز هویت است تا گوشی بتواند پیش از نصب گواهی،
/// آن را از همان شبکهٔ داخلی دریافت کند.
/// </summary>
[ApiController]
[Route("api/system/https")]
[Authorize]
public class HttpsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HttpsCertificates _https;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public HttpsController(AppDbContext db, HttpsCertificates https, IHostEnvironment env, IConfiguration cfg)
    {
        _db = db;
        _https = https;
        _env = env;
        _cfg = cfg;
    }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) && v > 0 ? v : 0;

    private async Task<bool> IsAdminAsync()
    {
        if (User.IsInRole("Admin")) return true;
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId);
        if (!hasRoles) return false;
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, pm => pm.Id, (pid, pm) => pm)
            .AnyAsync(pm => pm.Module == "Settings" && pm.Action == "Manage");
    }

    /// <summary>وضعیت HTTPS داخلی + نشانی‌های امن قابل استفاده روی شبکه.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        if (MyUserId <= 0) return Unauthorized();
        var facts = _https.Describe(_env, _cfg);
        var host = Request.Host.Host;
        var urls = facts.Hosts
            .Where(h => !h.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"https://{h}:{facts.Port}")
            .ToList();
        if (!urls.Any()) urls.Add($"https://{host}:{facts.Port}");

        return Ok(new
        {
            enabled = facts.Enabled,
            port = facts.Port,
            secureNow = Request.IsHttps,
            currentOrigin = $"{Request.Scheme}://{Request.Host}",
            urls,
            caUrl = $"{Request.Scheme}://{Request.Host}/ca.crt",
            setupUrl = $"{Request.Scheme}://{Request.Host}/https-setup",
            caFingerprint = facts.CaFingerprint,
            serverFingerprint = facts.ServerFingerprint,
            serverExpiresAt = facts.ServerNotAfterUtc,
            caExpiresAt = facts.CaNotAfterUtc,
            hasCaFile = facts.HasCaFile,
            hasPfxFile = facts.HasPfxFile,
            dataDir = facts.DataDir,
            hosts = facts.Hosts,
            extraPorts = facts.ExtraPorts,
            redirectHttp = facts.RedirectHttp,
            publicPort = facts.PublicPort,
            publicCertificate = new
            {
                loaded = facts.PublicCertificate.Loaded,
                names = facts.PublicCertificate.Names,
                expiresAt = facts.PublicCertificate.NotAfterUtc,
                issuer = facts.PublicCertificate.Issuer,
                fingerprint = facts.PublicCertificate.Fingerprint,
                path = facts.PublicCertificate.Path,
                error = facts.PublicCertificate.Error,
                urls = facts.PublicCertificate.Names.Where(n => !n.StartsWith("*.")).Select(n => facts.PublicPort == 443 ? $"https://{n}" : $"https://{n}:{facts.PublicPort}").ToList()
            },
            messages = facts.Messages
        });
    }

    /// <summary>ساخت/بازسازی گواهی‌ها (فقط مدیر). newCa=true یعنی مرجع جدید هم ساخته شود (نصب‌های قبلی باید تکرار شوند).</summary>
    [HttpPost("regenerate")]
    public async Task<IActionResult> Regenerate([FromBody] RegenInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsAdminAsync()) return StatusCode(403, new { message = "تنها مدیر سامانه مجاز است." });
        var fresh = HttpsCertificates.Regenerate(_env, _cfg, input?.NewCa ?? false);
        var facts = fresh.Describe(_env, _cfg);
        return Ok(new
        {
            enabled = _https.Enabled, // وضعیت شنوندهٔ در حال اجرا (پورت را خود سرویس گرفته؛ بررسی دوباره اشتباهاً «غیرفعال» می‌داد)
            hosts = facts.Hosts,
            caFingerprint = facts.CaFingerprint,
            message = input?.NewCa == true
                ? "مرجع صدور گواهی تازه ساخته شد؛ برای اجرای گواهی جدید سرویس را یک‌بار ری‌استارت کنید و روی همهٔ دستگاه‌ها فایل ca.crt را دوباره نصب کنید."
                : "گواهی سرور بازسازی شد؛ برای اعمال روی پورت HTTPS، سرویس را یک‌بار ری‌استارت کنید. فایل CA تغییر نکرده و نصب‌های قبلی معتبر می‌مانند."
        });
    }

    /// <summary>خروجی گواهی سرور (Base64) برای درون‌ریزی در IIS یا هر وب‌سرور دیگر (فقط مدیر).</summary>
    [HttpPost("pfx")]
    public async Task<IActionResult> ExportPfx()
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!await IsAdminAsync()) return StatusCode(403, new { message = "تنها مدیر سامانه مجاز است." });
        var facts = _https.Describe(_env, _cfg);
        var pfx = _https.ReadPfxBase64();
        if (string.IsNullOrEmpty(pfx)) return NotFound(new { message = "فایل گواهی ساخته نشده است؛ ابتدا گواهی را بسازید." });
        return Ok(new
        {
            fileName = "totall-server.pfx",
            password = facts.PfxPassword,
            base64 = pfx,
            hosts = facts.Hosts,
            message = "این فایل را در IIS (بایندینگ HTTPS) درون‌ریزی کنید: IIS → پیوندهای سایت → افزودن → نوع https → گواهی → Import."
        });
    }

    public sealed class RegenInput { public bool NewCa { get; set; } }

    /// <summary>دانلود فایل گواهی مرجع (CA) — بدون احراز هویت، چون گوشی پیش از نصب گواهی به آن نیاز دارد.</summary>
    [HttpGet("/ca.crt")]
    [AllowAnonymous]
    public IActionResult DownloadCa()
    {
        var pem = _https.ReadCaPem();
        if (string.IsNullOrEmpty(pem)) return Content("گواهی هنوز ساخته نشده است.", "text/plain; charset=utf-8");
        Response.Headers.CacheControl = "no-store";
        return File(System.Text.Encoding.ASCII.GetBytes(pem), "application/x-x509-ca-cert", "ca.crt");
    }

    /// <summary>صفحهٔ راهنمای نصب گواهی روی گوشی/رایانه — از روی HTTP هم باز می‌شود.</summary>
    [HttpGet("/https-setup")]
    [AllowAnonymous]
    public IActionResult SetupPage()
    {
        var facts = _https.Describe(_env, _cfg);
        var host = Request.Host.Host;
        var urls = facts.Hosts.Where(h => !h.Equals("localhost", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!urls.Any()) urls.Add(host);
        var urlList = string.Join("", urls.Select(u => $"<li><code>https://{u}:{facts.Port}</code></li>"));
        var fingerprint = string.IsNullOrEmpty(facts.CaFingerprint) ? "—" : facts.CaFingerprint;
        var html = SetupHtml
            .Replace("{{URLS}}", urlList)
            .Replace("{{FINGERPRINT}}", fingerprint)
            .Replace("{{PORT}}", facts.Port.ToString())
            .Replace("{{STATUS}}", facts.HasCaFile ? "گواهی ساخته شده است" : "گواهی ساخته نشده — مدیر باید از تنظیمات نرم‌افزار آن را بسازد");
        return Content(html, "text/html; charset=utf-8");
    }

    private const string SetupHtml = """
<!DOCTYPE html>
<html lang="fa" dir="rtl">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>راه‌اندازی HTTPS برای اعلان اندروید</title>
<style>
  :root { --brand:#0d6efd; --ok:#15803d; --warn:#b45309; }
  * { box-sizing:border-box; }
  body { margin:0; padding:14px; font-family:'Vazirmatn','Shabnam',Tahoma,sans-serif; background:#f7f8fa; color:#1f2937; line-height:1.9; }
  .card { max-width:560px; margin:0 auto 14px; background:#fff; border-radius:16px; padding:18px; box-shadow:0 6px 24px rgba(15,23,42,.08); }
  h1 { font-size:1.1rem; margin:0 0 8px; }
  h2 { font-size:.95rem; margin:16px 0 6px; color:#0f172a; }
  p, li { font-size:.86rem; color:#4b5563; }
  ol, ul { padding-right:1.1rem; margin:.4rem 0 .8rem; }
  code { background:#f1f5f9; padding:2px 6px; border-radius:6px; direction:ltr; display:inline-block; font-size:.82rem; }
  a.btn { display:block; text-align:center; text-decoration:none; background:var(--brand); color:#fff; font-weight:700;
          padding:13px; border-radius:12px; margin:10px 0; }
  .badge { display:inline-block; font-size:.75rem; padding:3px 10px; border-radius:999px; background:#ecfdf5; color:var(--ok); }
  .note { background:#fffbeb; color:#92400e; border-radius:10px; padding:10px; font-size:.82rem; }
  .fp { direction:ltr; font-size:.68rem; word-break:break-all; color:#64748b; }
</style>
</head>
<body>
<div class="card">
  <h1>🔒 راه‌اندازی HTTPS برای اعلان سیستمی</h1>
  <p><span class="badge">{{STATUS}}</span></p>
  <p>
    مرورگرها اعلان سیستمی (مثل اعلان اندروید) و کارکرد پس‌زمینه را فقط روی نشانی <b>امن (HTTPS)</b> فعال می‌کنند.
    روی <code>http://</code> این امکان وجود ندارد. این سامانه یک گواهی داخلی دارد؛ با نصب فایل زیر روی دستگاه‌ها،
    نشانی‌های زیر «امن» می‌شوند و اعلان‌ها فعال می‌گردند.
  </p>
  <ul>{{URLS}}</ul>
  <a class="btn" href="/ca.crt" download="ca.crt">📥 دانلود گواهی (ca.crt)</a>

  <h2>📱 اندروید (Chrome)</h2>
  <ol>
    <li>همین صفحه را در گوشی باز کنید و دکمهٔ «دانلود گواهی» را بزنید.</li>
    <li>فایل دانلودشده را باز کنید (یا از تنظیمات ← امنیت ← رمزگذاری و اعتبارنامه‌ها ← نصب گواهی).</li>
    <li>گزینهٔ <b>«گواهی CA»</b> را انتخاب کنید و «نصب هر حال» را تأیید کنید.</li>
    <li>Chrome را کامل ببندید و با نشانی <code>https://آی‌پی:{{PORT}}</code> باز کنید — باید قفل امن نمایش داده شود.</li>
    <li>در نرم‌افزار: تنظیمات ← «اعلان گوشی و مرورگر» ← «فعال‌سازی اعلان این دستگاه».</li>
  </ol>

  <h2>🪟 ویندوز</h2>
  <ol>
    <li>فایل ca.crt را دانلود کنید، روی آن دوبار کلیک کنید ← «نصب گواهی».</li>
    <li>«رایانهٔ محلی» ← «قرار دادن همهٔ گواهی‌ها در مخزن زیر» ← مرور ← <b>«مراجع صدور گواهی ریشهٔ مورد اعتماد»</b> ← تأیید.</li>
    <li>مرورگر را ببندید و با <code>https://آی‌پی:{{PORT}}</code> باز کنید.</li>
  </ol>

  <h2>🍎 iPhone / iPad</h2>
  <ol>
    <li>فایل را در Safari دانلود کنید ← «پروفایل دانلود شد» را لمس کنید (یا تنظیمات ← پروفایل دانلود‌شده).</li>
    <li>تنظیمات ← «نصب پروفایل» ← تأیید با رمز.</li>
    <li><b>مهم:</b> تنظیمات ← عمومی ← About ← Certificate Trust Settings ← گواهی را کاملاً «فعال» کنید.</li>
  </ol>

  <div class="note">
    اگر گواهی نصب نشود، مرورگر هشدار «اتصال شما خصوصی نیست» نشان می‌دهد و اعلان فعال نمی‌شود.
    گاهی لازم است فایروال ویندوز یا شبکه، پورت <b>{{PORT}}</b> را باز کند (هنگام اجرای سرویس، پیام Allow Windows Firewall را تأیید کنید).
  </div>
  <p class="fp">اثر انگشت SHA-256 گواهی: {{FINGERPRINT}}</p>
</div>
</body>
</html>
""";
}
