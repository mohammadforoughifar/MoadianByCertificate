using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Services.DocArchive;

public static class DocIndexQueue
{
    public static async Task EnqueueAsync(AppDbContext db, int attachmentId)
    {
        var now = DateTime.UtcNow;
        async Task<int> Update() => await db.DocIndexJobs.Where(j => j.AttachmentId == attachmentId).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Generation, j => j.Generation + 1).SetProperty(j => j.Attempts, 0)
            .SetProperty(j => j.Status, j => j.Status == "Working" ? "Working" : "Pending")
            .SetProperty(j => j.NextAttemptAtUtc, now).SetProperty(j => j.UpdatedAtUtc, now).SetProperty(j => j.Error, (string?)null));
        if (await Update() != 0) return;
        var row = new DocIndexJob { AttachmentId = attachmentId, UpdatedAtUtc = now, NextAttemptAtUtc = now };
        db.DocIndexJobs.Add(row);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            if (await Update() == 0) throw;
        }
    }

    // Also repairs the crash window between attachment commit and enqueue.
    public static async Task ReconcileAsync(AppDbContext db)
    {
        var ids = await (from a in db.AppAttachments
                         join v in db.DocumentVersions on a.RefId equals v.Id
                         join d in db.Documents on v.DocumentId equals d.Id
                         where a.Module == "DocVersion" && !d.IsDeleted && !db.DocIndexJobs.Any(j => j.AttachmentId == a.Id)
                            && !db.DocExtractedTexts.Any(e => e.AttachmentId == a.Id && e.Status == "Indexed")
                            // پیوست‌های ورود انبوه وارد صف OCR نمی‌شوند (OCR فاز ۲ و شبانه است).
                            // نشانهٔ آن‌ها یادداشت نسخه است: تنها مسیری که این متن را می‌نویسد، ورود انبوه است.
                            && v.ChangeNote != "ورود انبوه از پوشهٔ آرشیو"
                         orderby a.Id
                         select a.Id).Take(100).ToListAsync();
        foreach (var id in ids) await EnqueueAsync(db, id);
    }

    public static async Task<DocIndexJob?> ClaimAsync(AppDbContext db, DateTime now)
    {
        var candidate = await db.DocIndexJobs.AsNoTracking().Where(j =>
            (j.Status == "Pending" && j.NextAttemptAtUtc <= now) || (j.Status == "Working" && j.LeaseUntilUtc < now))
            .OrderBy(j => j.NextAttemptAtUtc).ThenBy(j => j.AttachmentId).FirstOrDefaultAsync();
        if (candidate == null) return null;
        var token = Guid.NewGuid().ToString("N");
        var count = await db.DocIndexJobs.Where(j => j.AttachmentId == candidate.AttachmentId && j.Generation == candidate.Generation &&
            ((j.Status == "Pending" && j.NextAttemptAtUtc <= now) || (j.Status == "Working" && j.LeaseUntilUtc < now)))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "Working").SetProperty(j => j.LeaseToken, token)
                .SetProperty(j => j.LeaseUntilUtc, now.AddMinutes(10)).SetProperty(j => j.Attempts, j => j.Attempts + 1).SetProperty(j => j.UpdatedAtUtc, now));
        if (count == 0) return null;
        candidate.LeaseToken = token; candidate.Attempts++;
        return candidate;
    }
    public static async Task CompleteAsync(AppDbContext db, DocIndexJob claim, bool success, string? error)
    {
        var now = DateTime.UtcNow;
        var message = error is { Length: > 500 } ? error[..500] : error;
        await db.DocIndexJobs.Where(j => j.AttachmentId == claim.AttachmentId && j.LeaseToken == claim.LeaseToken)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, j => j.Generation != claim.Generation ? "Pending" : success ? "Done" : j.Attempts >= 5 ? "Failed" : "Pending")
                .SetProperty(j => j.Error, message).SetProperty(j => j.LeaseToken, (string?)null).SetProperty(j => j.LeaseUntilUtc, (DateTime?)null)
                .SetProperty(j => j.UpdatedAtUtc, now).SetProperty(j => j.NextAttemptAtUtc, now.AddSeconds(Math.Min(900, 10 * Math.Pow(2, Math.Min(claim.Attempts, 6))))));
    }
}

public sealed class DocIndexWorker(IServiceScopeFactory scopes, IDocIndexService index, ILogger<DocIndexWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReconcile = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (DateTime.UtcNow >= nextReconcile) { await DocIndexQueue.ReconcileAsync(db); nextReconcile = DateTime.UtcNow.AddMinutes(1); }
                var job = await DocIndexQueue.ClaimAsync(db, DateTime.UtcNow);
                if (job == null) { await Task.Delay(5000, stoppingToken); continue; }
                using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var heartbeat = Heartbeat(job, heartbeatStop.Token);
                try
                {
                    var alive = await db.AppAttachments.AnyAsync(a => a.Id == job.AttachmentId && a.Module == "DocVersion" && db.DocumentVersions.Any(v => v.Id == a.RefId && db.Documents.Any(d => d.Id == v.DocumentId && !d.IsDeleted)));
                    var result = alive ? await index.IndexAttachmentAsync(job.AttachmentId, true) : null;
                    await DocIndexQueue.CompleteAsync(db, job, result?.Success ?? true, result?.ErrorMessage);
                }
                catch (Exception ex) { log.LogError(ex, "Index job {Id} failed", job.AttachmentId); await DocIndexQueue.CompleteAsync(db, job, false, "پردازش ناموفق؛ جزئیات در گزارش سرور ثبت شد."); }
                finally { heartbeatStop.Cancel(); await heartbeat; }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Document index queue unavailable"); await Task.Delay(10000, stoppingToken); }
        }
    }
    private async Task Heartbeat(DocIndexJob job, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.DocIndexJobs.Where(j => j.AttachmentId == job.AttachmentId && j.LeaseToken == job.LeaseToken)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(10)), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { log.LogWarning(ex, "Index lease heartbeat failed"); }
    }
}
