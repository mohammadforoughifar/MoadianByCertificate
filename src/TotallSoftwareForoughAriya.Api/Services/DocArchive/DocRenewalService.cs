using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;
using System.Net;
namespace Inventory.Api.Services.DocArchive;

public static class DocClock
{
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"));
    public static DateTime Today => Now.Date;
}
public class DocRenewalService(AppDbContext db, IDocAccessService access)
{
    public async Task<bool> HasPermissionAsync(int userId, string module, string action)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user == null) return false;
        if (!await db.UserRoles.AnyAsync(r => r.UserId == userId)) return user.Role == "Admin" || (user.Role == "Operator" && action is "Create" or "Read");
        return await (from ur in db.UserRoles
                      join role in db.Roles on ur.RoleId equals role.Id
                      join rp in db.RolePermissions on role.Id equals rp.RoleId
                      join p in db.Permissions on rp.PermissionId equals p.Id
                      where ur.UserId == userId && role.IsActive && p.Module == module && p.Action == action
                      select p.Id).AnyAsync();
    }
    public async Task<bool> CanAssignAsync(int owner, int assignee)
    {
        if (!await HasPermissionAsync(owner, "WorkOrders", "Create")) return false;
        if (!await db.Users.AnyAsync(u => u.Id == assignee && u.IsActive)) return false;
        if (owner == assignee) return true;
        if (!await HasPermissionAsync(owner, "WorkOrders", "AssignOthers")) return false;
        if (await db.Users.AnyAsync(u => u.Id == owner && u.Role == "Admin") && !await db.UserRoles.AnyAsync(r => r.UserId == owner)) return true;
        return await db.WorkOrderAllowedAssignees.AnyAsync(a => a.OwnerUserId == owner && a.TargetUserId == assignee);
    }
    public async Task<int?> RunForDocumentAsync(int documentId, DateTime now)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var policy = await db.DocRenewalPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.DocumentId == documentId && p.Enabled);
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.IsActive && !d.IsDeleted);
        if (policy == null || doc?.ExpireDate is not DateTime expiry || expiry.Date > now.Date.AddDays(policy.LeadDays)) return null;
        if (await db.DocRenewalRuns.AnyAsync(r => r.DocumentId == documentId && r.ExpiryDate == expiry.Date)) return null;
        var manager = await HasPermissionAsync(policy.OwnerUserId, "DocArchive", "Manage");
        if ((await access.DocumentAccessAsync(policy.OwnerUserId, manager, documentId)).Level < DocAccessLevel.Full || !await CanAssignAsync(policy.OwnerUserId, policy.AssigneeUserId)) return null;
        var assigneeManager = await HasPermissionAsync(policy.AssigneeUserId, "DocArchive", "Manage");
        if ((await access.DocumentAccessAsync(policy.AssigneeUserId, assigneeManager, documentId)).Level < DocAccessLevel.Read) return null;
        var owner = await db.Users.SingleAsync(u => u.Id == policy.OwnerUserId);
        var assignee = await db.Users.SingleAsync(u => u.Id == policy.AssigneeUserId);
        var title = $"تمدید مدرک {doc.Code} — {doc.Title}";
        var order = new WorkOrder
        {
            Title = title.Length > 200 ? title[..200] : title,
            Description = $"<p>پیگیری تمدید مدرک {WebUtility.HtmlEncode(doc.Code)}</p><p>ثبت نسخه و تاریخ اعتبار جدید پس از تمدید.</p>",
            OwnerUserId = owner.Id,
            OwnerName = owner.Username,
            SourceModule = "Document",
            SourceId = doc.Id,
            DueAt = expiry.Date.AddHours(17) > now ? expiry.Date.AddHours(17) : now.AddDays(1),
            CreatedAt = now,
            Status = "Open",
            Priority = WorkOrderPriority.High,
            Tags = ",تمدید مدرک,"
        };
        db.WorkOrders.Add(order);
        await db.SaveChangesAsync();
        order.Number = $"WO/{new PersianCalendar().GetYear(now)}/{order.Id}";
        db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = order.Id, UserId = assignee.Id, Name = assignee.Username });
        db.WorkOrderLogs.Add(new WorkOrderLog { OrderId = order.Id, ActorName = "سیستم", Action = "Created", Text = $"پیگیری خودکار تمدید مدرک {doc.Code}", CreatedAt = now });
        db.DocRenewalRuns.Add(new DocRenewalRun { DocumentId = doc.Id, ExpiryDate = expiry.Date, WorkOrderId = order.Id, CreatedAtUtc = DateTime.UtcNow });
        db.DocumentLogs.Add(new DocumentLog { DocumentId = doc.Id, UserId = owner.Id, UserName = "سیستم", Action = "RenewalOrder", Detail = order.Number, CreatedAt = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return order.Id;
    }
}
public class DocRenewalWorker(IServiceScopeFactory scopes, ILogger<DocRenewalWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = await db.DocRenewalPolicies.Where(p => p.Enabled).Select(p => p.DocumentId).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    try
                    {
                        using var itemScope = scopes.CreateScope();
                        var service = itemScope.ServiceProvider.GetRequiredService<DocRenewalService>();
                        var orderId = await service.RunForDocumentAsync(id, DocClock.Now);
                        if (orderId is int oid)
                        {
                            var itemDb = itemScope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var assignee = await itemDb.WorkOrderAssignees.Where(a => a.OrderId == oid).Select(a => a.UserId).SingleAsync();
                            await itemScope.ServiceProvider.GetRequiredService<INotifyService>().SendAsync(assignee, "پیگیری تمدید مدرک", "دستور کار تمدید ثبت شد.", "سیستم", "دستور کار", $"/work-orders?open={oid}");
                        }
                    }
                    catch (Exception ex) { log.LogError(ex, "Renewal document {Id} failed; will retry", id); }
                }
            }
            catch (Exception ex) { log.LogError(ex, "Renewal worker unavailable"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
