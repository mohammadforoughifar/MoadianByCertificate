using Microsoft.AspNetCore.Http;

namespace Inventory.Api.Services;

/// <summary>
/// ذخیره‌سازی یکپارچه فایل‌ها روی دیسک — همه‌ی فایل‌ها بر اساس ماژول در پوشه‌ی wwwroot/uploads/{module}/ قرار می‌گیرند.
/// در دیتابیس فقط مسیر نسبی (اطلاعات) ثبت می‌شود.
/// ساختار: wwwroot/uploads/{module}/{refId}/{guid}_{نام-اصلی}
///
/// آرشیو اسناد (ماژول DocVersion): اگر «DocArchive:StorageRoot» در appsettings پر شده باشد، فایل‌های مدارک
/// (آپلود دستی + ورود انبوه) در همان پوشه‌ی بیرون از برنامه ذخیره می‌شوند، مثلاً D:\InventoryData\DocArchive.
/// این کار روی IIS حیاتی است: پوشه‌ی برنامه با هر انتشار جدید جایگزین می‌شود و wwwroot/uploads همراهش پاک می‌شود.
/// مسیر نسبی ثبت‌شده در دیتابیس عوض نمی‌شود («DocVersion/{versionId}/{guid}_{نام}»)، پس فایل‌های قبلی
/// که هنوز در wwwroot/uploads/DocVersion هستند همچنان پیدا می‌شوند.
/// </summary>
public class FileStore
{
    /// <summary>نام ماژول پیوست‌های نسخه‌ی مدارک آرشیو.</summary>
    public const string DocArchiveModule = "DocVersion";

    /// <summary>کلید تنظیمات پوشه‌ی ذخیره‌ی مدارک آرشیو (خالی = همان wwwroot/uploads).</summary>
    public const string DocArchiveStorageRootKey = "DocArchive:StorageRoot";

    private readonly string _root;
    private readonly string _webRoot;
    private readonly string _contentRoot;
    private readonly string? _docArchiveRoot;

    public FileStore(IWebHostEnvironment env) : this(env, null) { }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public FileStore(IWebHostEnvironment env, IConfiguration? configuration)
    {
        _contentRoot = env.ContentRootPath;
        _webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _root = Path.Combine(_webRoot, "uploads");
        Directory.CreateDirectory(_root);

        _docArchiveRoot = ResolveDocArchiveRoot(configuration?[DocArchiveStorageRootKey], _contentRoot);
        if (_docArchiveRoot is not null) Directory.CreateDirectory(_docArchiveRoot);
    }

    public string RootPath => _root;
    public string WebRootPath => _webRoot;
    public string ContentRootPath => _contentRoot;

    /// <summary>پوشه‌ی واقعی ذخیره‌ی مدارک آرشیو روی دیسک سرور.</summary>
    public string DocArchiveRootPath => _docArchiveRoot ?? Path.Combine(_root, DocArchiveModule);

    /// <summary>آیا مدارک آرشیو بیرون از پوشه‌ی برنامه (مسیر پیکربندی‌شده) ذخیره می‌شوند؟</summary>
    public bool HasExternalDocArchiveRoot => _docArchiveRoot is not null;

