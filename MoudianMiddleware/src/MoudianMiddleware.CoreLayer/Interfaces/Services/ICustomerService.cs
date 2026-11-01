using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Customers;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>عملیات کاربردی مشتری.</summary>
public interface ICustomerService
{
    /// <summary>دریافت مشتری در محدوده‌ی مودی جاری.</summary>
    Task<CustomerViewModel> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>دریافت صفحه‌ای از مشتریان.</summary>
    Task<PagedResult<CustomerViewModel>> ListAsync(CustomerListQuery query, CancellationToken cancellationToken = default);
    /// <summary>ایجاد مشتری.</summary>
    Task<CustomerViewModel> CreateAsync(CustomerWriteRequest request, CancellationToken cancellationToken = default);
    /// <summary>ویرایش مشتری.</summary>
    Task<CustomerViewModel> UpdateAsync(int id, CustomerWriteRequest request, CancellationToken cancellationToken = default);
    /// <summary>حذف نرم مشتری.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
