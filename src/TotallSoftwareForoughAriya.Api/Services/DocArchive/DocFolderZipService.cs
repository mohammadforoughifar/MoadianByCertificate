using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

public interface IDocFolderZipService
{
    Task<(byte[] ZipBytes, string FileName)> ExportZipAsync(
        int? folderId,
        bool includeSubfolders,
        bool onlyActiveVersions,
        bool includeManifest,
        int userId,
        bool isManager);
}

public class DocFolderZipService : IDocFolderZipService
{
    private readonly AppDbContext _db;
    private readonly IDocAccessService _access;
    private readonly FileStore _store;

    public DocFolderZipService(AppDbContext db, IDocAccessService access, FileStore store)
    {
        _db = db;
        _access = access;
        _store = store;
    }

    public async Task<(byte[] ZipBytes, string FileName)> ExportZipAsync(
        int? folderId,
        bool includeSubfolders,
        bool onlyActiveVersions,
        bool includeManifest,
        int userId,
        bool isManager)
    {
        var allFolders = await _db.DocFolders.AsNoTracking()
            .Where(f => f.IsActive)
            .ToListAsync();

        var folderAccessMap = await _access.FolderAccessMapAsync(userId, isManager);

        // تعیین پوشه‌های هدف
        List<DocFolder> targetFolders = new();
        string rootTitle = "آرشیو_کل_اسناد";

        if (folderId.HasValue && folderId.Value > 0)
        {
            var targetRoot = allFolders.FirstOrDefault(f => f.Id == folderId.Value);
            if (targetRoot == null)
                throw new KeyNotFoundException("پوشه مورد نظر در آرشیو یافت نشد.");

            if (!folderAccessMap.TryGetValue(targetRoot.Id, out var access) ||
                access.Level < DocAccessLevel.Read || !access.Download)
            {
                throw new UnauthorizedAccessException("شما اجازه دانلود محتوای این پوشه را ندارید.");
            }

            rootTitle = targetRoot.Name;

            if (includeSubfolders)
            {
                var collected = new List<DocFolder> { targetRoot };
                void CollectChildren(int pid)
                {
                    foreach (var ch in allFolders.Where(f => f.ParentId == pid))
                    {
                        if (folderAccessMap.TryGetValue(ch.Id, out var a) &&
                            a.Level >= DocAccessLevel.Read && a.Download)
                        {
                            collected.Add(ch);
                            CollectChildren(ch.Id);
                        }
                    }
                }
                CollectChildren(targetRoot.Id);
                targetFolders = collected;
            }
            else
            {
                targetFolders = new List<DocFolder> { targetRoot };
            }
        }
        else
        {
            // کل پوشه‌های مجاز
            targetFolders = allFolders
                .Where(f => folderAccessMap.TryGetValue(f.Id, out var a) && a.Level >= DocAccessLevel.Read && a.Download)
                .ToList();
        }

        if (targetFolders.Count == 0)
            throw new UnauthorizedAccessException("برای دریافت ZIP، مجوز جداگانه دانلود روی پوشه لازم است.");

        var targetFolderIds = targetFolders.Select(f => f.Id).ToHashSet();

        // ساخت مسیر کامل متنی هر پوشه
        var folderLookup = allFolders.ToDictionary(f => f.Id);
        var folderPathMap = new Dictionary<int, string>();

        string BuildPath(int fId)
        {
            if (folderPathMap.TryGetValue(fId, out var p)) return p;
            if (!folderLookup.TryGetValue(fId, out var f)) return "";

            var safeName = SanitizePathPart(f.Name);
            if (f.ParentId.HasValue && folderLookup.ContainsKey(f.ParentId.Value) &&
                (folderId == null || fId != folderId.Value))
            {
                var parentPath = BuildPath(f.ParentId.Value);
                p = string.IsNullOrEmpty(parentPath) ? safeName : $"{parentPath}/{safeName}";
            }
            else
            {
                p = safeName;
            }
            folderPathMap[fId] = p;
            return p;
        }

        foreach (var tf in targetFolders)
        {
            BuildPath(tf.Id);
        }

        // بارگذاری مدارک فعال داخل پوشه‌ها
        var rawDocs = await _db.Documents.AsNoTracking()
            .Where(d => targetFolderIds.Contains(d.FolderId) && !d.IsDeleted && d.IsActive)
            .OrderBy(d => d.FolderId).ThenBy(d => d.Code)
            .ToListAsync();

        // محاسبه دسته‌ای؛ دانلود پوشه نباید محدودیت اختصاصی خود مدرک را باز کند.
        var documentAccess = await _access.DocumentAccessMapAsync(userId, isManager, rawDocs.Select(d => d.Id));
        var docs = rawDocs.Where(d => documentAccess.TryGetValue(d.Id, out var grant)
            && grant.Level >= DocAccessLevel.Read && grant.Download).ToList();
        if (rawDocs.Count > 0 && docs.Count == 0)
            throw new UnauthorizedAccessException("برای مدارک این پوشه مجوز دانلود ندارید.");

        var docIds = docs.Select(d => d.Id).ToList();

        // بارگذاری ورژن‌ها
        var versionsQuery = _db.DocumentVersions.AsNoTracking().Where(v => docIds.Contains(v.DocumentId));
        if (onlyActiveVersions)
            versionsQuery = versionsQuery.Where(v => v.IsActive || v.Status == DocVersionStatus.Approved);

        var versions = await versionsQuery.OrderBy(v => v.DocumentId).ThenByDescending(v => v.VersionNo).ToListAsync();
        var versionIds = versions.Select(v => v.Id).ToList();

        // بارگذاری پیوست‌ها
        var attachments = await _db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "DocVersion" && versionIds.Contains(a.RefId))
            .ToListAsync();

