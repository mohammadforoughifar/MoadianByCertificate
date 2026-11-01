using System.Security.Claims;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDbContext = Inventory.Api.Data.AppDbContext;

namespace Inventory.Api.Controllers;

/// <summary>احراز هویت (ورود با JWT) — برای همه نقش‌ها.
/// (از ApiControllerBase ارث نمی‌برد چون آن کلاس مخصوص نقش مدیر است.)</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly AppDbContext _db;
    private readonly FileStore _store;
    private readonly UserPhotoService _photos;

    public AuthController(IAuthService auth, AppDbContext db, FileStore store, UserPhotoService photos)
    {
        _auth = auth;
        _db = db;
        _store = store;
        _photos = photos;
    }

    /// <summary>ورود به سیستم.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return result is null
            ? Unauthorized(new { message = "نام کاربری یا رمز عبور اشتباه است." })
            : Ok(result);
    }

    /// <summary>تغییر رمز عبور توسط خود کاربر — هر نقشی (مدیر یا معرف).</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        await _auth.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        return Ok(new { ok = true });
    }
    // ================== عکس کاربران لاگین (آواتار نوار بالا و...) ==================

    /// <summary>عکس کاربر لاگین — برای آواتارها (بدون احراز هویت).</summary>
    [HttpGet("users/photo/{id:int}")]
    [AllowAnonymous]
    public ActionResult UserPhoto(int id)
    {
        var path = _db.Users.AsNoTracking().FirstOrDefault(x => x.Id == id)?.PhotoPath;
        var (data, ct) = _photos.GetPhoto(path);
        return File(data, ct);
    }

    /// <summary>دریافت امضای ثبت‌شدهٔ کاربر برای استفادهٔ ماژول‌های مجاز.</summary>
    [HttpGet("users/signature/{id:int}")]
    [Authorize]
    public async Task<IActionResult> UserSignature(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (user == null || string.IsNullOrWhiteSpace(user.SignaturePath)) return NotFound(new { message = "امضای کاربر ثبت نشده است." });
        var bytes = _store.ReadBytes(user.SignaturePath);
        if (bytes is null) return NotFound(new { message = "فایل امضای کاربر یافت نشد." });
        // Legacy files are served cropped as well, so previews do not need to wait for disk migration.
        return File(CropSignatureToAlphaBounds(bytes), "image/png", enableRangeProcessing: true);
    }

    private static byte[] CropSignatureToAlphaBounds(byte[] pngBytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(pngBytes);
            var left = image.Width;
            var top = image.Height;
            var right = -1;
            var bottom = -1;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].A == 0) continue;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            });
            if (right < left || bottom < top) return pngBytes;

            var crop = new Rectangle(left, top, right - left + 1, bottom - top + 1);
            using var trimmed = image.Clone(ctx => ctx
                .Crop(crop)
                .Pad(crop.Width + 8, crop.Height + 8, new Rgba32(0, 0, 0, 0)));
            using var output = new MemoryStream();
            trimmed.Save(output, new PngEncoder { ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha });
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not block a preview if an old signature cannot be decoded; the migration tool reports it separately.
            return pngBytes;
        }
    }

    /// <summary>آپلود یا جایگزینی امضای کاربر — فقط ادمین.</summary>
    [HttpPost("users/signature/{id:int}")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadUserSignature(int id, IFormFile file)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return NotFound(new { message = "کاربر پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایل امضا انتخاب نشده است." });
        const int maxBytes = 10 * 1024 * 1024;
        if (file.Length > maxBytes) return BadRequest(new { message = "حجم فایل امضا نباید بیشتر از ۱۰ مگابایت باشد." });

        // اعتبارسنجی بر اساس بایت‌های واقعی فایل است؛ پسوند و Content-Type قابل جعل هستند.
        using var upload = new MemoryStream((int)file.Length);
        await file.CopyToAsync(upload);
        var inputBytes = upload.ToArray();
        byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (inputBytes.Length < pngSignature.Length || !inputBytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
            return BadRequest(new { message = "فقط فایل PNG معتبر مجاز است." });

        byte[] processedPng;
        try
        {
            var info = Image.Identify(inputBytes);
            if (info == null || info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 40_000_000)
                return BadRequest(new { message = "ابعاد تصویر امضا معتبر نیست یا بیش از حد مجاز است." });

            using var image = Image.Load<Rgba32>(inputBytes); // decode کامل، نه صرفاً بررسی header
            if (image.Width != info.Width || image.Height != info.Height)
                return BadRequest(new { message = "تصویر PNG قابل رمزگشایی نیست." });

            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var px = row[x];
                        if (px.A == 0) continue; // preserve pixels already transparent in the uploaded PNG
                        var min = Math.Min(px.R, Math.Min(px.G, px.B));
                        var max = Math.Max(px.R, Math.Max(px.G, px.B));
                        if (min >= 250 && max - min <= 12)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                            continue;
                        }

                        // حذف زمینهٔ سفید با حفظ لبه‌های anti-alias امضا.
                        var foregroundAlpha = 255 - min;
                        if (foregroundAlpha <= 0) { row[x] = new Rgba32(0, 0, 0, 0); continue; }
                        var alpha = (foregroundAlpha * px.A + 127) / 255;
                        if (alpha <= 0) { row[x] = new Rgba32(0, 0, 0, 0); continue; }
                        static byte Unblend(byte channel, int a) => (byte)Math.Clamp(((channel - (255 - a)) * 255 + a / 2) / a, 0, 255);
                        row[x] = new Rgba32(Unblend(px.R, foregroundAlpha), Unblend(px.G, foregroundAlpha), Unblend(px.B, foregroundAlpha), (byte)alpha);
                    }
                }
            });

            // Crop to the alpha-channel bounds of visible ink and add only a 4 px transparent inset.
            var left = image.Width;
            var top = image.Height;
            var right = -1;
            var bottom = -1;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].A == 0) continue;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            });
            if (right < left || bottom < top)
                return BadRequest(new { message = "تصویر امضا پس از حذف زمینه، هیچ پیکسل قابل‌مشاهده‌ای ندارد." });

            var crop = new Rectangle(left, top, right - left + 1, bottom - top + 1);
            using var trimmed = image.Clone(ctx => ctx
                .Crop(crop)
                .Pad(crop.Width + 8, crop.Height + 8, new Rgba32(0, 0, 0, 0)));
            // Keep the stored signature compact without touching its transparent alpha channel.
            if (trimmed.Width > 1200 || trimmed.Height > 480)
                trimmed.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(1200, 480),
                    Sampler = KnownResamplers.Lanczos3
                }));
            await using var png = new MemoryStream();
            await trimmed.SaveAsync(png, new PngEncoder { ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha });
            processedPng = png.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return BadRequest(new { message = "فایل انتخاب‌شده PNG معتبر یا قابل رمزگشایی نیست." });
        }

        var originalFileName = $"user_{id}_signature_original_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png";
        using (var originalUpload = new MemoryStream(inputBytes, writable: false))
            await _store.SaveNamedAsync("UserSignatures", id, originalUpload, originalFileName, "originals");

        using var output = new MemoryStream(processedPng, writable: false);
        var previousPath = user.SignaturePath;
        var path = await _store.SaveNamedAsync("UserSignatures", id, output, $"user_{id}_signature.png");
        static string NormalizeSignaturePath(string value) => value.Replace('\\', '/').TrimStart('/');
        if (!string.IsNullOrWhiteSpace(previousPath)
            && !string.Equals(NormalizeSignaturePath(previousPath), NormalizeSignaturePath(path), StringComparison.OrdinalIgnoreCase))
            _store.Delete(previousPath);
        user.SignaturePath = path;
        await _db.SaveChangesAsync();
        return Ok(new { signaturePath = path, url = $"/api/auth/users/signature/{id}" });
    }

        /// <summary>تغییر عکس کاربر لاگین — فقط ادمین.</summary>
    [HttpPost("users/photo/{id:int}")]
    [Authorize(Roles = "Admin")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadUserPhoto(int id, IFormFile file)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return NotFound(new { message = "کاربر پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "عکسی انتخاب نشده است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        string newPath;
        try { newPath = await _photos.SaveUserPhotoAsync(id, ms, file.FileName); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        if (!string.IsNullOrWhiteSpace(user.PhotoPath)) _store.Delete(user.PhotoPath);
        user.PhotoPath = newPath;
        await _db.SaveChangesAsync();
        return Ok(new { photoPath = newPath });
    }
}
/// <summary>مدیریت کاربران — مدیر: کامل؛ اپراتور: بدون هیچ عملیاتی روی نقش/کاربر ادمین.</summary>
[Route("api/users")]
[Authorize(Roles = "Admin,Operator")]
[Inventory.Api.Infrastructure.ModulePermission("SystemUsers")]
public class UsersController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public UsersController(IAuthService auth) => _auth = auth;

    private bool IsAdmin => User.IsInRole("Admin");
    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;

    /// <summary>فهرست کاربران.</summary>
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _auth.GetUsersAsync(pagination: pagination), skip, take));

    /// <summary>ایجاد یا ویرایش کاربر (رمز فقط در صورت پر بودن تغییر می‌کند).
    /// اپراتور نمی‌تواند کاربر ادمین بسازد، نقش کسی را به ادمین تغییر دهد یا کاربر ادمین را ویرایش کند.</summary>
    [HttpPost]
    public async Task<ActionResult<UserDto>> Save([FromBody] UserDto user)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        return Ok(await _auth.SaveUserAsync(user, currentUserId: CurrentUserId, callerIsAdmin: IsAdmin));
    }

    /// <summary>حذف کاربر — اپراتور نمی‌تواند کاربر ادمین را حذف کند.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        await _auth.DeleteUserAsync(id, currentId, callerIsAdmin: IsAdmin);
        return Ok(new { ok = true });
    }
}

