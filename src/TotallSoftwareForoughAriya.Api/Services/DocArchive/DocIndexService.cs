using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

public interface IDocIndexService
{
    Task<DocExtractionResult> IndexAttachmentAsync(int attachmentId, bool force = false);
    Task<int> IndexVersionAsync(int versionId, bool force = false);
    Task<int> IndexDocumentAsync(int documentId, bool force = false);
    Task<DocReindexResultDto> ReindexAllAsync();
    Task QueueAttachmentIndexingAsync(int attachmentId);
}

public class DocIndexService : IDocIndexService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DocIndexService> _logger;

    public DocIndexService(IServiceProvider services, ILogger<DocIndexService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task QueueAttachmentIndexingAsync(int attachmentId)
    {
        using var scope = _services.CreateScope();
        await DocIndexQueue.EnqueueAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), attachmentId);
    }

    public async Task<DocExtractionResult> IndexAttachmentAsync(int attachmentId, bool force = false)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var extractor = scope.ServiceProvider.GetRequiredService<IDocTextExtractorService>();
        var fileStore = scope.ServiceProvider.GetRequiredService<FileStore>();

        var att = await db.AppAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.Module == "DocVersion");
        if (att == null)
            return new DocExtractionResult(false, "NotFound", "", "", 0, "پیوست آرشیو یافت نشد.");

        var existing = await db.DocExtractedTexts.FirstOrDefaultAsync(x => x.AttachmentId == attachmentId);
        if (existing != null && !force && existing.Status == "Indexed")
        {
            return new DocExtractionResult(true, existing.SourceType, existing.ExtractedText, existing.NormalizedText, existing.CharacterCount, null);
        }

        // Find DocumentId from DocumentVersion
        var version = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == att.RefId);
        if (version == null)
            return new DocExtractionResult(false, "NoVersion", "", "", 0, "ورژن مدرک یافت نشد.");

        // Read file bytes
        byte[]? bytes = null;
        if (!string.IsNullOrWhiteSpace(att.FilePath))
        {
            bytes = fileStore.ReadBytes(att.FilePath);
        }
        if (bytes == null && att.Data is { Length: > 0 })
        {
            bytes = att.Data;
        }

        if (bytes == null || bytes.Length == 0)
            return new DocExtractionResult(false, "NoContent", "", "", 0, "داده‌های فایل در دسترس نیست.");

        var res = await extractor.ExtractAsync(bytes, att.FileName, att.ContentType);

        if (existing == null)
        {
            existing = new DocExtractedText
            {
                DocumentId = version.DocumentId,
                VersionId = version.Id,
                AttachmentId = att.Id,
                FileName = att.FileName,
                ContentType = att.ContentType,
                SourceType = res.SourceType,
                ExtractedText = res.ExtractedText,
                NormalizedText = res.NormalizedText,
                Status = res.Success ? "Indexed" : "Failed",
                ErrorMessage = res.ErrorMessage,
                CharacterCount = res.CharacterCount,
                IndexedAt = DateTime.Now
            };
            db.DocExtractedTexts.Add(existing);
        }
        else
        {
            existing.DocumentId = version.DocumentId;
            existing.VersionId = version.Id;
            existing.FileName = att.FileName;
            existing.ContentType = att.ContentType;
            existing.SourceType = res.SourceType;
            existing.ExtractedText = res.ExtractedText;
            existing.NormalizedText = res.NormalizedText;
            existing.Status = res.Success ? "Indexed" : "Failed";
            existing.ErrorMessage = res.ErrorMessage;
            existing.CharacterCount = res.CharacterCount;
            existing.IndexedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("ایندکس پیوست {FileName} (مدرک {DocId}) انجام شد. وضعیت: {Status}، حروف: {Count}",
            att.FileName, version.DocumentId, existing.Status, existing.CharacterCount);

        return res;
    }

    public async Task<int> IndexVersionAsync(int versionId, bool force = false)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attIds = await db.AppAttachments.Where(a => a.Module == "DocVersion" && a.RefId == versionId)
            .Select(a => a.Id).ToListAsync();

        var count = 0;
        foreach (var id in attIds)
        {
            var res = await IndexAttachmentAsync(id, force);
            if (res.Success) count++;
        }
        return count;
    }

    public async Task<int> IndexDocumentAsync(int documentId, bool force = false)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var versionIds = await db.DocumentVersions.Where(v => v.DocumentId == documentId)
            .Select(v => v.Id).ToListAsync();

        var count = 0;
        foreach (var vid in versionIds)
        {
            count += await IndexVersionAsync(vid, force);
        }
        return count;
    }

    public async Task<DocReindexResultDto> ReindexAllAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var atts = await db.AppAttachments.Where(a => a.Module == "DocVersion")
            .Select(a => a.Id).ToListAsync();

        foreach (var id in atts) await DocIndexQueue.EnqueueAsync(db, id);
        return new DocReindexResultDto { ProcessedCount = atts.Count, Message = $"{atts.Count} فایل در صف پردازش قرار گرفت." };
    }
}