        // بارگذاری تگ‌ها
        var tags = await _db.DocumentTags.AsNoTracking()
            .Where(t => docIds.Contains(t.DocumentId))
            .Join(_db.DocTags, t => t.TagId, dt => dt.Id, (t, dt) => new { t.DocumentId, dt.Name, dt.Color })
            .ToListAsync();

        var tagsGroup = tags.GroupBy(t => t.DocumentId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());
        var versionsGroup = versions.GroupBy(v => v.DocumentId).ToDictionary(g => g.Key, g => g.ToList());
        var attachmentsGroup = attachments.GroupBy(a => a.RefId).ToDictionary(g => g.Key, g => g.ToList());

        // ساخت فایل ZIP
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
        {
            var manifestRows = new List<ManifestItem>();
            int seq = 1;

            foreach (var doc in docs)
            {
                var folderPath = folderPathMap.TryGetValue(doc.FolderId, out var fp) ? fp : "General";
                var safeDocCode = SanitizePathPart(doc.Code);
                var safeDocTitle = SanitizePathPart(doc.Title);
                var docDirName = $"{safeDocCode}_{safeDocTitle}";
                if (docDirName.Length > 90) docDirName = docDirName[..90];

                var docVersions = versionsGroup.TryGetValue(doc.Id, out var vl) ? vl : new List<DocumentVersion>();
                if (docVersions.Count == 0 && onlyActiveVersions)
                {
                    // اگر هیچ نسخه فعالی علامت‌گذاری نشده بود، آخرین نسخه موجود را می‌آوریم
                    var anyVer = await _db.DocumentVersions.AsNoTracking()
                        .Where(v => v.DocumentId == doc.Id)
                        .OrderByDescending(v => v.VersionNo)
                        .FirstOrDefaultAsync();
                    if (anyVer != null) docVersions.Add(anyVer);
                }

                int totalDocAttachments = 0;

                foreach (var ver in docVersions)
                {
                    var verAtts = attachmentsGroup.TryGetValue(ver.Id, out var al) ? al : new List<AppAttachment>();
                    totalDocAttachments += verAtts.Count;

                    foreach (var att in verAtts)
                    {
                        var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
                        var safeFileName = SanitizePathPart(att.FileName);
                        var entryFileName = docVersions.Count > 1 ? $"v{ver.VersionNo}_{safeFileName}" : safeFileName;
                        var entryPath = $"{folderPath}/{docDirName}/{entryFileName}";

                        var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        {
                            if (bytes != null && bytes.Length > 0)
                            {
                                await entryStream.WriteAsync(bytes, 0, bytes.Length);
                            }
                            else
                            {
                                var placeholder = Encoding.UTF8.GetBytes($"[فایل پیوست {att.FileName} در سرور یافت نشد]");
                                await entryStream.WriteAsync(placeholder, 0, placeholder.Length);
                            }
                        }
                    }
                }

                var docTagsList = tagsGroup.TryGetValue(doc.Id, out var tlist) ? tlist : new List<string>();

                manifestRows.Add(new ManifestItem
                {
                    Seq = seq++,
                    FolderPath = folderPath,
                    Code = doc.Code,
                    Title = doc.Title,
                    CustomerCode = doc.CustomerCode ?? "—",
                    ActiveVersion = docVersions.FirstOrDefault(v => v.IsActive)?.VersionNo ?? (docVersions.FirstOrDefault()?.VersionNo ?? 1),
                    CreatedAt = PersianDate.ToShort(doc.CreatedAt),
                    ExpireDate = doc.ExpireDate.HasValue ? PersianDate.ToShort(doc.ExpireDate.Value) : "بدون انقضا",
                    IsExpired = doc.ExpireDate.HasValue && doc.ExpireDate.Value.Date < DateTime.Today,
                    Tags = string.Join("، ", docTagsList),
                    AttachmentCount = totalDocAttachments
                });
            }

            // تولید اکسل شناسنامه اسناد
            if (includeManifest && manifestRows.Count > 0)
            {
                var excelBytes = GenerateManifestExcel(rootTitle, manifestRows);
                var excelEntry = zip.CreateEntry("_فهرست_شناسنامه_اسناد.xlsx", CompressionLevel.Optimal);
                using (var es = excelEntry.Open())
                {
                    await es.WriteAsync(excelBytes, 0, excelBytes.Length);
                }

                // راهنمای متنی بایگانی
                var readmeText = GenerateReadmeText(rootTitle, manifestRows.Count, docs.Count, targetFolders.Count);
                var readmeEntry = zip.CreateEntry("_راهنمای_محتوای_بایگانی.txt", CompressionLevel.Optimal);
                using (var rs = readmeEntry.Open())
                {
                    var rb = Encoding.UTF8.GetBytes(readmeText);
                    await rs.WriteAsync(rb, 0, rb.Length);
                }
            }
        }

