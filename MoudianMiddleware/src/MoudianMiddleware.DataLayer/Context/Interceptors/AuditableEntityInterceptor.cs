using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Context.Interceptors;

/// <summary>
/// پر کردن خودکار فیلدهای حسابرسی و تبدیل «حذف» به «حذف نرم».
/// </summary>
/// <remarks>
/// بنابراین در سرویس‌ها نباید <c>CreatedAt</c> یا <c>ModifiedAt</c> را دستی مقداردهی کنید.
/// </remarks>
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserProvider _currentUser;
    private readonly IClock _clock;

    /// <summary>
    /// سازنده با تزریق وابستگی‌های لازم.
    /// </summary>
    /// <param name="currentUser">تأمین‌کننده‌ی کاربر جاری.</param>
    /// <param name="clock">منبع زمان.</param>
    public AuditableEntityInterceptor(ICurrentUserProvider currentUser, IClock clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        ConvertDeletesToSoftDeletes(context, now);

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedByUserId = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedByUserId = userId;
                    // فیلدهای ایجاد نباید در ویرایش تغییر کنند
                    entry.Property(x => x.CreatedAt).IsModified = false;
                    entry.Property(x => x.CreatedByUserId).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }

    private static void ConvertDeletesToSoftDeletes(DbContext context, DateTimeOffset now)
    {
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;

            if (entry.Entity is AuditableEntity auditable)
            {
                auditable.DeletedAt = now;
            }
        }
    }
}
