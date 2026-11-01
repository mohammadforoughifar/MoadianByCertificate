using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.DocArchive;

public sealed class DocImportOcrWorker(
    IServiceScopeFactory scopes,
    IOptions<DocImportOcrOptions> options,
    ILogger<DocImportOcrWorker> log) : BackgroundService
{
    /// <summary>یادداشت نسخه‌هایی که ورود انبوه ساخته است — نشانهٔ شناسایی همین فایل‌ها.</summary>
    internal const string ImportChangeNote = "ورود انبوه از پوشهٔ آرشیو";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // چند ثانیه صبر تا برنامه کامل بالا بیاید
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var opt = options.Value;
                if (!opt.Enabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                    continue;
                }

                if (!DocImportOcrSchedule.InWindow(opt, DateTime.Now))
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                int processed;
                using (var scope = scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    processed = await ProcessBatchAsync(db, opt, stoppingToken);
                }

                if (processed == 0)
                {
                    // چیزی برای متن‌گیری نمانده؛ تا بررسی بعدی صبر می‌کنیم
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                }
                else if (opt.PauseBetweenBatchesMs > 0)
                {
                    await Task.Delay(opt.PauseBetweenBatchesMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "متن‌گیری شبانهٔ فایل‌های ورود انبوه با خطا مواجه شد");
                try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// یک دسته از پیوست‌های ورود انبوه را که هنوز متن ندارند در صف می‌گذارد.
    /// خودِ متن‌گیری را کارگر صف (DocIndexWorker) انجام می‌دهد؛ اینجا فقط صف پر می‌شود.
    /// </summary>
    private async Task<int> ProcessBatchAsync(AppDbContext db, DocImportOcrOptions opt, CancellationToken ct)
    {
        var attachmentIds = await (
            from a in db.AppAttachments
            join v in db.DocumentVersions on a.RefId equals v.Id
            join d in db.Documents on v.DocumentId equals d.Id
            where a.Module == "DocVersion"
                  && v.ChangeNote == ImportChangeNote
                  && !d.IsDeleted
                  && !db.DocIndexJobs.Any(j => j.AttachmentId == a.Id)
                  && !db.DocExtractedTexts.Any(e => e.AttachmentId == a.Id && e.Status == "Indexed")
            orderby a.Id
            select a.Id
        ).Take(Math.Clamp(opt.BatchSize, 1, 200)).ToListAsync(ct);

        if (attachmentIds.Count == 0) return 0;

        foreach (var id in attachmentIds)
        {
            ct.ThrowIfCancellationRequested();
            await DocIndexQueue.EnqueueAsync(db, id);
        }

        log.LogInformation("متن‌گیری شبانه: {Count} پیوست ورود انبوه در صف قرار گرفت", attachmentIds.Count);
        return attachmentIds.Count;
    }
}
