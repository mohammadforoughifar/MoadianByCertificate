using System.Buffers;
using Inventory.Api.Data;
using Inventory.Api.Entities.Chat;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Chat;

public record ChatFileResult(string Path, string FileName, string ContentType);

/// <summary>ذخیرهٔ خصوصی فایل، اعتبارسنجی مالکیت و دانلود با کنترل عضویت در گفتگو.</summary>
public sealed class ChatAttachmentService
{
    private const long MaxFileBytes = 50L * 1024L * 1024L;
    private const string UrlPrefix = "/api/chat/attachments/";
    private readonly AppDbContext _db;
    private readonly ILogger<ChatAttachmentService> _logger;
    private readonly string _contentRoot;
    private readonly string _webRoot;
    private readonly string _root;
    private readonly string _legacyRoot;
    private readonly string? _configuredChatRoot;
    private readonly string? _configuredLegacyRoot;
    private static readonly FileExtensionContentTypeProvider MimeTypes = new();

    static ChatAttachmentService()
    {
        // پسوندهای رایج ضبط صدا که در فهرست پیش‌فرض provider نیستند یا نوع بهتری دارند.
        MimeTypes.Mappings[".m4a"] = "audio/mp4";
        MimeTypes.Mappings[".3gp"] = "audio/3gpp";
        MimeTypes.Mappings[".oga"] = "audio/ogg";
        MimeTypes.Mappings[".opus"] = "audio/ogg";
    }

