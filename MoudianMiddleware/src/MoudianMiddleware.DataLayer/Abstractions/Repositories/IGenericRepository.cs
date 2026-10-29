using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// قرارداد Repository عمومی برای عملیات متداول CRUD.
/// </summary>
/// <typeparam name="TEntity">نوع موجودیت (باید <see cref="BaseEntity"/> باشد).</typeparam>
/// <remarks>
/// متدهای این قرارداد، فیلترهای سراسری (مستأجر و حذف نرم) را به‌طور خودکار اعمال می‌کنند.
/// فقط برای عملیات سیستمی (Jobها/Seed) از <see cref="Query"/> با
/// <c>ignoreQueryFilters: true</c> استفاده کنید و دلیل آن را در کد بنویسید.
/// </remarks>
public interface IGenericRepository<TEntity>
    where TEntity : BaseEntity
{
    /// <summary>دسترسی به کوئری برای فیلترهای پیچیده (پیش‌فرض: بدون Tracking).</summary>
    /// <param name="asNoTracking">آیا بدون Tracking خوانده شود؟</param>
    /// <param name="ignoreQueryFilters">آیا فیلترهای سراسری نادیده گرفته شوند؟</param>
    /// <returns>کوئری آماده.</returns>
    IQueryable<TEntity> Query(bool asNoTracking = true, bool ignoreQueryFilters = false);

    /// <summary>دریافت موجودیت با شناسه (با اعمال فیلترها).</summary>
    /// <param name="id">شناسه.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>موجودیت یا <c>null</c>.</returns>
    Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>دریافت همه‌ی رکوردهای قابل مشاهده.</summary>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>فهرست موجودیت‌ها.</returns>
    Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>افزودن موجودیت جدید (SaveChanges جداگانه انجام می‌شود).</summary>
    /// <param name="entity">موجودیت.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>وظیفه‌ی انجام عملیات.</returns>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>افزودن چند موجودیت.</summary>
    /// <param name="entities">موجودیت‌ها.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>وظیفه‌ی انجام عملیات.</returns>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>علامت‌گذاری موجودیت برای ویرایش.</summary>
    /// <param name="entity">موجودیت.</param>
    void Update(TEntity entity);

    /// <summary>حذف موجودیت (در صورت پشتیبانی، حذف نرم انجام می‌شود).</summary>
    /// <param name="entity">موجودیت.</param>
    void Remove(TEntity entity);

    /// <summary>بررسی وجود موجودیت با شناسه.</summary>
    /// <param name="id">شناسه.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>در صورت وجود <c>true</c>.</returns>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>شمارش رکوردهای قابل مشاهده.</summary>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>تعداد.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
