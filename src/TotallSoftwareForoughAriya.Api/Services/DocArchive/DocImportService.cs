using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.DocArchive;

public record DocImportPreviewItem(string RelativePath, long SizeBytes);

public record DocImportPreviewProblem(
    string RelativePath, string FileName, string Status, string? ErrorCode, string MessageFa);

public record DocImportPreviewSummary(
    int BatchId, int Total, int Ready, int Failed, int Skipped, int Duplicate,
    List<DocImportPreviewProblem> Problems);

public record DocImportFileOutcome(string Status, bool Duplicate, string MessageFa);

public interface IDocImportService
{
    DocImportConfigDto GetConfig();
    Task<DocImportBatchDto> CreateBatchAsync(DocImportBatchCreateDto dto, int userId, string userName);
    Task<DocImportPreviewSummary> PreviewAsync(
        int batchId, List<DocImportPreviewItem> items, int userId, string userName);
    Task<DocImportFileOutcome> ImportFileAsync(
        int batchId, string relativePath, Stream content, long length, int userId, string userName);
    Task<DocImportBatchDto> FinishAsync(int batchId);
    Task<List<DocImportBatchDto>> ListBatchesAsync(int take);
    Task<byte[]> BuildReportAsync(int batchId);
}

/// <summary>
/// ورود انبوه، نسخهٔ ساده: پیش‌نمایش نام‌ها (بدون نوشتن سند)، سپس ورود فایل‌به‌فایل.
/// هر فایل یک تراکنش جدا دارد و خطای یک فایل بقیه را نمی‌بندد.
/// </summary>
public class DocImportService(
    AppDbContext db,
    IOptions<DocImportOptions> options,
    FileStore fileStore,
    ILogger<DocImportService> log) : IDocImportService
{
    private readonly DocImportOptions _opt = options.Value;

    public DocImportConfigDto GetConfig() => new()
    {
        Enabled = _opt.Enabled,
        MaxFileBytes = _opt.MaxFileBytes,
        CreateMissingFolders = _opt.CreateMissingFolders
    };

    // ============================ ۱) ساخت نوبت ============================

    public async Task<DocImportBatchDto> CreateBatchAsync(DocImportBatchCreateDto dto, int userId, string userName)
    {
        if (!_opt.Enabled)
            throw new InvalidOperationException("ورود انبوه روی سرور خاموش است (متغیر محیطی DocArchiveImport__Enabled=false). آن را حذف کنید تا روشن شود.");

        if (dto.RootFolderId is int rootFolderId &&
            !await db.DocFolders.AnyAsync(f => f.Id == rootFolderId && f.IsActive))
            throw new InvalidOperationException("پوشهٔ مقصد انتخاب‌شده وجود ندارد.");

        var batch = new DocImportBatch
        {
            CreatedByUserId = userId,
            CreatedByName = userName,
            RootFolderId = dto.RootFolderId,
            CreateMissingFolders = dto.CreateMissingFolders
        };
        db.DocImportBatches.Add(batch);
        await db.SaveChangesAsync();
        return ToDto(batch);
    }

    // ============================ ۲) پیش‌نمایش نام‌ها ============================

    /// <summary>
    /// تحلیل نام همهٔ فایل‌ها بدون نوشتن هیچ سندی. نتیجه در جدول نتایج ذخیره می‌شود
    /// تا گزارش اکسل از همان‌جا ساخته شود. اجرای دوباره، ردیف‌های «در انتظار» قبلی را
    /// جایگزین می‌کند و به نتایج قطعی دست نمی‌زند.
    /// </summary>
    public async Task<DocImportPreviewSummary> PreviewAsync(
        int batchId, List<DocImportPreviewItem> items, int userId, string userName)
    {
        var batch = await db.DocImportBatches.FirstOrDefaultAsync(b => b.Id == batchId)
                    ?? throw new InvalidOperationException("نوبت ورود پیدا نشد.");

        // آنچه قبلاً در این نوبت ثبت شده — چه در انتظار، چه قطعی.
        // پیش‌نمایش دسته‌ای است (هزار فایل در هر درخواست) و ممکن است همان فایل
        // در دو دسته تکرار شود یا کاربر پوشه را دوباره انتخاب کند؛ بدون این
        // بررسی، کلید یکتای (BatchId, RelativePath) می‌شکند.
        var previous = await db.DocImportResults
            .Where(r => r.BatchId == batchId)
            .Select(r => new { r.RelativePath, r.Status })
            .ToListAsync();
        var seenPaths = previous.Select(p => p.RelativePath).ToHashSet(StringComparer.Ordinal);

        var existingCodes = await db.Documents
            .Where(d => d.Code != null)
            .Select(d => d.Code!)
            .ToListAsync();
        var codeTaken = new HashSet<string>(existingCodes, StringComparer.Ordinal);

        // ورود انبوه به سوییچ کلی شماره‌گذار گره نمی‌خورد: فایلِ بدون شماره یا با
        // شمارهٔ ناسازگار با الگو هرگز ناموفق نمی‌شود و هنگام ورود شمارهٔ خودکار می‌گیرد.
        // (ردیف Id=1 هنگام استارتاپ همیشه وجود دارد؛ اگر نباشد، مسیر قدیمیِ خطا حفظ می‌شود.)
        var autoSettings = await DocAutoCode.RowAsync(db);

        var seenInFolder = new Dictionary<string, string>(StringComparer.Ordinal);

        var ready = 0; var failed = 0; var skipped = 0; var duplicate = 0;
        var problems = new List<DocImportPreviewProblem>();
        var rows = new List<DocImportResult>();
        var newFolders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (seenPaths.Contains(item.RelativePath))
            {
                // ردیف قبلی «در انتظار» بوده و دوباره آمده: شماره‌اش را رزرو نگه می‌داریم
                var prevRow = await db.DocImportResults
                    .Where(r => r.BatchId == batchId && r.RelativePath == item.RelativePath)
                    .Select(r => new { r.Status, r.ParsedCode })
                    .FirstOrDefaultAsync();
                if (prevRow is { Status: DocImportResultStatus.Pending, ParsedCode: { Length: > 0 } pc })
                    codeTaken.Add(pc);
                continue;
            }

            var row = Classify(item.RelativePath, item.SizeBytes, codeTaken, seenInFolder, newFolders, autoSettings);
            // Classify فقط محتوای ردیف را می‌سازد؛ اینکه ردیف به کدام نوبت تعلق دارد
            // وظیفهٔ اینجاست. بدون این، BatchId صفر می‌ماند و کلید یکتای
            // (BatchId, RelativePath) پیش‌نمایش دوم را می‌شکند.
            row.BatchId = batchId;
            seenPaths.Add(row.RelativePath);
            rows.Add(row);

            switch (row.Status)
            {
                case DocImportResultStatus.Pending: ready++; break;
                case DocImportResultStatus.Skipped: skipped++; break;
                case DocImportResultStatus.Duplicate: duplicate++; break;
                default: failed++; break;
            }

            if (row.Status != DocImportResultStatus.Pending && problems.Count < 500)
            {
                problems.Add(new DocImportPreviewProblem(
                    row.RelativePath, row.FileName, row.Status,
                    row.ErrorCode, row.MessageFa ?? ""));
            }
        }

        if (rows.Count > 0)
        {
            db.DocImportResults.AddRange(rows);
            await db.SaveChangesAsync();
        }

        // پوشه‌های لازم پیش از ورود ساخته می‌شوند تا حین آپلود، ساخت پوشه کار را کند نکند
        if (newFolders.Count > 0)
        {
            var mapper = new DocImportFolderMapper(db);
            var created = 0;
            foreach (var f in newFolders.OrderBy(x => x, StringComparer.Ordinal))
            {
                var (folderId, error, isNew) = await mapper.ResolveAsync(
                    f, batch.RootFolderId, batch.CreateMissingFolders, dryRun: false, userId, userName);
                if (folderId == null) continue;
                if (isNew) created++;
            }
            batch.CreatedFolderCount += created;
        }

        // شمارش نهایی از خود جدول خوانده می‌شود، نه از متغیرهای این اجرا —
        // وگرنه در پیش‌نمایشِ دسته‌ای، دستهٔ آخر فقط شمار خودش را برمی‌گرداند.
        var byStatus = await db.DocImportResults
            .Where(r => r.BatchId == batchId)
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        int CountOf(string status) => byStatus.FirstOrDefault(x => x.Key == status)?.Count ?? 0;

        var total = byStatus.Sum(x => x.Count);
        batch.TotalFiles = total;
        await db.SaveChangesAsync();

        return new DocImportPreviewSummary(
            batchId,
            total,
            CountOf(DocImportResultStatus.Pending),
            CountOf(DocImportResultStatus.Failed),
            CountOf(DocImportResultStatus.Skipped),
            CountOf(DocImportResultStatus.Duplicate),
            problems);
    }

    /// <summary>بررسی یک نام فایل. هیچ نوشتنی روی سند انجام نمی‌دهد.</summary>
    private DocImportResult Classify(
        string relativePath, long sizeBytes,
        HashSet<string> codeTaken,
        Dictionary<string, string> seenInFolder,
        HashSet<string> newFolders,
        DocCodeSettings? autoSettings)
    {
        var row = new DocImportResult
        {
            RelativePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            SizeBytes = sizeBytes
        };

        if (DocImportNameParser.IsSystemFile(row.FileName))
        {
            // مسیر «Skipped» در Fail() پیام را پر نمی‌کند، پس صریح نوشته می‌شود
            row.Status = DocImportResultStatus.Skipped;
            row.ErrorCode = DocImportErrorCodes.InfoSystemFile;
            row.MessageFa = DocImportErrorCodes.MessageFa(DocImportErrorCodes.InfoSystemFile);
            return row;
        }

        if (row.FileName.Length > DocImportNameParser.MaxFileNameLength)
            return Fail(row, DocImportErrorCodes.FileNameLong);

        var baseName = DocImportNameParser.StripExtensions(row.FileName);
        var (nameStatus, title, code) = DocImportNameParser.Parse(baseName);
        row.ParsedTitle = title.Length > 250 ? title[..250] : title;
        row.ParsedCode = code.Length > 80 ? code[..80] : code;

        // وقتی شماره‌گذار خودکار فعال است: شمارهٔ ناموجود/ناسازگار با الگو مانع ورود نمی‌شود.
        // ردیف «در انتظار» می‌ماند (ParsedCode خالی) و شماره هنگام ورود تخصیص می‌یابد.
        // چون شماره هنوز معلوم نیست، در رزرو شماره‌ها (codeTaken/seenInFolder) ثبت نمی‌شود.
        DocImportResult AutoAssign(string docTitle)
        {
            if (docTitle.Length == 0) return Fail(row, DocImportErrorCodes.TitleEmpty);
            if (docTitle.Length > DocImportNameParser.MaxTitleLength) return Fail(row, DocImportErrorCodes.TitleLong);
            if (sizeBytes == 0) return Fail(row, DocImportErrorCodes.FileEmpty);
            if (sizeBytes > _opt.MaxFileBytes) return Fail(row, DocImportErrorCodes.FileTooLarge);

            row.ParsedTitle = Trim(docTitle, 250);
            row.ParsedCode = null;
            row.Status = DocImportResultStatus.Pending;
            row.ErrorCode = DocImportErrorCodes.AutoCode;
            row.MessageFa = DocImportErrorCodes.MessageFa(DocImportErrorCodes.AutoCode);

            var folder = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
            if (folder.Length > 0) newFolders.Add(folder);
            return row;
        }

        // عنوان قابل حدس زدن نیست — این یکی حتی با شماره‌گذار خودکار هم ناموفق می‌ماند
        if (nameStatus == "EMPTY_PART" && title.Length == 0)
            return Fail(row, DocImportErrorCodes.TitleEmpty);

        if (autoSettings != null)
        {
            switch (nameStatus)
            {
                // جداکننده ندارد یا بیش از یکی دارد: کل نام فایل عنوان می‌شود و شماره خودکار می‌گیرد
                case "NO_SEPARATOR":
                case "MULTI_SEPARATOR":
                    return AutoAssign(DocImportNameParser.NormalizeTitle(baseName));
                // عنوان دارد ولی شماره ندارد: شماره خودکار می‌گیرد، ورود متوقف نمی‌شود
                case "EMPTY_PART":
                    return AutoAssign(title);
            }
        }
        else
        {
            switch (nameStatus)
            {
                case "NO_SEPARATOR": return Fail(row, DocImportErrorCodes.NameNoSeparator);
                case "MULTI_SEPARATOR": return Fail(row, DocImportErrorCodes.NameMultiSeparator);
                case "EMPTY_PART": return Fail(row, DocImportErrorCodes.CodeEmpty);
            }
        }

        if (title.Length > DocImportNameParser.MaxTitleLength) return Fail(row, DocImportErrorCodes.TitleLong);

        // شمارهٔ نامعتبر (خیلی بلند/حروف غیرمجاز) یا سازگارنیامده با الگوی شماره‌گذار:
        // به‌جای توقف ورود، شمارهٔ خودکار می‌گیرد.
        var codeInvalid = code.Length > DocImportNameParser.MaxCodeLength
                          || DocImportNameParser.CodeCharsError(code) != null;
        if (autoSettings != null && (codeInvalid || !DocAutoCode.MatchesFormula(code, autoSettings.Prefix)))
            return AutoAssign(title);

        if (code.Length > DocImportNameParser.MaxCodeLength) return Fail(row, DocImportErrorCodes.CodeLong);
        if (DocImportNameParser.CodeCharsError(code) is { } charsError) return Fail(row, charsError);
        if (sizeBytes == 0) return Fail(row, DocImportErrorCodes.FileEmpty);
        if (sizeBytes > _opt.MaxFileBytes) return Fail(row, DocImportErrorCodes.FileTooLarge);

        if (seenInFolder.TryGetValue(code, out var other))
            return Fail(row, DocImportErrorCodes.CodeDupFile);
        seenInFolder[code] = relativePath;

        if (codeTaken.Contains(code))
            return Fail(row, DocImportErrorCodes.CodeDupDb, DocImportResultStatus.Duplicate);
        codeTaken.Add(code);

        var folder = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        if (folder.Length > 0) newFolders.Add(folder);

        row.Status = DocImportResultStatus.Pending;
        return row;
    }

    private static DocImportResult Fail(
        DocImportResult row, string code, string status = DocImportResultStatus.Failed)
    {
        row.Status = status;
        row.ErrorCode = code;
        row.MessageFa = DocImportErrorCodes.MessageFa(code);
        return row;
    }

    // ============================ ۳) ورود یک فایل ============================

    /// <summary>
    /// یک فایل را می‌گیرد و در همان درخواست وارد آرشیو می‌کند:
    /// سند + نسخهٔ ۱ تأییدشده + پیوست + لاگ، همه در یک تراکنش.
    /// </summary>
    public async Task<DocImportFileOutcome> ImportFileAsync(
        int batchId, string relativePath, Stream content, long length, int userId, string userName)
    {
        var batch = await db.DocImportBatches.FirstOrDefaultAsync(b => b.Id == batchId)
                    ?? throw new InvalidOperationException("نوبت ورود پیدا نشد.");

        var cleaned = CleanRelativePath(relativePath)
                      ?? throw new InvalidOperationException(DocImportErrorCodes.MessageFa(DocImportErrorCodes.PathInvalid));

        var row = await db.DocImportResults
            .FirstOrDefaultAsync(r => r.BatchId == batchId && r.RelativePath == cleaned);

        if (row == null)
        {
            // فایلی که پیش‌نمایش نشده (مثلاً بعد از پیش‌نمایش اضافه شده) — همان‌جا بررسی می‌شود
            var existing = await db.Documents.Where(d => d.Code != null).Select(d => d.Code!).ToListAsync();
            row = Classify(cleaned, length, new HashSet<string>(existing, StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
                await DocAutoCode.RowAsync(db));
            row.BatchId = batchId;
            db.DocImportResults.Add(row);
            await db.SaveChangesAsync();
        }

        if (row.Status == DocImportResultStatus.Imported)
            return new DocImportFileOutcome(row.Status, true, "این فایل قبلاً در همین نوبت وارد شده است.");

        if (row.Status is DocImportResultStatus.Failed or DocImportResultStatus.Skipped
            or DocImportResultStatus.Duplicate)
        {
            return new DocImportFileOutcome(row.Status, row.Status == DocImportResultStatus.Duplicate,
                row.MessageFa ?? "");
        }

        if (length > _opt.MaxFileBytes)
            throw new InvalidOperationException(
                $"حجم فایل از سقف مجاز ({_opt.MaxFileBytes / 1048576} مگابایت) بیشتر است.");

        var title = (row.ParsedTitle ?? "").Trim();
        var code = (row.ParsedCode ?? "").Trim();
        if (title.Length == 0)
            throw new InvalidOperationException(DocImportErrorCodes.MessageFa(DocImportErrorCodes.TitleEmpty));

        // ردیف «شمارهٔ خودکار»: شماره در تراکنش ورود از شماره‌گذار تخصیص می‌یابد.
        // ورود انبوه از سوییچ Enabled تبعیت نمی‌کند؛ فقط وجود ردیف تنظیمات (که خودتعمیر اسکیما
        // همیشه می‌سازد) کافی است. نبود ردیف یعنی اسکیما خیلی قدیمی است — همان خطای قبلی.
        var autoAssign = code.Length == 0;
        if (autoAssign && !await db.DocCodeSettings.AsNoTracking().AnyAsync(s => s.Id == 1))
            throw new InvalidOperationException(DocImportErrorCodes.MessageFa(DocImportErrorCodes.CodeEmpty));

        var mapper = new DocImportFolderMapper(db);
        var folder = Path.GetDirectoryName(cleaned)?.Replace('\\', '/') ?? "";
        var (folderId, folderError, _) = await mapper.ResolveAsync(
            folder, batch.RootFolderId, batch.CreateMissingFolders, dryRun: false, userId, userName);
        if (folderId == null)
            throw new InvalidOperationException(DocImportErrorCodes.MessageFa(
                folderError ?? DocImportErrorCodes.FolderNotFound));
        await db.SaveChangesAsync();

        var outcome = await ImportOneAsync(batch, row, folderId.Value, title, code, content, userId, userName, autoAssign);

        // مسیر خطا در ImportOneAsync با ChangeTracker.Clear() نمونه‌های batch و row را جدا می‌کند؛ بدون بارگذاری دوباره
        // شمارش «ناموفق» نوبت ذخیره نمی‌شد و پاسخ به مرورگر وضعیت «Pending» با پیام خالی داشت («وارد نشد» بدون دلیل).
        if (db.Entry(batch).State == EntityState.Detached)
            batch = await db.DocImportBatches.FirstAsync(b => b.Id == batchId);
        if (db.Entry(row).State == EntityState.Detached)
        {
            var rowId = row.Id;
            row = await db.DocImportResults.FirstAsync(r => r.Id == rowId);
        }

        if (outcome.Imported)
        {
            batch.ImportedCount++;
        }
        else if (row.Status == DocImportResultStatus.Duplicate)
        {
            // شمارش تکراری‌ها در پیش‌نمایش انجام شده؛ اینجا فقط نتیجه ثبت می‌شود
        }
        else
        {
            batch.FailedCount++;
        }
        if (row.Status == DocImportResultStatus.Skipped) batch.SkippedCount++;

        await db.SaveChangesAsync();

        return new DocImportFileOutcome(row.Status, row.Status == DocImportResultStatus.Duplicate,
            row.MessageFa ?? (outcome.Imported ? "وارد شد." : ""));
    }

    /// <summary>ساخت سند کامل در یک تراکنش. خطا فقط همین فایل را ناموفق می‌کند.</summary>
    private async Task<(bool Imported, string? Message)> ImportOneAsync(
        DocImportBatch batch, DocImportResult row, int folderId, string title, string code,
        Stream content, int userId, string userName, bool autoAssign)
    {
        // شمارهٔ خودکار به لحاظ ساختاری یکتاست (از شماره‌گذار اتمیک می‌آید) و نیازی به بررسی تکراری ندارد
        if (!autoAssign && await db.Documents.AnyAsync(d => d.Code == code))
        {
            row.Status = DocImportResultStatus.Duplicate;
            row.ErrorCode = DocImportErrorCodes.CodeDupDb;
            row.MessageFa = DocImportErrorCodes.MessageFa(DocImportErrorCodes.CodeDupDb);
            row.ProcessedAtUtc = DateTime.UtcNow;
            return (false, row.MessageFa);
        }

        // مسیر فایلی که روی دیسک سرور نوشته شد؛ اگر تراکنش برگردد، فایل یتیم نباید بماند
        string? savedRelPath = null;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // تخصیص داخل تراکنش است تا اگر ورود همین فایل ناموفق شود، شماره سوزانده نشود.
            // به سوییچ کلی شماره‌گذار هم وابسته نیست — ورود انبوه هرگز سرِ شماره نمی‌ایستد.
            if (autoAssign)
                code = (await DocAutoCode.AllocateNextCodeAsync(db, allowWhenDisabled: true))
                       ?? throw new InvalidOperationException(DocImportErrorCodes.MessageFa(DocImportErrorCodes.CodeEmpty));

            var doc = new ArchiveDocument
            {
                FolderId = folderId,
                Title = title,
                Code = code,
                AllowMultipleActiveVersions = false,
                IsPublic = false,
                PublicCanDownload = false,
                RequireDownloadConfirm = false,
                WatermarkPreview = false,
                CreatedByUserId = userId,
                CreatedByName = userName,
                IsActive = true
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            db.DocumentPermissions.Add(new DocumentPermission
            {
                DocumentId = doc.Id,
                UserId = userId,
                RoleId = 0,
                Level = DocAccessLevel.Full,
                CanDownload = true
            });

            var version = new DocumentVersion
            {
                DocumentId = doc.Id,
                VersionNo = 1,
                Title = title,
                ChangeNote = "ورود انبوه از پوشهٔ آرشیو",
                Status = DocVersionStatus.Approved,
                IsActive = true,
                ActivateOnApprove = true,
                CreatedByUserId = userId,
                CreatedByName = userName,
                ApprovedAt = DateTime.Now
            };
            db.DocumentVersions.Add(version);
            await db.SaveChangesAsync();

            // همان FileStore سراسری (DI) — تا ورود انبوه دقیقاً در همان پوشه‌ای بنشیند که آپلود دستی
            // و دانلود/پیش‌نمایش از آن می‌خوانند (از جمله DocArchive:StorageRoot روی سرور IIS)
            var relPath = await fileStore.SaveAsync(FileStore.DocArchiveModule, version.Id, content, row.FileName);
            savedRelPath = relPath;

            db.AppAttachments.Add(new AppAttachment
            {
                Module = "DocVersion",
                RefId = version.Id,
                FileName = Trim(row.FileName, 255),
                ContentType = ContentTypeFor(row.FileName),
                FilePath = relPath,
                Data = Array.Empty<byte>(),
                UploaderName = userName,
                UploaderUserId = userId
            });

            db.DocumentLogs.Add(new DocumentLog
            {
                DocumentId = doc.Id,
                VersionId = version.Id,
                Action = "Import",
                Detail = Trim($"batch:{batch.Id} file:{row.FileName}", 600),
                UserId = userId,
                UserName = userName
            });

            row.Status = DocImportResultStatus.Imported;
            row.DocumentId = doc.Id;
            row.ErrorCode = null;
            // برای ردیف‌های خودکار، شمارهٔ تخصیص‌یافته در گزارش دیده می‌شود
            row.ParsedCode = autoAssign ? Trim(code, 80) : row.ParsedCode;
            row.MessageFa = autoAssign ? $"شماره مدرک به‌صورت خودکار «{code}» تخصیص یافت." : null;
            row.ProcessedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ورود فایل {File} در نوبت {Batch} ناموفق بود", row.FileName, batch.Id);

            // تراکنش برگشت خورد؛ فایلی که روی دیسک نوشته شده بود را هم پاک کن تا فضای سرور با فایل بی‌صاحب پر نشود
            if (savedRelPath is not null) fileStore.Delete(savedRelPath);
            if (ex is UnauthorizedAccessException or IOException)
                log.LogError(ex, "نوشتن فایل مدرک روی دیسک سرور ناموفق بود. پوشه: {Dir} — روی IIS به هویت Application Pool اجازهٔ Modify بدهید.",
                    fileStore.DocArchiveRootPath);

            // اگر تغییرات نیمه‌کاره در change tracker مانده باشد، ثبت خطا هم شکست می‌خورد
            db.ChangeTracker.Clear();

            var isDuplicate = ex is DbUpdateException &&
                              ex.Message.Contains("Documents", StringComparison.OrdinalIgnoreCase);

            var tracked = await db.DocImportResults.FirstOrDefaultAsync(r => r.Id == row.Id);
            if (tracked != null)
            {
                if (isDuplicate)
                {
                    tracked.Status = DocImportResultStatus.Duplicate;
                    tracked.ErrorCode = DocImportErrorCodes.CodeDupDb;
                    tracked.MessageFa = DocImportErrorCodes.MessageFa(DocImportErrorCodes.CodeDupDb);
                }
                else
                {
                    tracked.Status = DocImportResultStatus.Failed;
                    tracked.ErrorCode = DocImportErrorCodes.WriteFailed;
                    // پیام خام EF/SQL به کاربر نشان داده نمی‌شود
                    tracked.MessageFa = ex is UnauthorizedAccessException or IOException
                        ? "ذخیرهٔ فایل روی دیسک سرور ناموفق بود؛ مدیر سیستم دسترسی نوشتن پوشهٔ ذخیرهٔ آرشیو را بررسی کند."
                        : DocImportErrorCodes.MessageFa(DocImportErrorCodes.WriteFailed);
                }
                tracked.ProcessedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return (false, tracked.MessageFa);
            }
            return (false, ex.Message);
        }
    }

    // ============================ ۴) پایان و سابقه ============================

    public async Task<DocImportBatchDto> FinishAsync(int batchId)
    {
        var batch = await db.DocImportBatches.FirstOrDefaultAsync(b => b.Id == batchId)
                    ?? throw new InvalidOperationException("نوبت ورود پیدا نشد.");

        if (batch.FinishedAt == null)
        {
            var counts = await db.DocImportResults
                .Where(r => r.BatchId == batchId)
                .GroupBy(r => r.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();

            batch.ImportedCount = counts.FirstOrDefault(c => c.Key == DocImportResultStatus.Imported)?.Count ?? 0;
            batch.FailedCount = counts.FirstOrDefault(c => c.Key == DocImportResultStatus.Failed)?.Count ?? 0;
            batch.SkippedCount = counts.FirstOrDefault(c => c.Key == DocImportResultStatus.Skipped)?.Count ?? 0;
            batch.TotalFiles = await db.DocImportResults.CountAsync(r => r.BatchId == batchId);
            batch.FinishedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }
        return ToDto(batch);
    }

    public async Task<List<DocImportBatchDto>> ListBatchesAsync(int take)
    {
        var list = await db.DocImportBatches
            .OrderByDescending(b => b.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    // ============================ ۵) گزارش اکسل ============================

    public async Task<byte[]> BuildReportAsync(int batchId)
    {
        var batch = await db.DocImportBatches.FirstOrDefaultAsync(b => b.Id == batchId)
                    ?? throw new InvalidOperationException("نوبت ورود پیدا نشد.");

        var rows = await db.DocImportResults
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.Id)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("گزارش ورود");

        string[] headers =
        [
            "ردیف", "مسیر فایل", "نام فایل", "عنوان", "شماره", "وضعیت", "کد خطا", "توضیح", "حجم (بایت)", "شناسهٔ سند"
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;

        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = r - 1;
            Put(ws.Cell(r, 2), row.RelativePath);
            Put(ws.Cell(r, 3), row.FileName);
            Put(ws.Cell(r, 4), row.ParsedTitle ?? "");
            // ستون شماره متنی است تا ۰۰۷ به ۷ تبدیل نشود
            var codeCell = ws.Cell(r, 5);
            codeCell.Style.NumberFormat.Format = "@";
            Put(codeCell, row.ParsedCode ?? "");
            ws.Cell(r, 6).Value = StatusFa(row.Status);
            Put(ws.Cell(r, 7), row.ErrorCode ?? "");
            Put(ws.Cell(r, 8), row.MessageFa ?? "");
            ws.Cell(r, 9).Value = row.SizeBytes;
            ws.Cell(r, 10).Value = row.DocumentId?.ToString() ?? "";
            r++;
        }

        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 10, 60);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();

        // سلول‌هایی که با = + - @ شروع می‌شوند در اکسل فرمول حساب می‌شوند
        static void Put(IXLCell cell, string text)
        {
            var t = text ?? "";
            if (t.Length > 0 && "=+-@".Contains(t[0])) t = "'" + t;
            cell.Value = t;
        }
    }

    private static string StatusFa(string status) => status switch
    {
        DocImportResultStatus.Pending => "در انتظار ورود",
        DocImportResultStatus.Imported => "وارد شد",
        DocImportResultStatus.Duplicate => "تکراری",
        DocImportResultStatus.Failed => "ناموفق",
        DocImportResultStatus.Skipped => "نادیده",
        _ => status
    };

    private static DocImportBatchDto ToDto(DocImportBatch b) => new()
    {
        Id = b.Id,
        StartedAt = b.StartedAt,
        FinishedAt = b.FinishedAt,
        CreatedByName = b.CreatedByName,
        TotalFiles = b.TotalFiles,
        ImportedCount = b.ImportedCount,
        FailedCount = b.FailedCount,
        SkippedCount = b.SkippedCount,
        CreatedFolderCount = b.CreatedFolderCount,
        StatusFa = b.FinishedAt == null ? "در جریان" : "تمام شد"
    };

    // ============================ ابزارها ============================

    /// <summary>
    /// پاک‌سازی مسیر نسبی coming از مرورگر: جداکنندهٔ یکدست، حذف «..»، حذف پیشوند
    /// درایو و کاراکترهای غیرمجاز. اگر مسیر بخواهد از پوشهٔ مقصد بیرون بزند، null.
    /// </summary>
    internal static string? CleanRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var normalized = relativePath.Replace('\\', '/').Trim();

        var colon = normalized.IndexOf(':');
        if (colon >= 0 && colon <= 2) normalized = normalized[(colon + 1)..];
        normalized = normalized.TrimStart('/');

        if (normalized.Length == 0) return null;
        if (normalized.Contains("//", StringComparison.Ordinal)) return null;

        var parts = new List<string>();
        foreach (var raw in normalized.Split('/'))
        {
            var part = raw.Trim();
            if (part.Length == 0 || part == ".") continue;
            if (part == "..") return null;
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            parts.Add(part);
        }

        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".tif" or ".tiff" => "image/tiff",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".html" or ".htm" => "text/html",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".zip" => "application/zip",
        ".rar" => "application/vnd.rar",
        ".7z" => "application/x-7z-compressed",
        ".mp3" => "audio/mpeg",
        ".mp4" => "video/mp4",
        ".wav" => "audio/wav",
        _ => "application/octet-stream"
    };

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