    public ChatAttachmentService(AppDbContext db, IWebHostEnvironment env, IConfiguration config, ILogger<ChatAttachmentService> logger)
    {
        _db = db;
        _logger = logger;
        _contentRoot = env.ContentRootPath;
        _webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        // خارج از wwwroot؛ StaticFiles نباید بتواند مجوز چت را دور بزند.
        // برای استقرارهایی که چند نمونه دارند یا پوشهٔ App_Data پاک می‌شود، می‌توان
        // مسیر را با Files:ChatRoot (یا Files__ChatRoot در محیط) به فضای پایدار/اشتراکی برد.
        // رشتهٔ خالی هم مانند نبودِ تنظیم یعنی «پیش‌فرض» تا در appsettings مستندسازی خنثی بماند.
        var chatRoot = config["Files:ChatRoot"];
        var legacyRoot = config["Files:ChatLegacyRoot"];
        _configuredChatRoot = string.IsNullOrWhiteSpace(chatRoot) ? null : chatRoot;
        _configuredLegacyRoot = string.IsNullOrWhiteSpace(legacyRoot) ? null : legacyRoot;
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(chatRoot) ? Path.Combine(_webRoot, "uploads", "chat") : chatRoot!);
        _legacyRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(legacyRoot) ? Path.Combine(_webRoot, "uploads", "chat") : legacyRoot!);
        ProbeRoot();
    }

    private void ProbeRoot()
    {
        try
        {
            Directory.CreateDirectory(_root);
            var probe = Path.Combine(_root, ".probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            _logger.LogInformation(
                "[ChatFiles] ذخیره‌گاه فایل‌های چت '{Root}' آماده است (contentRoot: '{ContentRoot}', پوشهٔ جاری: '{Cwd}').",
                _root, _contentRoot, Environment.CurrentDirectory);
            _logger.LogInformation("[ChatFiles] فایل‌های قدیمی چت از '{LegacyRoot}' خوانده می‌شوند.", _legacyRoot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[ChatFiles] نوشتن در ذخیره‌گاه پیش‌فرض '{Root}' ممکن نیست. مقدار Files:ChatRoot را به پوشهٔ پایدار/اشتراکی تنظیم کنید. contentRoot: '{ContentRoot}', پوشهٔ جاری: '{Cwd}'.",
                _root, _contentRoot, Environment.CurrentDirectory);
        }
    }

    public static string FileUrl(Guid id) => $"{UrlPrefix}{id:D}/download";

    // Normalize the same path aliases a physical/static file provider may accept.
    // A raw StartsWith("/uploads/chat") check misses /uploads//chat on Linux/Windows.
    public static bool IsLegacyPublicPath(string? path)
    {
        var segments = new List<string>();
        foreach (var segment in Uri.UnescapeDataString(path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); continue; }
            segments.Add(segment.TrimEnd(' ', '.'));
        }
        return segments.Count >= 2 && segments[0].Equals("uploads", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("chat", StringComparison.OrdinalIgnoreCase);
    }


    private static bool TryGetId(string? url, out Guid id)
    {
        id = Guid.Empty;
        return url != null && url.StartsWith(UrlPrefix, StringComparison.Ordinal) &&
               url.EndsWith("/download", StringComparison.Ordinal) &&
               Guid.TryParseExact(url[UrlPrefix.Length..^9], "D", out id);
    }

    private string PathFor(Guid id) => Path.Combine(_root, id.ToString("N"));

    /// <summary>
    /// اگر فایل در ذخیره‌گاه فعلی نباشد، چند مسیر جایگزین رایج (نسخه/پوشهٔ کاری قبلی،
    /// استقرار تک‌نمونه با contentRoot متفاوت) هم بررسی می‌شوند تا دانلود پیام‌های قدیمی
    /// بدون انتقال فایل ممکن بماند. هیچ مسیری خارج از این مجموعه پذیرفته نمی‌شود.
    /// </summary>
    private string ResolveExisting(Guid id)
    {
        var primary = PathFor(id);
        if (File.Exists(primary)) return primary;
        foreach (var candidate in NewFileCandidates(id).Skip(1))
            if (File.Exists(candidate))
            {
                _logger.LogWarning("[ChatFiles] فایل '{Id}' در مسیر اصلی نبود؛ از مسیر جایگزین '{Path}' سرو می‌شود.", id, candidate);
                return candidate;
            }
        return primary;
    }

    private IEnumerable<string> NewFileCandidates(Guid id)
    {
        var name = id.ToString("N");
        var roots = new[]
        {
            _root,
            Path.Combine(_contentRoot, "App_Data", "chat"),
            Path.Combine(Environment.CurrentDirectory, "App_Data", "chat")
        };
        return roots.Select(root => Path.Combine(root, name)).Distinct();
    }

    private IEnumerable<string> LegacyFileCandidates(string leaf)
    {
        var roots = new[]
        {
            _legacyRoot,
            Path.Combine(_contentRoot, "wwwroot", "uploads", "chat"),
            Path.Combine(Environment.CurrentDirectory, "wwwroot", "uploads", "chat")
        };
        return roots.Select(root => Path.Combine(root, leaf)).Distinct();
    }

    private async Task RequireAccessAsync(int userId, int? conversationId, CancellationToken ct = default)
    {
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, ct))
            throw new UnauthorizedAccessException("حساب کاربری فعال نیست.");
        if (conversationId.HasValue && !await _db.ChatMembers.AsNoTracking()
                .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId, ct))
            throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");
    }

    public async Task<ChatUploadResultDto> UploadAsync(int userId, int? conversationId, IFormFile file, CancellationToken ct = default)
    {
        if (conversationId is <= 0) throw new ArgumentException("شناسهٔ گفتگو نامعتبر است.");
        await RequireAccessAsync(userId, conversationId, ct);
        if (file.Length <= 0) throw new ArgumentException("فایل خالی قابل ارسال نیست.");
        if (file.Length > MaxFileBytes) throw new ArgumentException("حداکثر حجم هر فایل ۵۰ مگابایت است.");

        var name = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 250)
            throw new ArgumentException("نام فایل نامعتبر یا طولانی است.");

        var attachment = new ChatAttachment
        {
            Id = Guid.NewGuid(), UploadedByUserId = userId, ConversationId = conversationId,
            FileName = name, ContentType = ContentTypeFor(name, file.ContentType), SizeBytes = file.Length
        };
        Directory.CreateDirectory(_root);
        var path = PathFor(attachment.Id);
        var created = false;
        try
        {
            await using (var source = file.OpenReadStream())
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                created = true;
                var buffer = ArrayPool<byte>.Shared.Rent(65536);
                try
                {
                    long length = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(0, 65536), ct)) > 0)
                    {
                        length += read;
                        if (length > MaxFileBytes) throw new ArgumentException("حداکثر حجم هر فایل ۵۰ مگابایت است.");
                        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    if (length != file.Length) throw new IOException("بارگذاری فایل کامل نشد؛ دوباره تلاش کنید.");
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            _db.ChatAttachments.Add(attachment);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            if (created && File.Exists(path)) File.Delete(path);
            _db.Entry(attachment).State = EntityState.Detached;
            throw;
        }
        return new ChatUploadResultDto
        {
            Id = attachment.Id, FileUrl = FileUrl(attachment.Id), FileName = attachment.FileName,
            FileSizeBytes = attachment.SizeBytes, FileContentType = attachment.ContentType,
            MessageType = MessageTypeFor(attachment.ContentType)
        };
    }

    public async Task ApplyToMessageAsync(int userId, SendChatMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileUrl))
        {
            request.FileUrl = null;
            request.FileName = null;
            request.FileSizeBytes = null;
            request.FileContentType = null;
            return;
        }
        if (!TryGetId(request.FileUrl, out var id)) throw new ArgumentException("پیوست معتبر نیست؛ فایل را دوباره بارگذاری کنید.");
        var attachment = await _db.ChatAttachments.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new KeyNotFoundException("پیوست پیدا نشد؛ فایل را دوباره بارگذاری کنید.");
        if (attachment.UploadedByUserId != userId) throw new UnauthorizedAccessException("این فایل توسط شما بارگذاری نشده است.");
        if (attachment.ConversationId.HasValue && attachment.ConversationId != request.ConversationId)
            throw new ArgumentException("این فایل برای گفتگوی دیگری بارگذاری شده است.");
        await RequireAccessAsync(userId, request.ConversationId);
        if (!File.Exists(ResolveExisting(id))) throw new KeyNotFoundException("فایل پیوست روی سرور یافت نشد.");

        // بارگذاری قدیمی بدون conversationId هنگام اولین ارسال به همان گفتگو متصل می‌شود.
        attachment.ConversationId = request.ConversationId;
        request.FileUrl = FileUrl(id);
        request.FileName = attachment.FileName;
        request.FileSizeBytes = attachment.SizeBytes;
        request.FileContentType = attachment.ContentType;
        request.MessageType = MessageTypeFor(attachment.ContentType);
    }

    public async Task<ChatFileResult> GetMessageFileAsync(int userId, int messageId)
    {
        var message = await _db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId && !m.IsDeleted)
            ?? throw new KeyNotFoundException("پیام یا فایل پیدا نشد.");
        await RequireAccessAsync(userId, message.ConversationId);
        if (TryGetId(message.FileUrl, out var id))
        {
            var attachment = await _db.ChatAttachments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.ConversationId == message.ConversationId)
                ?? throw new KeyNotFoundException("پیوست پیدا نشد.");
            return ExistingFile(ResolveExisting(id), attachment.FileName, attachment.ContentType, JoinCandidates(NewFileCandidates(id)));
        }

        // سازگاری با پیام‌های قدیمی، بدون انتقال یا حذف فایل‌های موجود.
        const string legacyPrefix = "/uploads/chat/";
        var url = message.FileUrl ?? "";
        if (!url.StartsWith(legacyPrefix, StringComparison.Ordinal)) throw new KeyNotFoundException("پیوست پیدا نشد.");
        var leaf = Uri.UnescapeDataString(url[legacyPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or ".." || leaf.Contains('/') || leaf.Contains('\\') ||
            leaf.Contains('\0') || Path.IsPathRooted(leaf))
            throw new KeyNotFoundException("مسیر پیوست نامعتبر است.");
        var fileName = string.IsNullOrWhiteSpace(message.FileName) ? leaf : Path.GetFileName(message.FileName.Replace('\\', '/'));
        var path = LegacyFileCandidates(leaf).FirstOrDefault(File.Exists) ?? LegacyFileCandidates(leaf).First();
        if (!File.Exists(path))
            _logger.LogError("[ChatFiles] فایل قدیمی پیام {MessageId} در هیچ‌یک از مسیرها یافت نشد: {Leaf}", message.Id, leaf);
        return ExistingFile(path, fileName, ContentTypeFor(fileName), JoinCandidates(LegacyFileCandidates(leaf)));
    }

    public async Task<ChatFileResult> GetAttachmentAsync(int userId, Guid id)
    {
        var attachment = await _db.ChatAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new KeyNotFoundException("پیوست پیدا نشد.");
        await RequireAccessAsync(userId, attachment.ConversationId);
        if (attachment.UploadedByUserId != userId)
        {
            var url = FileUrl(id);
            if (!attachment.ConversationId.HasValue || !await _db.ChatMessages.AsNoTracking()
                    .AnyAsync(m => m.ConversationId == attachment.ConversationId && m.FileUrl == url && !m.IsDeleted))
                throw new UnauthorizedAccessException("این پیوست هنوز در گفتگو به اشتراک گذاشته نشده است.");
        }
        return ExistingFile(ResolveExisting(id), attachment.FileName, attachment.ContentType, JoinCandidates(NewFileCandidates(id)));
    }

    /// <summary>ابزار تشخیصی (فقط مدیر): مسیرهای مؤثر ذخیره، دسترسی نوشتن، شمارش فایل‌ها و نمونه‌های اخیر.</summary>
    public async Task<object> DiagnoseAsync()
    {
        static bool Hex32(string name) => name.Length == 32 && name.All(char.IsAsciiHexDigit);
        static (bool Exists, bool Writable, int Files) Inspect(string root, bool legacy)
        {
            var exists = Directory.Exists(root);
            var writable = false;
            var files = 0;
            try
            {
                if (!exists) { Directory.CreateDirectory(root); exists = true; writable = true; }
                else
                {
                    var probe = Path.Combine(root, ".probe-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    writable = true;
                }
                files = legacy
                    ? Directory.EnumerateFiles(root).Count()
                    : Directory.EnumerateFiles(root).Count(p => Hex32(Path.GetFileNameWithoutExtension(p)));
            }
            catch { files = -1; }
            return (exists, writable, files);
        }

        var newRoots = new[]
        {
            _root,
            Path.Combine(_contentRoot, "App_Data", "chat"),
            Path.Combine(Environment.CurrentDirectory, "App_Data", "chat")
        }.Select(Path.GetFullPath).Distinct().ToArray();
        var legacyRoots = new[]
        {
            _legacyRoot,
            Path.Combine(_contentRoot, "wwwroot", "uploads", "chat"),
            Path.Combine(Environment.CurrentDirectory, "wwwroot", "uploads", "chat")
        }.Select(Path.GetFullPath).Distinct().ToArray();

        var recentAttachments = await _db.ChatAttachments.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(6).ToListAsync();
        var attachmentRows = recentAttachments.Select(a =>
        {
            var name = a.Id.ToString("N");
            return new
            {
                a.Id, a.FileName, a.SizeBytes, a.CreatedAt,
                roots = newRoots.Select(r => new { path = r, found = File.Exists(Path.Combine(r, name)) }).ToArray()
            };
        }).ToArray();

        var legacyMessages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.FileUrl != null && m.FileUrl.StartsWith("/uploads/chat/") && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt).Take(6)
            .Select(m => new { m.Id, m.FileUrl, m.CreatedAt }).ToListAsync();

        return new
        {
            generatedAt = DateTime.UtcNow,
            contentRoot = _contentRoot,
            workingDirectory = Environment.CurrentDirectory,
            configuredChatRoot = _configuredChatRoot ?? "(تنظیم نشده)",
            effectiveChatRoot = _root,
            configuredChatLegacyRoot = _configuredLegacyRoot ?? "(تنظیم نشده)",
            effectiveChatLegacyRoot = _legacyRoot,
            newRoots = newRoots.Select(r => { var (e, w, f) = Inspect(r, false); return new { path = r, exists = e, writable = w, hexFileCount = f }; }).ToArray(),
            legacyRoots = legacyRoots.Select(r => { var (e, w, f) = Inspect(r, true); return new { path = r, exists = e, writable = w, fileCount = f }; }).ToArray(),
            recentAttachments = attachmentRows,
            recentLegacyMessages = legacyMessages
        };
    }

    private ChatFileResult ExistingFile(string path, string name, string contentType, string? searched = null)
    {
        if (File.Exists(path)) return new ChatFileResult(path, name, contentType);
        _logger.LogError(
            "[ChatFiles] فایل پیوست روی سرور یافت نشد. مسیر: '{Path}' | مسیرهای بررسی‌شده: {Searched} | contentRoot: '{ContentRoot}' | پوشهٔ جاری: '{Cwd}' | Files:ChatRoot: '{ConfigRoot}' | Files:ChatLegacyRoot: '{ConfigLegacyRoot}'",
            path, searched ?? "—", _contentRoot, Environment.CurrentDirectory,
            _configuredChatRoot ?? "(تنظیم نشده)", _configuredLegacyRoot ?? "(تنظیم نشده)");
        throw new KeyNotFoundException("فایل پیوست روی سرور یافت نشد.");
    }

    private static string JoinCandidates(IEnumerable<string> candidates) => string.Join("  ⨯  ", candidates.Distinct());

    private static string ContentTypeFor(string name, string? declaredContentType = null)
    {
        var byExtension = MimeTypes.TryGetContentType(name, out var type) ? type : "application/octet-stream";
        // ضبط پیام صوتی در مرورگر/گوشی ممکن است webm یا mp4 «صوتی» باشد که از روی پسوند video تشخیص داده می‌شود.
        // فقط وقتی نوع اعلامی‌شده audio/* باشد و پسوند از کانتینرهای صوتی/ویدیویی باشد همان نوع صوتی پذیرفته می‌شود؛
        // نوع اعلامی هرگز به‌تنهایی معتبر نمی‌شود تا ارسال HTML با محتوای audio امکان‌پذیر نباشد.
        if (declaredContentType != null &&
            declaredContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) &&
            byExtension is "video/webm" or "video/mp4" or "application/octet-stream" or "application/ogg")
            return declaredContentType;
        return byExtension;
    }

    public static ChatMessageTypeDto MessageTypeFor(string contentType) => contentType switch
    {
        "image/png" or "image/jpeg" or "image/gif" or "image/webp" => ChatMessageTypeDto.Image,
        "audio/mpeg" or "audio/wav" or "audio/x-wav" or "audio/ogg" or "audio/mp4" or "audio/aac"
            or "audio/webm" or "audio/3gpp" => ChatMessageTypeDto.Audio,
        "video/mp4" or "video/webm" or "video/quicktime" => ChatMessageTypeDto.Video,
        _ => ChatMessageTypeDto.File // HTML/SVG and unknown types are downloads, never active inline content.
    };
}