        var cleanTitle = SanitizePathPart(rootTitle);
        if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = "Archive";
        var (jy, jm, jd) = PersianDate.FromGregorian(DateTime.Now);
        var zipName = $"{cleanTitle}_{jy:0000}{jm:00}{jd:00}_{DateTime.Now:HHmm}.zip";

        return (ms.ToArray(), zipName);
    }

    private static byte[] GenerateManifestExcel(string title, List<ManifestItem> items)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("شناسنامه اسناد آرشیو");
        ws.RightToLeft = true;
        ws.Style.Font.FontName = "Tahoma";
        ws.Style.Font.FontSize = 10;

        // سربرگ عنوان
        ws.Cell(1, 1).Value = $"فهرست و شناسنامه اسناد بایگانی — {title}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");
        ws.Range(1, 1, 1, 10).Merge();
        ws.Row(1).Height = 26;

        ws.Cell(2, 1).Value = $"تاریخ ایجاد بسته ZIP: {PersianDate.ToShort(DateTime.Now)} ساعت {DateTime.Now:HH:mm} | تعداد کل اسناد: {items.Count}";
        ws.Cell(2, 1).Style.Font.FontSize = 9;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");
        ws.Range(2, 1, 2, 10).Merge();

        // عناوین ستون‌ها
        var headers = new[]
        {
            "ردیف", "مسیر پوشه", "کد مدرک", "عنوان سند", "شماره مشتری",
            "نسخه فعال", "تاریخ ثبت", "تاریخ انقضا", "برچسب‌ها (تگ‌ها)", "تعداد پیوست"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#172554");
        }
        ws.Row(4).Height = 24;

        int row = 5;
        foreach (var item in items)
        {
            var isStripe = (row % 2) == 1;
            var bg = isStripe ? XLColor.FromHtml("#f8fafc") : XLColor.White;

            ws.Cell(row, 1).Value = item.Seq;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, 2).Value = item.FolderPath;
            ws.Cell(row, 3).Value = item.Code;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Style.Font.Bold = true;

            ws.Cell(row, 4).Value = item.Title;
            ws.Cell(row, 5).Value = item.CustomerCode;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, 6).Value = $"v{item.ActiveVersion}";
            ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, 7).Value = item.CreatedAt;
            ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, 8).Value = item.ExpireDate;
            ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (item.IsExpired)
            {
                ws.Cell(row, 8).Style.Font.FontColor = XLColor.Red;
                ws.Cell(row, 8).Style.Font.Bold = true;
            }

            ws.Cell(row, 9).Value = item.Tags;

            ws.Cell(row, 10).Value = item.AttachmentCount;
            ws.Cell(row, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (int col = 1; col <= 10; col++)
            {
                var c = ws.Cell(row, col);
                c.Style.Fill.BackgroundColor = bg;
                c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                c.Style.Border.OutsideBorderColor = XLColor.FromHtml("#e2e8f0");
                c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            row++;
        }

        ws.Columns(1, 10).AdjustToContents();
        ws.Range(4, 1, row - 1, 10).SetAutoFilter();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string GenerateReadmeText(string title, int manifestCount, int docCount, int folderCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===============================================================================");
        sb.AppendLine($"   سامانه جامع آرشیو اسناد و مدارک — خروجی درختی بایگانی (ZIP)");
        sb.AppendLine("===============================================================================");
        sb.AppendLine($"عنوان پوشه خروجی: {title}");
        sb.AppendLine($"تاریخ صدور: {PersianDate.ToShort(DateTime.Now)} ساعت {DateTime.Now:HH:mm:ss}");
        sb.AppendLine($"تعداد پوشه‌ها: {folderCount}");
        sb.AppendLine($"تعداد کل مدارک: {docCount}");
        sb.AppendLine($"تعداد ردیف‌های شناسنامه: {manifestCount}");
        sb.AppendLine();
        sb.AppendLine("راهنمای ساختار پوشه‌ها:");
        sb.AppendLine("1. ساختار پوشه‌ها بر اساس سلسله‌مراتب درختی تعریف‌شده در سیستم مرتب شده است.");
        sb.AppendLine("2. درون هر پوشه، مدارک به صورت «کد مدرک _ عنوان مدرک» قرار دارند.");
        sb.AppendLine("3. فایل «_فهرست_شناسنامه_اسناد.xlsx» شامل مشخصات، تگ‌ها و تاریخ انقضای هر سند است.");
        sb.AppendLine("===============================================================================");
        return sb.ToString();
    }



    private static string SanitizePathPart(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Item";
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToHashSet();
        invalid.Add('/');
        invalid.Add('\\');
        invalid.Add(':');
        invalid.Add('*');
        invalid.Add('?');
        invalid.Add('"');
        invalid.Add('<');
        invalid.Add('>');
        invalid.Add('|');

        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }
        var res = sb.ToString().Trim();
        return string.IsNullOrEmpty(res) ? "Item" : res;
    }

    private class ManifestItem
    {
        public int Seq { get; set; }
        public string FolderPath { get; set; } = "";
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string CustomerCode { get; set; } = "";
        public int ActiveVersion { get; set; }
        public string CreatedAt { get; set; } = "";
        public string ExpireDate { get; set; } = "";
        public bool IsExpired { get; set; }
        public string Tags { get; set; } = "";
        public int AttachmentCount { get; set; }
    }
}
