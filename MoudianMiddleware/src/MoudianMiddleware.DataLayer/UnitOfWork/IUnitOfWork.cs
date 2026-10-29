namespace MoudianMiddleware.DataLayer.UnitOfWork;

/// <summary>
/// واحد کار: نقطه‌ی واحد ذخیره‌ی تغییرات (SaveChanges) در یک درخواست.
/// </summary>
/// <remarks>
/// Repositoryها تغییرات را فقط در ChangeTracker ثبت می‌کنند؛ تا زمانی که
/// <see cref="SaveChangesAsync"/> صدا زده نشود چیزی در دیتابیس نوشته نمی‌شود.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>ذخیره‌ی همه‌ی تغییرات ثبت‌شده در ChangeTracker.</summary>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>تعداد رکوردهای تأثیرگرفته.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
