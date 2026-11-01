using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Services.DocArchive;

public static class DocQuery
{
    public static Task<IQueryable<ArchiveDocument>> AccessibleAsync(AppDbContext db, IDocAccessService access, int userId, bool manager, DocAccessLevel minimum = DocAccessLevel.View)
        => access.AccessibleDocumentsAsync(userId, manager, minimum);

    public static IQueryable<ArchiveDocument> ApplyFilters(AppDbContext db, IQueryable<ArchiveDocument> q, DocSearchFilterDto f, DateTime today)
    {
        q = f.Status?.ToLowerInvariant() switch
        {
            "deleted" => q.Where(d => d.IsDeleted),
            "inactive" => q.Where(d => !d.IsDeleted && !d.IsActive),
            "all" => q.Where(d => !d.IsDeleted),
            _ => q.Where(d => !d.IsDeleted && d.IsActive)
        };
        if (f.CreatedFrom is DateTime cf) q = q.Where(d => d.CreatedAt >= cf.Date);
        if (f.CreatedTo is DateTime ct) { var end = ct.Date.AddDays(1); q = q.Where(d => d.CreatedAt < end); }
        if (f.ExpiryFrom is DateTime ef) q = q.Where(d => d.ExpireDate >= ef.Date);
        if (f.ExpiryTo is DateTime et) { var end = et.Date.AddDays(1); q = q.Where(d => d.ExpireDate < end); }
        var horizon = today.AddDays(61);
        q = f.ExpiryStatus switch
        {
            "expired" => q.Where(d => d.ExpireDate < today),
            "expiring" => q.Where(d => d.ExpireDate >= today && d.ExpireDate < horizon),
            "valid" => q.Where(d => d.ExpireDate >= today),
            _ => q
        };
        if (f.CreatedByUserId is int creator) q = q.Where(d => d.CreatedByUserId == creator);
        if (f.ApproverUserId is int approver) q = q.Where(d => db.DocumentApprovers.Any(a => a.DocumentId == d.Id && a.UserId == approver));
        foreach (var tag in f.TagIds.Distinct()) q = q.Where(d => db.DocumentTags.Any(t => t.DocumentId == d.Id && t.TagId == tag));
        if (f.HasAttachment is bool has) q = q.Where(d => db.DocumentVersions.Any(v => v.DocumentId == d.Id && db.AppAttachments.Any(a => a.Module == "DocVersion" && a.RefId == v.Id)) == has);

        if (f.FileTypes.Count > 0)
        {
            var extensions = f.FileTypes.SelectMany(t => t.ToLowerInvariant() switch
            {
                "pdf" => new[] { ".pdf" },
                "word" => new[] { ".doc", ".docx" },
                "excel" => new[] { ".xls", ".xlsx", ".csv" },
                "image" => new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" },
                "text" => new[] { ".txt", ".json", ".xml", ".md" },
                _ => Array.Empty<string>()
            }).Distinct();
            var ids = db.DocumentVersions.Where(v => false).Select(v => v.DocumentId);
            foreach (var ext in extensions)
                ids = ids.Union(from v in db.DocumentVersions
                                join a in db.AppAttachments on v.Id equals a.RefId
                                where a.Module == "DocVersion" && a.FileName.ToLower().EndsWith(ext)
                                select v.DocumentId);
            q = q.Where(d => ids.Contains(d.Id));
        }
        return q;
    }
}
