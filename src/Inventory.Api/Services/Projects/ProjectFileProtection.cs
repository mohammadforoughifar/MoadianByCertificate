using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس رمزنگاری پیوست‌های پروژه:
/// ۱) فایل‌ها با AES-256-GCM روی دیسک سرور رمزنگاری می‌شوند (نام فیزیکی تصادفی).
/// ۲) نام اصلی فایل هم رمزنگاری شده و در دیتابیس ذخیره می‌شود.
/// کلید از تنظیمات Files:EncryptionKey خوانده می‌شود؛ در صورت نبود، یک کلید تصادفی
/// در فایل .key داخل پوشه ذخیره‌سازی ساخته و نگهداری می‌شود (این فایل را بکاپ بگیرید!).
/// </summary>
public interface IProjectFileProtection
{
    /// <summary>رمزنگاری داده‌ها و ذخیره با نام تصادفی — خروجی: نام فایل ذخیره‌شده</summary>
    Task<string> EncryptAndStoreAsync(Stream data, CancellationToken ct = default);

    /// <summary>رمزنگاری و ذخیره در پوشهٔ اختصاصی (مثل «کد پروژه/تصاویر») — خروجی: مسیر نسبی داخل SecureFiles</summary>
    Task<string> EncryptAndStoreAsync(Stream data, string? subFolder, CancellationToken ct = default);

    /// <summary>سازماندهی فایل‌های قدیمیِ بدون‌پوشه بر اساس دیتابیس: هر پروژه پوشهٔ خودش + جداسازی تصاویر/مستندات/سایر</summary>
    Task OrganizeProjectFoldersAsync(Data.AppDbContext db, CancellationToken ct = default);

    /// <summary>رمزگشایی فایل ذخیره‌شده — خروجی: استریم حاوی محتوای اصلی</summary>
    Task<MemoryStream> LoadAndDecryptAsync(string storedFileName, CancellationToken ct = default);

    /// <summary>رمزنگاری رشته (نام فایل) — خروجی Base64</summary>
    string EncryptString(string plain);

    /// <summary>رمزگشایی رشته (نام فایل)</summary>
    string DecryptString(string cipher);

    /// <summary>حذف فیزیکی فایل ذخیره‌شده</summary>
    void Delete(string storedFileName);

    /// <summary>مسیر کامل فایل ذخیره‌شده (جهت بررسی وجود)</summary>
    string GetPath(string storedFileName);
}

public class ProjectFileProtection : IProjectFileProtection
{
    private const int NonceSize = 12; // GCM استاندارد
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly string _root;
    private readonly string _rootLegacy;

