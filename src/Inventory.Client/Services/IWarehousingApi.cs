using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// قراردادهای سرویس‌های ماژول انبارداری (سمت کلاینت)
// صفحات فقط با این اینترفیس‌ها کار می‌کنند و هیچ آدرس API در صفحه نیست.
// =====================================================================

/// <summary>گروه‌های کالا — درختی.</summary>
public interface IInvCategoryService
{
    Task<List<InvCategory>> GetTreeAsync(bool activeOnly = false);
    Task<List<InvCategory>> GetFlatAsync(bool activeOnly = false);
    Task<InvCategory> SaveAsync(InvCategory category);
    Task MoveAsync(InvCategoryMove cmd);
    Task DeleteAsync(int id);
    Task<int> ApplyCategoryVatAsync(int categoryId, bool isVatIncluded, decimal vatRate);
}

/// <summary>ویژگی‌های کالا.</summary>
public interface IInvAttributeService
{
    Task<List<InvAttribute>> GetAllAsync(bool activeOnly = false, int? categoryId = null);
    Task<InvAttribute> SaveAsync(InvAttribute attribute);
    Task DeleteAsync(int id);
}

/// <summary>کالاها — فرم چندمرحله‌ای تعریف کالا.</summary>
public interface IInvProductService
{
    Task<PagedResult<InvProduct>> GetAllAsync(string? search = null, int? categoryId = null, int? warehouseId = null,
        bool below = false, bool activeOnly = false, int page = 1, int pageSize = 15);
    Task<InvProduct> GetAsync(int id);
    Task<InvProduct> NewAsync(int? categoryId = null);
    Task<InvProduct> SaveAsync(InvProduct product);
    Task DeleteAsync(int id);
    Task<List<LookupItem>> GetLookupsAsync(string? search = null, int? warehouseId = null);
}

/// <summary>انبارها.</summary>
public interface IInvWarehouseService
{
    Task<List<InvWarehouse>> GetAllAsync(bool activeOnly = false);
    Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = true);
    Task<InvWarehouse> SaveAsync(InvWarehouse warehouse);
    Task DeleteAsync(int id);
}

/// <summary>انواع رسید و حواله.</summary>
public interface IInvDocTypeService
{
    Task<List<InvDocType>> GetAllAsync(bool activeOnly = false, StockNature? nature = null);
    Task<InvDocType> SaveAsync(InvDocType docType);
    Task DeleteAsync(int id);
}

/// <summary>اسناد انبار (رسید، حواله، انتقال).</summary>
public interface IInvDocService
{
    Task<PagedResult<InvDoc>> GetAllAsync(StockNature? nature = null, int? docTypeId = null, int? warehouseId = null,
        InvDocStatus? status = null, string? search = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 15);
    Task<InvDoc> GetAsync(int id);
    Task<InvDoc> SaveAsync(InvDoc doc);
    Task<InvDoc> ConfirmAsync(int id);
    Task<InvDoc> UnconfirmAsync(int id);
    Task<InvDoc> CancelAsync(int id);
    Task DeleteAsync(int id);
}

/// <summary>گزارش‌های انبارداری: کاردکس و موجودی.</summary>
public interface IInvReportService
{
    Task<InvKardexResult> GetKardexAsync(int productId, int? warehouseId = null, DateTime? from = null, DateTime? to = null);
    Task<PagedResult<InvStockRow>> GetStockAsync(int? warehouseId = null, int? categoryId = null,
        string? search = null, bool below = false, int page = 1, int pageSize = 15);
}

/// <summary>تنظیمات جامع ماژول انبارداری و ساختار کدینگ کالا.</summary>
public interface IInvSettingsService
{
    Task<WarehousingSettingsDto> GetSettingsAsync();
    Task<WarehousingSettingsDto> SaveSettingsAsync(WarehousingSettingsDto dto);
}
