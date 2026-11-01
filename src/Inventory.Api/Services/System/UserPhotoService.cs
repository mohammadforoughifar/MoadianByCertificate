using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Inventory.Api.Services;

/// <summary>
/// عکس کاربران: آپلود با اندازه‌ی مناسب ( مربع ۳۰۰px ) و ذخیره در uploads/users/
/// — در دیتابیس فقط مسیر نسبی فایل ثبت می‌شود.
/// </summary>
public class UserPhotoService
{
    public const string DefaultPhoto = "users/default.jpg";

    private readonly FileStore _store;
    private readonly ILogger<UserPhotoService> _log;

    public UserPhotoService(FileStore store, ILogger<UserPhotoService> log)
    {
        _store = store;
        _log = log;
    }

    private static readonly HashSet<string> AllowedExts = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxBytes = 5 * 1024 * 1024; // 5MB

    /// <summary>
    /// پردازش و ذخیره‌ی عکس کاربر. مسیر نسبی (users/xxx.jpg) برمی‌گرداند.
    /// در صورت خطا، استثنای InvalidOperationException با پیام فارسی پرتاب می‌شود.
    /// </summary>
    public async Task<string> SaveUserPhotoAsync(int userId, Stream imageStream, string? originalName)
    {
        byte[] input;
        await using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms);
            input = ms.ToArray();
        }
        if (input.Length == 0) throw new InvalidOperationException("فایل خالی است.");
        if (input.Length > 5 * 1024 * 1024) throw new InvalidOperationException("حداکثر حجم عکس ۵ مگابایت است.");

        // فرمت مجاز (از امتداد یا magic bytes)
        var ext = Path.GetExtension(originalName ?? "");
        if (!AllowedExts.Contains(ext))
        {
            if (input.Length > 3 && input[0] == 0x89 && input[1] == 0x50) ext = ".png";
            else if (input.Length > 3 && input[0] == 0xFF && input[1] == 0xD8) ext = ".jpg";
            else if (input.Length > 12 && input[0] == 0x52 && input[1] == 0x49 && input[2] == 0x46 && input[3] == 0x46) ext = ".webp";
            else throw new InvalidOperationException("فرمت مجاز: JPG، PNG یا WEBP");
        }

        // بازسازی و مربع‌کردن مرکزی + کاهش به 300px
        byte[] output;
        try
        {
            using var img = await Image.LoadAsync<Rgb24>(new MemoryStream(input));
            var side = Math.Min(img.Width, img.Height);
            var cx = (img.Width - side) / 2;
            var cy = (img.Height - side) / 2;
            img.Mutate(x => x
                .Crop(new SixLabors.ImageSharp.Rectangle(cx, cy, side, side))
                .Resize(new SixLabors.ImageSharp.Size(300, 300)));
            await using var outMs = new MemoryStream();
            await img.SaveAsJpegAsync(outMs, new JpegEncoder { Quality = 85 });
            output = outMs.ToArray();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "خطا در پردازش عکس کاربر {UserId}", userId);
            throw new InvalidOperationException("فایل تصویر نامعتبر است — از یک عکس JPG/PNG عادی استفاده کنید.");
        }

        return await _store.SaveRawAsync("users", output, "jpg", ownerId: userId);
    }

    /// <summary>بایت‌های عکس کاربر (اگر وجود ندارد، عکس پیش‌فرض).</summary>
    public (byte[] Data, string ContentType) GetPhoto(string? relativePath)
    {
        var bytes = _store.ReadBytes(relativePath);
        if (bytes is { Length: > 0 }) return (bytes, "image/jpeg");
        var def = _store.ReadBytes(DefaultPhoto);
        if (def is { Length: > 0 }) return (def, "image/jpeg");
        // اگر حتی پیش‌فرض هم نیست (مثلاً قبل از seed)، یک نقطه‌ی خالی برمی‌گردانیم تا مرورگر خطا ندهد
        return (EmptyJpeg, "image/jpeg");
    }

    /// <summary>JPG سفید 1x1 — آخرین قید برای نبود عکس پیش‌فرض.</summary>
    private static byte[] EmptyJpeg => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA" +
        "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==");
}