/// <summary>پنل معرف — هر معرف فقط اطلاعات خودش را می‌بیند.
/// (از ApiControllerBase ارث نمی‌برد چون آن کلاس مخصوص نقش مدیر است.)</summary>
[ApiController]
[Route("api/my")]
[Authorize(Roles = "Referrer")]
public class MyPanelController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IInventoryService _inventory;
    private readonly AppDbContext _db;

    public MyPanelController(IAuthService auth, IInventoryService inventory, AppDbContext db)
    {
        _auth = auth;
        _inventory = inventory;
        _db = db;
    }

    private int MyReferrerId =>
        int.TryParse(User.FindFirstValue("referrerId"), out var v) ? v : 0;

    private int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    /// <summary>آیا کاربر جاری از طریق نقش‌های RBAC این دسترسی را دارد؟</summary>
    private Task<bool> HasRbacAsync(string module, string action) =>
        _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action);

    /// <summary>داشبورد معرف جاری.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<ReferrerDashboard>> Dashboard()
    {
        if (MyReferrerId <= 0) return Forbid();
        return Ok(await _auth.GetReferrerDashboardAsync(MyReferrerId));
    }

    /// <summary>کالاهای موجود — فقط اگر مدیر دسترسی «مشاهده کالا» را برای این معرف فعال کرده باشد.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<List<ReferrerProductItem>>> Products([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (MyReferrerId <= 0) return Forbid();
        // مجوز مشاهده کالاها: یا پرمیشن RBAC (ReferrerPanel.MyProducts) یا فلگ مشاهده کالا روی خود معرف
        var hasRbac = await HasRbacAsync("ReferrerPanel", "MyProducts");
        return Ok(await Paging.ResultAsync(async pagination => await _inventory.GetReferrerProductsAsync(MyReferrerId, search, bypassFlag: hasRbac, pagination: pagination), skip, take));
    }

    /// <summary>اسناد پرداخت معرف جاری.</summary>
    [HttpGet("payments")]
    public async Task<ActionResult<List<ReferrerPayment>>> Payments([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (MyReferrerId <= 0) return Forbid();
        return Ok(await Paging.ResultAsync(async pagination => await _inventory.GetReferrerPaymentsAsync(MyReferrerId, pagination: pagination), skip, take));
    }

}
