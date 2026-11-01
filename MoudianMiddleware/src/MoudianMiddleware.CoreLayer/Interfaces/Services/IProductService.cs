using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Products;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>عملیات کاربردی کالا/خدمت.</summary>
public interface IProductService
{
    /// <summary>دریافت کالا در محدوده‌ی مودی جاری.</summary>
    Task<ProductViewModel> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>دریافت صفحه‌ای از کالاها.</summary>
    Task<PagedResult<ProductViewModel>> ListAsync(ProductListQuery query, CancellationToken cancellationToken = default);
    /// <summary>ایجاد کالا.</summary>
    Task<ProductViewModel> CreateAsync(ProductWriteRequest request, CancellationToken cancellationToken = default);
    /// <summary>ویرایش کالا.</summary>
    Task<ProductViewModel> UpdateAsync(int id, ProductWriteRequest request, CancellationToken cancellationToken = default);
    /// <summary>حذف نرم کالا.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
