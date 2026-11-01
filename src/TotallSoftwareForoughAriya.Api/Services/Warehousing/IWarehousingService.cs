using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services;

/// <summary>
/// قرارداد سرویس ماژول انبارداری:
/// گروه کالای درختی، ویژگی‌های کالا، تعریف کالا، انبارها،
/// انواع رسید/حواله (با ماهیت)، اسناد انبار و گزارش کاردکس.
/// </summary>
public interface IWarehousingService
{
    // ---------------- گروه کالا (درختی) ----------------
    /// <summary>گروه‌های کالا به‌صورت درخت (هر گره شامل فرزندانش).</summary>
    Task<List<InvCategory>> GetCategoryTreeAsync(bool activeOnly = false, Paging.Request? pagination = null);

    /// <summary>گروه‌های کالا به‌صورت فهرست تخت مرتب‌شده (برای کمبوها).</summary>
    Task<List<InvCategory>> GetCategoriesFlatAsync(bool activeOnly = false, Paging.Request? pagination = null);

    Task<InvCategory> SaveCategoryAsync(InvCategory dto);
    Task DeleteCategoryAsync(int id);

    /// <summary>اعمال ارزش افزوده به یک گروه کالا و کلیه کالاهای زیرمجموعه آن.</summary>
    Task<int> ApplyCategoryVatAsync(int categoryId, bool isVatIncluded, decimal vatRate);

    /// <summary>جابه‌جایی گروه در درخت (تغییر والد یا ترتیب).</summary>
    Task MoveCategoryAsync(InvCategoryMove cmd);

    // ---------------- ویژگی‌های کالا ----------------
    Task<List<InvAttribute>> GetAttributesAsync(bool activeOnly = false, int? categoryId = null, Paging.Request? pagination = null);
    Task<InvAttribute> SaveAttributeAsync(InvAttribute dto);
    Task DeleteAttributeAsync(int id);

    // ---------------- کالا ----------------
    Task<PagedResult<InvProduct>> GetProductsAsync(string? search, int? categoryId, int? warehouseId,
        bool belowOnly, bool activeOnly, int page, int pageSize);
    Task<InvProduct?> GetProductAsync(int id);

    /// <summary>قالب خالی کالای جدید همراه با ویژگی‌های گروه انتخابی و کد پیشنهادی.</summary>
    Task<InvProduct> NewProductAsync(int? categoryId);
    Task<InvProduct> SaveProductAsync(InvProduct dto);
    Task DeleteProductAsync(int id);
    Task<List<LookupItem>> GetProductLookupsAsync(string? search = null, int? warehouseId = null, Paging.Request? pagination = null);

    // ---------------- انبار ----------------
    Task<List<InvWarehouse>> GetWarehousesAsync(bool activeOnly = false, Paging.Request? pagination = null);
    Task<InvWarehouse> SaveWarehouseAsync(InvWarehouse dto);
    Task DeleteWarehouseAsync(int id);

    // ---------------- نوع رسید و حواله ----------------
    Task<List<InvDocType>> GetDocTypesAsync(bool activeOnly = false, StockNature? nature = null, Paging.Request? pagination = null);
    Task<InvDocType> SaveDocTypeAsync(InvDocType dto);
    Task DeleteDocTypeAsync(int id);

    // ---------------- اسناد انبار ----------------
    Task<PagedResult<InvDoc>> GetDocsAsync(StockNature? nature, int? docTypeId, int? warehouseId,
        InvDocStatus? status, string? search, DateTime? from, DateTime? to, int page, int pageSize);
    Task<InvDoc?> GetDocAsync(int id);
    Task<InvDoc> SaveDocAsync(InvDoc dto, string? user);
    Task DeleteDocAsync(int id);

    /// <summary>قطعی‌سازی سند — اثرگذاری روی موجودی و کاردکس.</summary>
    Task<InvDoc> ConfirmDocAsync(int id, string? user);

    /// <summary>برگشت سند قطعی به پیش‌نویس.</summary>
    Task<InvDoc> UnconfirmDocAsync(int id, string? user);

    /// <summary>ابطال سند.</summary>
    Task<InvDoc> CancelDocAsync(int id, string? user);

    // ---------------- کاردکس و موجودی ----------------
    Task<InvKardexResult> GetKardexAsync(int productId, int? warehouseId, DateTime? from, DateTime? to,
        Paging.Request? pagination = null);
    Task<PagedResult<InvStockRow>> GetStockAsync(int? warehouseId, int? categoryId, string? search,
        bool belowOnly, int page, int pageSize);

    // ---------------- تنظیمات انبارداری و کدینگ ----------------
    Task<WarehousingSettingsDto> GetWarehousingSettingsAsync();
    Task<WarehousingSettingsDto> SaveWarehousingSettingsAsync(WarehousingSettingsDto dto);
}
