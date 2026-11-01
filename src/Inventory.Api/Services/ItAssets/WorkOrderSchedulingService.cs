using System.Globalization;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>Materializes a finite series. The caller owns the database transaction.</summary>
public sealed class WorkOrderSchedulingService(AppDbContext db)
{
    public async Task<int> MaterializeAsync(WorkOrder first, DateTime? notBefore = null)
    {
        if (first.Recurrence == WorkOrderRecurrence.None || first.RecurrenceSeriesId != null) return 0;
        var dates = WorkOrderSchedule.Dates(first.DueAt, first.Recurrence).Skip(1);
        if (notBefore.HasValue) dates = dates.Where(d => d > notBefore.Value);
        // Legacy sub-orders may have a parent whose deadline is shorter than the series.
        if (first.ParentOrderId.HasValue)
        {
            var parent = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == first.ParentOrderId);
            if (parent == null) return 0;
            dates = dates.Where(d => d <= parent.DueAt);
        }
        first.RecurrenceSeriesId = Guid.NewGuid().ToString("N");
        first.RecurrenceScheduledAt = first.DueAt;
        var assignees = await db.WorkOrderAssignees.AsNoTracking().Where(a => a.OrderId == first.Id).ToListAsync();
        var checklist = await db.WorkOrderChecklistItems.AsNoTracking().Where(c => c.OrderId == first.Id).ToListAsync();
        var orders = dates.Select(d => new WorkOrder
        {
            Title = first.Title, Description = first.Description, OwnerUserId = first.OwnerUserId,
            OwnerName = first.OwnerName, DueAt = d, Priority = first.Priority, Recurrence = first.Recurrence,
            RecurrenceSeriesId = first.RecurrenceSeriesId, RecurrenceScheduledAt = d,
            RecurrenceParentId = first.Id, SourceModule = first.SourceModule, SourceId = first.SourceId,
            Tags = first.Tags, ParentOrderId = first.ParentOrderId,
            ProjectId = first.ProjectId, Weight = first.Weight
        }).ToList();
        db.WorkOrders.AddRange(orders);
        await db.SaveChangesAsync();
        var pc = new PersianCalendar();
        // نوبت‌های بعدی سری، فقط «پیش‌نمایش» شرح را به ارث می‌برند (ستون اصلی). متن کامل
        // (تصاویر نامهٔ مبدأ) هم کپی می‌شود تا نوبت‌های بعدی هم تصویر داشته باشند.
        var fullDescription = await WorkOrderDescriptionStore.GetAsync(db, first.Id);
        foreach (var order in orders)
        {
            order.Number = $"WO/{pc.GetYear(order.CreatedAt)}/{order.Id}";
            if (fullDescription != null)
                await WorkOrderDescriptionStore.SaveAsync(db, order.Id, fullDescription);
            foreach (var a in assignees)
                db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = order.Id, UserId = a.UserId, Name = a.Name });
            foreach (var c in checklist)
                db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem { OrderId = order.Id, Text = c.Text, SortOrder = c.SortOrder });
            db.WorkOrderLogs.Add(new WorkOrderLog
            {
                OrderId = order.Id, ActorName = "سیستم", Action = "Scheduled",
                Text = $"ثبت تقویمی نوبت {WorkOrderRecurrence.ToFa(first.Recurrence)} — سری {first.Number}"
            });
        }
        db.WorkOrderLogs.Add(new WorkOrderLog
        {
            OrderId = first.Id, ActorName = "سیستم", Action = "Scheduled",
            Text = $"{orders.Count} نوبت بعدی از پیش در تقویم همهٔ گیرندگان ثبت شد."
        });
        await db.SaveChangesAsync();
        return orders.Count;
    }

    /// <summary>Upgrade only open leaves of old close-triggered chains, without replaying history.</summary>
    public async Task UpgradeLegacyAsync(DateTime now)
    {
        var ids = await db.WorkOrders.Where(w => w.Status == "Open" && w.Recurrence > 0 && w.RecurrenceSeriesId == null)
            .Where(w => !db.WorkOrders.IgnoreQueryFilters().Any(n => n.RecurrenceParentId == w.Id))
            .Select(w => w.Id).ToListAsync();
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var order = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
            if (order != null && order.RecurrenceSeriesId == null && order.Status == "Open")
                await MaterializeAsync(order, now);
            await tx.CommitAsync();
        }
    }
}
