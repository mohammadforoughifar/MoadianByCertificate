using System.Security.Claims;
using System.Security.Cryptography;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.ItAssets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// گاوصندوق رمزهای تجهیزات (مدیریت سخت‌افزار) — سرور، سوییچ منیجبل، میکروتیک، فایروال، سامانه‌ها.
/// <para>• به تفکیک شرکت: هر کاربر فقط شرکت‌های مجاز خودش (UserCompanyAccesses) را می‌بیند.</para>
/// <para>• رمزها با AES-256-GCM رمزنگاری می‌شوند و در لیست‌ها هرگز برگردانده نمی‌شوند.</para>
/// <para>• نمایش رمز: مجوز ItVault.Reveal + تأیید دوبارهٔ رمز ورود کاربر (۱۰ دقیقه) + ثبت در لاگ.</para>
/// </summary>
[ApiController]
[Route("api/it-vault")]
public sealed class ItVaultController : ControllerBase
{
    public static readonly string[] Categories = { "Server", "Switch", "Mikrotik", "Firewall", "System", "Other" };
    private const string UnlockHeader = "X-Vault-Unlock";

    private readonly AppDbContext _db;
    private readonly CredentialVaultCrypto _crypto;
    private readonly IEffectivePermissions _perms;

    public ItVaultController(AppDbContext db, CredentialVaultCrypto crypto, IEffectivePermissions perms)
    {
        _db = db;
        _crypto = crypto;
        _perms = perms;
    }

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool IsAdmin => User.IsInRole("Admin");
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private HashSet<string>? _permCache;
    private async Task<bool> HasAsync(string action)
    {
        _permCache ??= await _perms.GetAsync(User);
        return _permCache.Contains("ItVault." + action);
    }

    /// <summary>شرکت‌های مجاز کاربر — همان منطق انتخاب شرکت فعال (CompanyContextController).</summary>
    private async Task<List<int>> AllowedCompanyIdsAsync()
    {
        var ids = await _db.UserCompanyAccesses.AsNoTracking()
            .Where(x => x.UserId == UserId && x.Company.IsActive)
            .Select(x => x.CompanyId).ToListAsync();
        if (ids.Count > 0) return ids;

        var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
        var legacy = await _db.SystemUsers.AsNoTracking()
            .Where(u => u.Username == username && u.CompanyId != null)
            .Select(u => u.CompanyId!.Value).FirstOrDefaultAsync();
        if (legacy > 0 && await _db.SystemCompanies.AnyAsync(c => c.Id == legacy && c.IsActive))
            return new List<int> { legacy };

        if (IsAdmin)
            return await _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync();
        return new List<int>();
    }

