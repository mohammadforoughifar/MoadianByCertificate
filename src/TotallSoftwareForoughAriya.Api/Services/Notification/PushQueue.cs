using System.Text.Json;
using System.Text.Encodings.Web;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

public static class PushQueue
{
    // Stage with the business notification, then commit through the caller's SaveChanges.
    public static async Task<int> StageAsync(AppDbContext db, IEnumerable<int> users, string title, string? body, string? link, string? tag = null)
    {
        var ids = users.Where(x => x > 0).Distinct().ToArray();
        var subs = await db.PushSubscriptions.AsNoTracking().Where(s => ids.Contains(s.UserId) && db.Users.Any(u => u.Id == s.UserId && u.IsActive)).ToListAsync();
        var now = DateTime.UtcNow;
        tag ??= Guid.NewGuid().ToString("N");
        foreach (var s in subs)
            db.PushDeliveries.Add(new PushDelivery { SubscriptionId = s.Id, UserId = s.UserId, Tag = tag,
                Title = Limit(title, 120), Body = Limit(body, 300), Link = Limit(link, 300),
                CreatedAtUtc = now, NextAttemptAtUtc = now, ExpiresAtUtc = now.AddHours(48) });
        return subs.Count;
    }
    private static string Limit(string? text, int max) => string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];

    public static async Task<PushDelivery?> ClaimAsync(AppDbContext db, DateTime now, CancellationToken ct = default)
    {
        var candidate = await db.PushDeliveries.AsNoTracking().Where(j =>
            (j.Status == "Pending" && j.NextAttemptAtUtc <= now) || (j.Status == "Working" && j.LeaseUntilUtc <= now))
            .OrderBy(j => j.Id).FirstOrDefaultAsync(ct);
        if (candidate == null) return null;
        var token = Guid.NewGuid().ToString("N");
        var changed = await db.PushDeliveries.Where(j => j.Id == candidate.Id &&
            ((j.Status == "Pending" && j.NextAttemptAtUtc <= now) || (j.Status == "Working" && j.LeaseUntilUtc <= now)))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "Working").SetProperty(j => j.LeaseToken, token)
                .SetProperty(j => j.LeaseUntilUtc, now.AddMinutes(2)).SetProperty(j => j.Attempts, j => j.Attempts + 1), ct);
        if (changed != 1) return null;
        return await db.PushDeliveries.AsNoTracking().SingleAsync(j => j.Id == candidate.Id && j.LeaseToken == token, ct);
    }

    public static async Task ProcessAsync(AppDbContext db, IPushTransport transport, PushDelivery job, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (!await db.PushDeliveries.AnyAsync(j => j.Id == job.Id && j.Status == "Working" && j.LeaseToken == job.LeaseToken, ct)) return;
        var sub = await db.PushSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == job.SubscriptionId && s.UserId == job.UserId, ct);
        var status = "Cancelled"; string? error = null;
        if (job.ExpiresAtUtc <= now) status = "Expired";
        else if (job.Attempts > 6) { status = "Failed"; error = "ATTEMPTS_EXHAUSTED"; }
        else if (sub != null && await db.Users.AnyAsync(u => u.Id == job.UserId && u.IsActive, ct))
        {
            if (!PushEndpointPolicy.ValidEndpoint(sub.Endpoint)) { status = "Failed"; error = "INVALID_ENDPOINT"; }
            else
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var payload = JsonSerializer.Serialize(new { title = job.Title, body = job.Body, link = job.Link, tag = "inv-" + job.Tag,
                        userId = job.UserId, expiresAtUtc = DateTime.SpecifyKind(job.ExpiresAtUtc, DateTimeKind.Utc), icon = "/icon-192.png", badge = "/icon-192.png" },
                        new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    var code = await transport.SendAsync(sub, payload, timeout.Token);
                    if (code is >= 200 and < 300) status = "Accepted"; // Provider acceptance is not device delivery.
                    else if (code is 404 or 410)
                    {
                        status = "Expired"; error = "SUBSCRIPTION_EXPIRED";
                        await db.PushSubscriptions.Where(s => s.Id == sub.Id && s.UserId == job.UserId && s.P256DH == sub.P256DH && s.Auth == sub.Auth).ExecuteDeleteAsync(ct);
                    }
                    else { error = "PROVIDER_" + code; status = code == 429 || code >= 500 || code is 401 or 403 ? "Pending" : "Failed"; }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { status = "Pending"; error = "NETWORK_OR_TIMEOUT"; }
                if (status == "Pending" && job.Attempts >= 6) status = "Failed";
            }
        }
        await db.PushDeliveries.Where(j => j.Id == job.Id && j.LeaseToken == job.LeaseToken && j.Status == "Working")
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, status).SetProperty(j => j.ErrorCode, error)
                .SetProperty(j => j.NextAttemptAtUtc, now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, job.Attempts - 1))))
                .SetProperty(j => j.LeaseToken, (string?)null).SetProperty(j => j.LeaseUntilUtc, (DateTime?)null), ct);
    }
}

public sealed class PushDeliveryWorker(IServiceScopeFactory scopes, ILogger<PushDeliveryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (scope.ServiceProvider.GetRequiredService<IPushService>().IsConfigured)
                {
                    var transport = scope.ServiceProvider.GetRequiredService<IPushTransport>();
                    for (var i = 0; i < 25; i++)
                    {
                        var job = await PushQueue.ClaimAsync(db, DateTime.UtcNow, stoppingToken);
                        if (job == null) break;
                        await PushQueue.ProcessAsync(db, transport, job, stoppingToken);
                    }
                }
                var cutoff = DateTime.UtcNow.AddDays(-7);
                await db.PushDeliveries.Where(j => j.CreatedAtUtc < cutoff && j.Status != "Working").ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Push queue iteration failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