    /// <summary>نام پوشهٔ نوع فایل بر اساس پسوند — تصاویر از مستندات جدا می‌شوند (درخواست کاربر)</summary>
    public static string KindFolderOf(string? extension) => (extension ?? "").Trim().ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".tif" or ".tiff" => "تصاویر",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" or ".csv" => "مستندات",
        _ => "سایر"
    };

    /// <summary>امن‌سازی نام پوشه (کد پروژه و…) — جلوگیری از کاراکترهای نامعتبر و «..» (Path Traversal)</summary>
    public static string SanitizeSegment(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "بدون‌کد";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Trim().Length);
        foreach (var c in s.Trim())
            sb.Append(invalid.Contains(c) || c is '/' or '\\' or '.' ? '-' : c);
        var t = sb.ToString().Trim('.', ' ', '-');
        return string.IsNullOrEmpty(t) ? "بدون‌کد" : t;
    }

    public ProjectFileProtection(IConfiguration config, IWebHostEnvironment env)
    {
        var configuredRoot = config["Files:SecureRoot"];
        // محل ذخیره‌سازی یکپارچه پیوست‌های پروژه: زیر wwwroot/uploads/projects
        _root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "projects")
            : configuredRoot;
        // مسیر قدیمی جهت سازگاری
        _rootLegacy = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(env.ContentRootPath, "wwwroot", "SecureFiles")
            : configuredRoot;
        Directory.CreateDirectory(_root);
        MigrateLegacyFiles();

        _key = LoadOrCreateKey(config["Files:EncryptionKey"]);
    }

    /// <summary>انتقال یک‌باره فایل‌های مسیر قدیمی (ContentRoot/SecureFiles) به مسیر جدید زیر wwwroot</summary>
    private void MigrateLegacyFiles()
    {
        try
        {
            if (string.Equals(_root, _rootLegacy, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(_rootLegacy)) return;

            var moved = 0;
            foreach (var src in Directory.EnumerateFiles(_rootLegacy))
            {
                var dest = Path.Combine(_root, Path.GetFileName(src));
                // از جملهٔ فایل‌های مهم: «.key» (کلید رمزنگاری) — بدون آن فایل‌های قبلی باز نمی‌شوند
                if (File.Exists(dest)) continue;
                File.Copy(src, dest);
                moved++;
            }
            if (moved > 0)
                Console.WriteLine($"[SecureFiles] {moved} فایل از مسیر قدیمی به wwwroot/SecureFiles منتقل شد.");
        }
        catch (Exception ex)
        {
            // انتقال نباید مانع اجرای برنامه شود
            Console.WriteLine($"[SecureFiles] انتقال فایل‌های قدیمی ناموفق بود: {ex.Message}");
        }
    }

    private byte[] LoadOrCreateKey(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // هر رشته‌ای (Base64 یا عبارت عادی) با SHA256 به کلید ۳۲ بایتی تبدیل می‌شود
            return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        }

        var keyFile = Path.Combine(_root, ".key");
        if (File.Exists(keyFile))
            return Convert.FromBase64String(File.ReadAllText(keyFile).Trim());

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyFile, Convert.ToBase64String(key));
        return key;
    }

    // ==================== فایل ====================

    public Task<string> EncryptAndStoreAsync(Stream data, CancellationToken ct = default)
        => EncryptAndStoreAsync(data, null, ct);

    public async Task<string> EncryptAndStoreAsync(Stream data, string? subFolder, CancellationToken ct = default)
    {
        using var plain = new MemoryStream();
        await data.CopyToAsync(plain, ct);
        var plainBytes = plain.ToArray();

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plainBytes, cipher, tag);

        var storedName = $"{Guid.NewGuid():N}.bin";
        // ساختار درخواستی کاربر: «SecureFiles/کد پروژه/تصاویر|مستندات|سایر/گُید.bin» — مسیر نسبی (با اسلش) در دیتابیس ذخیره می‌شود
        var rel = string.IsNullOrWhiteSpace(subFolder)
            ? storedName
            : string.Join('/', subFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(SanitizeSegment)) + "/" + storedName;
        var path = GetNewPath(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // چیدمان: [nonce][cipher][tag]
        await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            await fs.WriteAsync(nonce, ct);
            await fs.WriteAsync(cipher, ct);
            await fs.WriteAsync(tag, ct);
        }
        return rel;
    }

    /// <summary>فایل‌های قدیمیِ تخت (بدون پوشه) را بر اساس دیتابیس به پوشهٔ پروژهٔ خودشان + نوع فایل منتقل می‌کند — ایدمپوتنت است.</summary>
    public async Task OrganizeProjectFoldersAsync(Data.AppDbContext db, CancellationToken ct = default)
    {
        try
        {
            var flat = await db.ProjectAttaches
                .Where(a => !a.IsDelete && !a.StoredFileName.Contains("/") && !a.StoredFileName.Contains("\\"))
                .Select(a => new { a.Id, a.StoredFileName, a.Extension, a.ProjectId })
                .ToListAsync(ct);
            Console.WriteLine($"[SecureFiles] بررسی ساختار پوشه‌های پیوست: {flat.Count} فایلِ قدیمی بدون‌پوشه یافت شد.");
            if (flat.Count == 0) return;

            var ids = flat.Select(f => f.ProjectId).ToList();
            var codes = await db.ProjectEntryExits.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.CodeProject })
                .ToDictionaryAsync(p => p.Id, p => p.CodeProject, ct);

            var moved = 0;
            foreach (var a in flat)
            {
                var flatName = Path.GetFileName(a.StoredFileName);
                var src = GetPath(flatName);
                if (!File.Exists(src)) continue; // فایل گمشده — رکورد برای بررسی مدیر می‌ماند

                codes.TryGetValue(a.ProjectId, out var code);
                var folder = $"{SanitizeSegment(code ?? $"P-{a.ProjectId}")}/{KindFolderOf(a.Extension)}";
                var rel = $"{folder}/{flatName}";
                var dest = GetNewPath(rel); // مقصد — بدون فال‌بک به فایل تخت (وگرنه خود مبدا یافت و رد می‌شد!)
                if (File.Exists(dest)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(src, dest);

                var row = await db.ProjectAttaches.FirstAsync(x => x.Id == a.Id, ct);
                row.StoredFileName = rel;
                moved++;
            }
            if (moved > 0)
            {
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"[SecureFiles] {moved} پیوست به پوشهٔ اختصاصی پروژه (تصاویر/مستندات) منتقل شد.");
            }
        }
        catch (Exception ex)
        {
            // سازماندهی نباید مانع اجرای برنامه شود — اجرای بعدی دوباره تلاش می‌کند
            Console.WriteLine($"[SecureFiles] سازماندهی پوشه‌های پروژه ناموفق بود: {ex.Message}");
        }
    }

    public async Task<MemoryStream> LoadAndDecryptAsync(string storedFileName, CancellationToken ct = default)
    {
        var path = GetPath(storedFileName);
        if (!File.Exists(path)) throw new FileNotFoundException("فایل روی سرور پیدا نشد.", storedFileName);

        var all = await File.ReadAllBytesAsync(path, ct);
        if (all.Length < NonceSize + TagSize)
        {
            // اگر فایل خیلی کوچک است یا بدون رمزنگاری ذخیره شده، مستقیم همان بایت‌ها بازگردانده شوند
            return new MemoryStream(all, writable: false);
        }

        try
        {
            var nonce = all.AsMemory(0, NonceSize);
            var cipher = all.AsMemory(NonceSize, all.Length - NonceSize - TagSize);
            var tag = all.AsMemory(all.Length - TagSize, TagSize);

            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(_key, TagSize))
                aes.Decrypt(nonce.Span, cipher.Span, tag.Span, plain.AsSpan());

            return new MemoryStream(plain, writable: false);
        }
        catch
        {
            // فال‌بک سازگاری: اگر رمزگشایی AES-GCM ناموفق بود (مثلاً فایل بدون رمزنگاری یا با کلید قبلی ذخیره شده)، بایت‌های دیسک خوانده شوند
            return new MemoryStream(all, writable: false);
        }
    }

    public void Delete(string storedFileName)
    {
        var path = GetPath(storedFileName);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>مسیر «مقصد» برای ذخیره/جابه‌جایی فایل جدید — بدون فال‌بک‌های سازگاری (فقط کنترل Path Traversal)</summary>
    private string GetNewPath(string relative)
    {
        var rel = (relative ?? "").Replace('\\', '/').TrimStart('/');
        var pNew = Path.GetFullPath(Path.Combine(_root, rel));
        var rootFull = Path.GetFullPath(_root);
        if (!pNew.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            pNew = Path.Combine(rootFull, Path.GetFileName(rel));
        return pNew;
    }

    public string GetPath(string storedFileName)
    {
        // جلوگیری از Path Traversal — مسیر حاصل باید حتماً داخل ریشهٔ SecureFiles بماند
        var rel = (storedFileName ?? "").Replace('\\', '/').TrimStart('/');
        var pNew = Path.GetFullPath(Path.Combine(_root, rel));
        var rootFull = Path.GetFullPath(_root);
        if (!pNew.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            pNew = Path.Combine(rootFull, Path.GetFileName(rel)); // هر مسیر مشکوکی به نام خالص فروکاسته می‌شود
        if (File.Exists(pNew)) return pNew;

        // سازگاری ۱: فایل‌های تختِ بدون‌پوشه (ساختار قدیمی ممکن است هنوز در ریشه باشند — قبل از سازماندهی)
        var flatName = Path.GetFileName(rel);
        var pFlat = Path.Combine(rootFull, flatName);
        if (!string.Equals(pFlat, pNew, StringComparison.OrdinalIgnoreCase) && File.Exists(pFlat)) return pFlat;

        // سازگاری ۲: مسیر قدیمی ContentRoot/SecureFiles
        if (!string.Equals(_root, _rootLegacy, StringComparison.OrdinalIgnoreCase))
        {
            var pOld = Path.Combine(_rootLegacy, flatName);
            if (File.Exists(pOld)) return pOld;
        }
        return pNew;
    }

    // ==================== رشته (نام فایل) ====================

    public string EncryptString(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[bytes.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, bytes, cipher, tag);
        var result = new byte[NonceSize + cipher.Length + TagSize];
        nonce.CopyTo(result, 0);
        cipher.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + cipher.Length);
        return Convert.ToBase64String(result);
    }

    public string DecryptString(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return "";
        try
        {
            var all = Convert.FromBase64String(cipherText);
            if (all.Length >= NonceSize + TagSize)
            {
                var nonce = all.AsMemory(0, NonceSize);
                var cipher = all.AsMemory(NonceSize, all.Length - NonceSize - TagSize);
                var tag = all.AsMemory(all.Length - TagSize, TagSize);
                var plain = new byte[cipher.Length];
                using (var aes = new AesGcm(_key, TagSize))
                    aes.Decrypt(nonce.Span, cipher.Span, tag.Span, plain.AsSpan());
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch
        {
            // فال‌بک سازگاری: اگر متن ورودی به صورت عادی (غیررمزنگاری‌شده) ذخیره شده، خود همان برگردانده شود
            if (cipherText.Contains('.') || !cipherText.Contains('='))
                return cipherText;
        }
        return cipherText;
    }
}