    private async Task<string> MyNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId);
        return me is null ? (User.FindFirstValue(ClaimTypes.Name) ?? "") : UserDisplay.Name(me);
    }

    private async Task LogAsync(ItCredential c, string action, string? details = null)
    {
        _db.ItCredentialLogs.Add(new ItCredentialLog
        {
            CredentialId = c.Id, CompanyId = c.CompanyId, UserId = UserId, UserName = await MyNameAsync(),
            Action = action, ClientIp = ClientIp, Details = details, At = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    // ================================================================== شرکت‌ها

    [HttpGet("companies")]
    public async Task<IActionResult> Companies()
    {
        if (!await HasAsync("View")) return Forbid();
        var ids = await AllowedCompanyIdsAsync();
        var list = await _db.SystemCompanies.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, Count = _db.ItCredentials.Count(x => x.CompanyId == c.Id) })
            .ToListAsync();
        return Ok(list);
    }

    // ================================================================== لیست (بدون رمز)

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int companyId, [FromQuery] string? category, [FromQuery] string? q)
    {
        if (!await HasAsync("View")) return Forbid();
        var allowed = await AllowedCompanyIdsAsync();
        if (!allowed.Contains(companyId))
            return BadRequest(new { message = "به این شرکت دسترسی ندارید یا شرکت انتخاب نشده است." });

        var query = _db.ItCredentials.AsNoTracking().Where(c => c.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(category) && category != "All") query = query.Where(c => c.Category == category);
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(c => c.Title.Contains(q)
                || (c.Host != null && c.Host.Contains(q))
                || (c.Username != null && c.Username.Contains(q))
                || (c.Url != null && c.Url.Contains(q))
                || (c.Location != null && c.Location.Contains(q))
                || (c.Notes != null && c.Notes.Contains(q)));
        }

        var rows = await query.OrderBy(c => c.Category).ThenBy(c => c.Title)
            .Select(c => new
            {
                c.Id, c.CompanyId, c.Category, c.Title, c.Host, c.Port, c.AccessMethod, c.Url, c.Username,
                HasPassword = c.PasswordCipher != null,
                HasExtraSecret = c.ExtraSecretCipher != null,
                c.Location, c.Notes, c.IsActive, c.PasswordChangedAt, c.CreatedAt, c.UpdatedAt, c.UpdatedByName
            })
            .ToListAsync();
        return Ok(rows);
    }

    // ================================================================== ایجاد / ویرایش / حذف

    public sealed class SaveDto
    {
        public int CompanyId { get; set; }
        public string Category { get; set; } = "Server";
        public string Title { get; set; } = "";
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? AccessMethod { get; set; }
        public string? Url { get; set; }
        public string? Username { get; set; }
        /// <summary>در ویرایش: null یا خالی = رمز قبلی حفظ شود.</summary>
        public string? Password { get; set; }
        public bool ClearPassword { get; set; }
        /// <summary>اطلاعات محرمانهٔ تکمیلی؛ در ویرایش null = بدون تغییر.</summary>
        public string? ExtraSecret { get; set; }
        public bool ClearExtraSecret { get; set; }
        public string? Location { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }

    private string? Validate(SaveDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Title)) return "عنوان را وارد کنید.";
        if (!Categories.Contains(d.Category)) return "نوع تجهیز نامعتبر است.";
        if (d.Port is < 1 or > 65535) return "شماره پورت باید بین ۱ تا ۶۵۵۳۵ باشد.";
        if (d.Password is { Length: > 1000 } || d.ExtraSecret is { Length: > 4000 }) return "طول رمز/اطلاعات محرمانه بیش از حد مجاز است.";
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveDto d)
    {
        if (!await HasAsync("Create")) return Forbid();
        var err = Validate(d);
        if (err != null) return BadRequest(new { message = err });
        if (!(await AllowedCompanyIdsAsync()).Contains(d.CompanyId))
            return BadRequest(new { message = "به این شرکت دسترسی ندارید." });

        var now = DateTime.Now;
        var c = new ItCredential
        {
            CompanyId = d.CompanyId, Category = d.Category, Title = Clean(d.Title, 200)!,
            Host = Clean(d.Host, 200), Port = d.Port, AccessMethod = Clean(d.AccessMethod, 30), Url = Clean(d.Url, 500),
            Username = Clean(d.Username, 200), Location = Clean(d.Location, 250), Notes = Clean(d.Notes, 2000),
            IsActive = d.IsActive, CreatedAt = now, CreatedByUserId = UserId,
            UpdatedAt = now, UpdatedByUserId = UserId, UpdatedByName = await MyNameAsync(),
            KeyId = _crypto.KeyId
        };
        if (!string.IsNullOrEmpty(d.Password))
        {
            c.PasswordCipher = _crypto.Encrypt(d.Password, c.CompanyId);
            c.PasswordChangedAt = now;
        }
        if (!string.IsNullOrEmpty(d.ExtraSecret)) c.ExtraSecretCipher = _crypto.Encrypt(d.ExtraSecret, c.CompanyId);

        _db.ItCredentials.Add(c);
        await _db.SaveChangesAsync();
        await LogAsync(c, "Create", c.Title);
        return Ok(new { id = c.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveDto d)
    {
        if (!await HasAsync("Update")) return Forbid();
        var err = Validate(d);
        if (err != null) return BadRequest(new { message = err });
        var allowed = await AllowedCompanyIdsAsync();
        var c = await _db.ItCredentials.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null || !allowed.Contains(c.CompanyId)) return NotFound(new { message = "رکورد پیدا نشد." });
        if (!allowed.Contains(d.CompanyId)) return BadRequest(new { message = "به شرکت مقصد دسترسی ندارید." });

        var changes = new List<string>();
        // اگر شرکت عوض شود، متن‌های رمز باید با AAD شرکت جدید دوباره رمزنگاری شوند
        if (d.CompanyId != c.CompanyId)
        {
            try
            {
                var oldPass = string.IsNullOrEmpty(d.Password) ? _crypto.Decrypt(c.PasswordCipher, c.CompanyId) : null;
                var oldExtra = d.ExtraSecret == null ? _crypto.Decrypt(c.ExtraSecretCipher, c.CompanyId) : null;
                if (oldPass != null) c.PasswordCipher = _crypto.Encrypt(oldPass, d.CompanyId);
                if (oldExtra != null) c.ExtraSecretCipher = _crypto.Encrypt(oldExtra, d.CompanyId);
            }
            catch (CryptographicException)
            {
                return BadRequest(new { message = "رمز فعلی با کلید فعلی گاوصندوق قابل رمزگشایی نیست؛ برای انتقال شرکت، رمز را دوباره وارد کنید." });
            }
            changes.Add($"انتقال از شرکت {c.CompanyId} به {d.CompanyId}");
            c.CompanyId = d.CompanyId;
        }

        c.Category = d.Category;
        c.Title = Clean(d.Title, 200)!;
        c.Host = Clean(d.Host, 200);
        c.Port = d.Port;
        c.AccessMethod = Clean(d.AccessMethod, 30);
        c.Url = Clean(d.Url, 500);
        c.Username = Clean(d.Username, 200);
        c.Location = Clean(d.Location, 250);
        c.Notes = Clean(d.Notes, 2000);
        c.IsActive = d.IsActive;

        if (d.ClearPassword) { c.PasswordCipher = null; c.PasswordChangedAt = DateTime.Now; changes.Add("حذف رمز"); }
        else if (!string.IsNullOrEmpty(d.Password))
        {
            c.PasswordCipher = _crypto.Encrypt(d.Password, c.CompanyId);
            c.PasswordChangedAt = DateTime.Now;
            changes.Add("تغییر رمز");
        }
        if (d.ClearExtraSecret) { c.ExtraSecretCipher = null; changes.Add("حذف اطلاعات محرمانه"); }
        else if (!string.IsNullOrEmpty(d.ExtraSecret))
        {
            c.ExtraSecretCipher = _crypto.Encrypt(d.ExtraSecret, c.CompanyId);
            changes.Add("تغییر اطلاعات محرمانه");
        }
        if (c.PasswordCipher != null || c.ExtraSecretCipher != null) c.KeyId = _crypto.KeyId;

        c.UpdatedAt = DateTime.Now;
        c.UpdatedByUserId = UserId;
        c.UpdatedByName = await MyNameAsync();
        await _db.SaveChangesAsync();
        await LogAsync(c, "Update", changes.Count > 0 ? string.Join("، ", changes) : null);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        var allowed = await AllowedCompanyIdsAsync();
        var c = await _db.ItCredentials.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null || !allowed.Contains(c.CompanyId)) return NotFound(new { message = "رکورد پیدا نشد." });
        await LogAsync(c, "Delete", c.Title);
        _db.ItCredentials.Remove(c);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================================================================== باز کردن گاوصندوق و نمایش رمز

    public sealed class UnlockDto { public string Password { get; set; } = ""; }

    /// <summary>تأیید دوبارهٔ رمز ورود کاربر ← توکن ۱۰ دقیقه‌ای برای نمایش/کپی رمزها.</summary>
    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock([FromBody] UnlockDto d)
    {
        if (!await HasAsync("Reveal")) return Forbid();
        if (_crypto.IsLockedOut(UserId, out var remaining))
            return StatusCode(429, new { message = $"به‌دلیل تلاش‌های ناموفق، گاوصندوق تا {Math.Ceiling(remaining.TotalMinutes)} دقیقهٔ دیگر قفل است." });

        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId);
        if (me is null || string.IsNullOrEmpty(d.Password) || !AuthService.VerifyPassword(d.Password, me.PasswordHash))
        {
            _crypto.RegisterFailure(UserId);
            _db.ItCredentialLogs.Add(new ItCredentialLog
            {
                CredentialId = 0, CompanyId = 0, UserId = UserId, UserName = me is null ? null : UserDisplay.Name(me),
                Action = "UnlockFailed", ClientIp = ClientIp, At = DateTime.Now
            });
            await _db.SaveChangesAsync();
            return BadRequest(new { message = "رمز ورود شما اشتباه است." });
        }

        _crypto.RegisterSuccess(UserId);
        var (token, expiresAt) = _crypto.CreateUnlockToken(UserId);
        return Ok(new { token, expiresAt, minutes = (int)CredentialVaultCrypto.UnlockLifetime.TotalMinutes });
    }

    public sealed class RevealDto
    {
        /// <summary>reveal | copy — فقط برای لاگ</summary>
        public string? Purpose { get; set; }
        /// <summary>توکن باز بودن گاوصندوق (از /unlock) — نام «Token» تا در لاگ عملیات پوشانده شود.</summary>
        public string? Token { get; set; }
    }

    /// <summary>رمزگشایی رمز یک رکورد — نیازمند هدر X-Vault-Unlock معتبر؛ هر بار در لاگ ثبت می‌شود.</summary>
    [HttpPost("{id:int}/reveal")]
    public async Task<IActionResult> Reveal(int id, [FromBody] RevealDto? d, [FromHeader(Name = UnlockHeader)] string? unlock)
    {
        if (!await HasAsync("Reveal")) return Forbid();
        if (!_crypto.ValidateUnlockToken(string.IsNullOrWhiteSpace(d?.Token) ? unlock : d!.Token, UserId))
            return StatusCode(423, new { message = "گاوصندوق قفل است؛ رمز ورود خود را دوباره وارد کنید.", locked = true });

        var allowed = await AllowedCompanyIdsAsync();
        var c = await _db.ItCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null || !allowed.Contains(c.CompanyId)) return NotFound(new { message = "رکورد پیدا نشد." });

        string? password, extra;
        try
        {
            password = _crypto.Decrypt(c.PasswordCipher, c.CompanyId);
            extra = _crypto.Decrypt(c.ExtraSecretCipher, c.CompanyId);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            var hint = !string.IsNullOrEmpty(c.KeyId) && c.KeyId != _crypto.KeyId
                ? " این رکورد با کلید دیگری رمزنگاری شده — فایل کلید گاوصندوق (vault.key) سرور تغییر کرده است؛ فایل قبلی را برگردانید."
                : "";
            return StatusCode(500, new { message = "رمزگشایی ناموفق بود." + hint });
        }

        var purpose = string.Equals(d?.Purpose, "copy", StringComparison.OrdinalIgnoreCase) ? "Copy" : "Reveal";
        await LogAsync(c, purpose);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { password, extraSecret = extra });
    }

    // ================================================================== لاگ

    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] int companyId, [FromQuery] int? credentialId, [FromQuery] int take = 200)
    {
        if (!await HasAsync("ViewLog")) return Forbid();
        var allowed = await AllowedCompanyIdsAsync();
        if (!allowed.Contains(companyId)) return BadRequest(new { message = "به این شرکت دسترسی ندارید." });
        take = Math.Clamp(take, 1, 1000);

        var q = _db.ItCredentialLogs.AsNoTracking().Where(l => l.CompanyId == companyId);
        if (credentialId is > 0) q = q.Where(l => l.CredentialId == credentialId);
        var rows = await q.OrderByDescending(l => l.At).Take(take)
            .Select(l => new
            {
                l.Id, l.CredentialId, l.UserName, l.Action, l.ClientIp, l.Details, l.At,
                Title = _db.ItCredentials.Where(c => c.Id == l.CredentialId).Select(c => c.Title).FirstOrDefault()
            })
            .ToListAsync();
        return Ok(rows);
    }
}
