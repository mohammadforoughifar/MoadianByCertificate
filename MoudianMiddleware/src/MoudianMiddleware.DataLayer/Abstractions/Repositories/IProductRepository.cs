using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی کالا و خدمات.
/// </summary>
public interface IProductRepository : IGenericRepository<Product>
{
    /// <summary>بررسی یکتایی شناسه‌ی کالا/خدمت در مستأجر جاری.</summary>
    /// <param name="sstid">شناسه‌ی کالا/خدمت.</param>
    /// <param name="excludeId">شناسه‌ای که باید از بررسی مستثنی شود (در ویرایش).</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>در صورت وجود تکراری <c>true</c>.</returns>
    Task<bool> ExistsSstidAsync(string sstid, int? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>دریافت صفحه‌ای از کالاها همراه با واحد اندازه‌گیری.</summary>
    /// <param name="search">جست‌وجوی متنی.</param>
    /// <param name="isActive">فیلتر فعال بودن.</param>
    /// <param name="skip">تعداد ردشده.</param>
    /// <param name="take">تعداد درخواستی.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>آیتم‌های صفحه و تعداد کل.</returns>
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedAsync(
        string? search,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