    /// <summary>
    /// مسیر پیکربندی‌شده را مطلق می‌کند. مسیر نسبی نسبت به پوشه‌ی برنامه حل می‌شود؛
    /// متغیرهای محیطی ویندوز (مثل %ProgramData%) هم پشتیبانی می‌شوند.
    /// </summary>
    internal static string? ResolveDocArchiveRoot(string? configured, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
        var full = Path.IsPathRooted(expanded) ? expanded : Path.Combine(contentRoot, expanded);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(full));
    }

    /// <summary>
    /// تست نوشتن روی پوشه‌ی مدارک آرشیو — برای لاگ راه‌اندازی. روی IIS اگر هویت Application Pool
    /// اجازه‌ی Modify روی این پوشه را نداشته باشد، همین‌جا مشخص می‌شود نه وقتی کاربر اولین فایل را می‌فرستد.
    /// </summary>
    public (bool Ok, string Path, string? Error) ProbeDocArchiveWrite()
    {
        var dir = DocArchiveRootPath;
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return (true, dir, null);
        }
        catch (Exception ex)
        {
            return (false, dir, ex.Message);
        }
    }

    /// <summary>
    /// ساخت امن مسیر ماژول با حفظ ساختار زيرپوشه‌ها (مانند office/innerletter یا hr/employee/documents)
    /// و جلوگیری از Path Traversal.
    /// </summary>
    private static string SafeModule(string m)
    {
        if (string.IsNullOrWhiteSpace(m)) return "misc";
        var parts = m.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new List<string>();
        foreach (var part in parts)
        {
            if (part == "." || part == "..") continue;
            var sb = new System.Text.StringBuilder();
            foreach (var ch in part)
                if (char.IsLetterOrDigit(ch) || ch is '-' or '_') sb.Append(ch);
            if (sb.Length > 0) cleanParts.Add(sb.ToString());
        }
        return cleanParts.Count > 0 ? string.Join('/', cleanParts) : "misc";
    }

    /// <summary>
    /// کاراکترهایی که ویندوز در نام فایل نمی‌پذیرد. عمداً مستقل از سیستم‌عامل است تا فایلی که روی
    /// لینوکس/محیط توسعه ذخیره شده، بعد از انتقال پوشه به سرور ویندوز/IIS هم قابل باز شدن باشد.
    /// </summary>
    private static readonly HashSet<char> InvalidNameChars =
        new(Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));

    public static string SanitizeName(string? name)
    {
        var raw = (name ?? "").Replace('\\', '/');
        raw = raw[(raw.LastIndexOf('/') + 1)..];

        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
            sb.Append(InvalidNameChars.Contains(ch) || char.IsControl(ch) ? '_' : ch);

        // ویندوز نقطه/فاصله‌ی انتهای نام را بی‌صدا حذف می‌کند → مسیر ثبت‌شده با فایل واقعی یکی نمی‌ماند
        var s = sb.ToString().Trim().TrimEnd('.', ' ');
        // «..» در نام فایل مجاز است ولی مسیرخوان آن را نشانه‌ی Path Traversal می‌داند؛ یکی‌اش می‌کنیم
        while (s.Contains("..", StringComparison.Ordinal)) s = s.Replace("..", ".", StringComparison.Ordinal);
        if (s.Length == 0 || s == ".") s = "file";

        // کوتاه‌سازی بدون از دست دادن پسوند (پیش‌نمایش/نوع فایل به پسوند وابسته است)
        const int max = 80;
        if (s.Length > max)
        {
            var ext = Path.GetExtension(s);
            if (ext.Length is 0 or > 16) s = s[..max];
            else s = s[..(max - ext.Length)].TrimEnd('.', ' ') + ext;
        }
        return s;
    }

    private static string StorageModule(string module)
    {
        var key = (module ?? "").Trim();
        return key switch
        {
            "InnerLetters" => "office/innerletter",
            "IncomingLetters" => "office/incomingletter",
            "OutgoingLetters" => "office/outgoingletter",
            "OtoEmails" => "office/email",
            "Email" => "office/email",
            _ => key
        };
    }

    private bool IsDocArchive(string module) =>
        _docArchiveRoot is not null && string.Equals(SafeModule(StorageModule(module)), DocArchiveModule, StringComparison.OrdinalIgnoreCase);

    private string BuildDirectory(string module, int refId, string? subFolder = null)
    {
        // مدارک آرشیو: {StorageRoot}/{versionId}/… — مسیر نسبی همچنان «DocVersion/{versionId}/…» ثبت می‌شود
        var parts = IsDocArchive(module)
            ? new List<string> { _docArchiveRoot! }
            : new List<string> { _root, SafeModule(StorageModule(module)) };
        if (!string.IsNullOrWhiteSpace(subFolder))
            parts.Add(SafeModule(subFolder));
        parts.Add(refId.ToString());
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// ذخیره فایل جدید زیر wwwroot/uploads/{module}/[subFolder/]{refId}/{guid}_{originalName}
    /// </summary>
    public async Task<string> SaveAsync(string module, int refId, Stream stream, string originalName, string? subFolder = null)
    {
        var safe = SanitizeName(originalName);
        var dir = BuildDirectory(module, refId, subFolder);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}_{safe}");
        // نوشتن اتمیک: اول فایل موقت، بعد تغییر نام. اگر ارتباط وسط آپلود (مثلاً ورود انبوه) قطع شود
        // یا دیسک پر باشد، فایل نیمه‌کاره با نام نهایی روی سرور نمی‌ماند.
        var temp = file + ".part";
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.CopyToAsync(fs);
                await fs.FlushAsync();
            }
            File.Move(temp, file);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        return ToRelative(file);
    }

    /// <summary>
    /// جایگزینی اتمیک یک فایل با نام ثابت زیر wwwroot/uploads/{module}/{refId}.
    /// برای فایل‌هایی مثل امضای کاربر که یک فایل مشخص به ازای هر شناسه دارند.
    /// </summary>
    public async Task<string> SaveNamedAsync(string module, int refId, Stream stream, string fileName, string? subFolder = null)
    {
        var safe = SanitizeName(fileName);
        var dir = BuildDirectory(module, refId, subFolder);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, safe);
        var temp = file + $".{Guid.NewGuid():N}.part";
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.CopyToAsync(fs);
                await fs.FlushAsync();
            }
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        return ToRelative(file);
    }

    /// <summary>
    /// ذخیره بایت‌ها یا عکس خام زیر wwwroot/uploads/{module}/{ownerId|common}/{guid}.{ext}
    /// </summary>
    public async Task<string> SaveRawAsync(string module, byte[] data, string ext, int? ownerId = null)
    {
        var dir = IsDocArchive(module)
            ? Path.Combine(_docArchiveRoot!, ownerId?.ToString() ?? "common")
            : Path.Combine(_root, SafeModule(StorageModule(module)), ownerId?.ToString() ?? "common");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}{(ext.StartsWith('.') ? ext : "." + ext)}");
        await File.WriteAllBytesAsync(file, data);
        return ToRelative(file);
    }

    /// <summary>
    /// انتقال فایل به پوشه/مسیر جدید زیر wwwroot/uploads
    /// </summary>
    public string? Move(string? relativePath, string module, int refId, string? subFolder = null)
    {
        var full = ToFull(relativePath);
        if (full is null || !File.Exists(full)) return null;

        var dir = BuildDirectory(module, refId, subFolder);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(full));

        if (Path.GetFullPath(dest) == Path.GetFullPath(full)) return ToRelative(dest);

        try
        {
            File.Move(full, dest, true);
        }
        catch
        {
            return null;
        }

        return ToRelative(dest);
    }

    public byte[]? ReadBytes(string? relativePath)
    {
        var full = ToFull(relativePath);
        if (full is null || !File.Exists(full)) return null;
        try { return File.ReadAllBytes(full); }
        catch { return null; }
    }

    public long Size(string? relativePath)
    {
        var full = ToFull(relativePath);
        return full is not null && File.Exists(full) ? new FileInfo(full).Length : 0;
    }

    public void Delete(string? relativePath)
    {
        var full = ToFull(relativePath);
        if (full is not null && File.Exists(full))
        {
            try { File.Delete(full); } catch { }
        }
    }

    /// <summary>
    /// تبدیل مسیر نسبی به مسیر کامل دیسک با پشتیبانی از سازگاری عقب‌رو (Fallback) برای فایل‌های قدیمی و نام‌های مستعار
    /// </summary>
    public string? ToFull(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        // فقط بخش «..» (بالا رفتن از پوشه) ممنوع است؛ نامی مثل «نامه..pdf» مجاز است
        if (clean.Split('/').Any(seg => seg == "..")) return null;

        // نرمال‌سازی prefix uploads/
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        // لیست جفت‌مسیرهای معادل جهت پشتیبانی کامل از فایل‌های قبلی دیتابیس
        var aliases = new List<string> { uploadClean, clean };
        // مسیرهای قدیمی ماژول‌های اتوماسیون را با مسیر استاندارد office تطبیق می‌دهیم.
        var officeAliases = new[]
        {
            (Old: "InnerLetters/", New: "office/innerletter/"),
            (Old: "IncomingLetters/", New: "office/incomingletter/"),
            (Old: "OutgoingLetters/", New: "office/outgoingletter/"),
            (Old: "OtoEmails/", New: "office/email/")
        };
        foreach (var a in officeAliases)
        {
            if (uploadClean.StartsWith(a.Old, StringComparison.OrdinalIgnoreCase)) aliases.Add(a.New + uploadClean[a.Old.Length..]);
            if (uploadClean.StartsWith(a.New, StringComparison.OrdinalIgnoreCase)) aliases.Add(a.Old + uploadClean[a.New.Length..]);
        }
        if (uploadClean.StartsWith("فایل های صادره/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("office/outgoingletter/" + uploadClean["فایل های صادره/".Length..]);
        else if (uploadClean.StartsWith("office/outgoingletter/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("فایل های صادره/" + uploadClean["office/outgoingletter/".Length..]);

        if (uploadClean.StartsWith("فایل های ایمیل/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("office/email/" + uploadClean["فایل های ایمیل/".Length..]);
        else if (uploadClean.StartsWith("office/email/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("فایل های ایمیل/" + uploadClean["office/email/".Length..]);

        if (uploadClean.StartsWith("innerletter/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("office/innerletter/" + uploadClean["innerletter/".Length..]);
        else if (uploadClean.StartsWith("office/innerletter/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("innerletter/" + uploadClean["office/innerletter/".Length..]);

        if (uploadClean.StartsWith("work-orders/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("itassets/workorders/" + uploadClean["work-orders/".Length..]);
        else if (uploadClean.StartsWith("itassets/workorders/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("work-orders/" + uploadClean["itassets/workorders/".Length..]);

        if (uploadClean.StartsWith("it-requests/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("itassets/requests/" + uploadClean["it-requests/".Length..]);
        else if (uploadClean.StartsWith("itassets/requests/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("it-requests/" + uploadClean["itassets/requests/".Length..]);

        if (uploadClean.StartsWith("SecureFiles/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("projects/" + uploadClean["SecureFiles/".Length..]);
        else if (uploadClean.StartsWith("projects/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("SecureFiles/" + uploadClean["projects/".Length..]);

        var rootFull = Path.GetFullPath(_root);
        var webRootFull = Path.GetFullPath(_webRoot);
        var contentFull = Path.GetFullPath(_contentRoot);

        // مدارک آرشیو: اول پوشه‌ی پیکربندی‌شده‌ی بیرون از برنامه، بعد مسیرهای قدیمی (سازگاری با فایل‌های قبلی)
        string? docDefault = null;
        var docPrefix = DocArchiveModule + "/";
        if (_docArchiveRoot is not null && uploadClean.StartsWith(docPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var p0 = Path.GetFullPath(Path.Combine(_docArchiveRoot, uploadClean[docPrefix.Length..]));
            if (IsUnder(p0, _docArchiveRoot))
            {
                if (File.Exists(p0)) return p0;
                docDefault = p0;
            }
        }

        foreach (var candidate in aliases.Distinct())
        {
            var p1 = Path.GetFullPath(Path.Combine(_root, candidate));
            if (IsUnder(p1, rootFull) && File.Exists(p1)) return p1;

            var p2 = Path.GetFullPath(Path.Combine(_webRoot, candidate));
            if (IsUnder(p2, webRootFull) && File.Exists(p2)) return p2;

            var p3 = Path.GetFullPath(Path.Combine(_contentRoot, candidate));
            if (IsUnder(p3, contentFull) && File.Exists(p3)) return p3;
        }

        if (docDefault is not null) return docDefault;

        // در صورت نبود فیزیکی، مسیر پیش‌فرض در uploads بازگردانده می‌شود
        var defaultPath = Path.GetFullPath(Path.Combine(_root, uploadClean));
        return IsUnder(defaultPath, rootFull) ? defaultPath : null;
    }

    /// <summary>
    /// آیا path داخل root است؟ مقایسه با جداکننده‌ی انتهایی (تا «uploads2» داخل «uploads» حساب نشود)
    /// و روی ویندوز بدون حساسیت به حروف کوچک/بزرگ (NTFS این‌طور است).
    /// </summary>
    private static bool IsUnder(string path, string root)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(r, cmp) || path.StartsWith(r + Path.DirectorySeparatorChar, cmp);
    }

    public string ToRelative(string fullPath)
    {
        // فایل‌های مدارک در پوشه‌ی بیرونی هم با همان قالب قدیمی «DocVersion/…» در دیتابیس ثبت می‌شوند
        if (_docArchiveRoot is not null && IsUnder(Path.GetFullPath(fullPath), _docArchiveRoot))
            return DocArchiveModule + "/" + Path.GetRelativePath(_docArchiveRoot, fullPath).Replace('\\', '/');
        return Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
    }

    // ==================== روش‌های پشتیبانی و همگام‌سازی پوشه‌های قدیمی ====================

    public async Task<string> SaveWebRootAsync(string folder, int refId, Stream stream, string originalName)
    {
        return await SaveAsync(folder, refId, stream, originalName);
    }

    public byte[]? ReadWebRoot(string? relativePath) => ReadBytes(relativePath);
    public void DeleteWebRoot(string? relativePath) => Delete(relativePath);
    public long SizeWebRoot(string? relativePath) => Size(relativePath);
}
