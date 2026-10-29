using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی مشتریان.
/// </summary>
public interface ICustomerRepository : IGenericRepository<Customer>
{
    /// <summary>بررسی یکتایی کد داخلی مشتری در مستأجر جاری.</summary>
    /// <param name="code">کد مشتری.</param>
    /// <param name="excludeId">شناسه‌ی مستثنی (در ویرایش).</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>در صورت وجود تکراری <c>true</c>.</returns>
    Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>دریافت صفحه‌ای از مشتریان.</summary>
    /// <param name="search">جست‌وجوی متنی (نام/کد/کد ملی).</param>
    /// <param name="buyerType">فیلتر نوع شخص.</param>
    /// <param name="isActive">فیلتر فعال بودن.</param>
    /// <param name="skip">تعداد ردشده.</param>
    /// <param name="take">تعداد درخواستی.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>آیتم‌های صفحه و تعداد کل.</returns>
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        string? search,
        BuyerType? buyerType,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
