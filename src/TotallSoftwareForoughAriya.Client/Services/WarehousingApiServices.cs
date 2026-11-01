using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های ماژول انبارداری — تنها نقطه‌ی ساخت مسیرهای API
// =====================================================================

/// <summary>پیاده‌سازی سرویس گروه‌های کالا (درختی).</summary>
public class InvCategoryService : IInvCategoryService
{
    private readonly IApiClient _api;
    public InvCategoryService(IApiClient api) => _api = api;

    public Task<List<InvCategory>> GetTreeAsync(bool activeOnly = false)
        => _api.GetAsync<List<InvCategory>>($"api/inv/categories/tree?activeOnly={activeOnly}");

    public Task<List<InvCategory>> GetFlatAsync(bool activeOnly = false)
        => _api.GetAsync<List<InvCategory>>($"api/inv/categories?activeOnly={activeOnly}");

    public Task<InvCategory> SaveAsync(InvCategory category)
        => _api.PostAsync<InvCategory>("api/inv/categories", category);

    public Task MoveAsync(InvCategoryMove cmd)
        => _api.PostAsync<object>("api/inv/categories/move", cmd);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/inv/categories/{id}");

    public async Task<int> ApplyCategoryVatAsync(int categoryId, bool isVatIncluded, decimal vatRate)
    {
        var res = await _api.PostAsync<ApplyCategoryVatResponse>("api/inv/categories/apply-vat", new ApplyCategoryVatRequest
        {
            CategoryId = categoryId,
            IsVatIncluded = isVatIncluded,
            VatRate = vatRate
        });
        return res?.UpdatedCount ?? 0;
    }
}

/// <summary>پیاده‌سازی سرویس ویژگی‌های کالا.</summary>
public class InvAttributeService : IInvAttributeService
{
    private readonly IApiClient _api;
    public InvAttributeService(IApiClient api) => _api = api;

    public Task<List<InvAttribute>> GetAllAsync(bool activeOnly = false, int? categoryId = null)
    {
        var q = $"api/inv/attributes?activeOnly={activeOnly}";
        if (categoryId is > 0) q += $"&categoryId={categoryId}";
        return _api.GetAsync<List<InvAttribute>>(q);
    }

    public Task<InvAttribute> SaveAsync(InvAttribute attribute)
        => _api.PostAsync<InvAttribute>("api/inv/attributes", attribute);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/inv/attributes/{id}");
}

/// <summary>پیاده‌سازی سرویس کالاها.</summary>
public class InvProductService : IInvProductService
{
    private readonly IApiClient _api;
    public InvProductService(IApiClient api) => _api = api;

    public Task<PagedResult<InvProduct>> GetAllAsync(string? search = null, int? categoryId = null, int? warehouseId = null,
        bool below = false, bool activeOnly = false, int page = 1, int pageSize = 15)
    {
        var q = $"api/inv/products?search={Uri.EscapeDataString(search ?? "")}&below={below}&activeOnly={activeOnly}&page={page}&pageSize={pageSize}";
        if (categoryId is > 0) q += $"&categoryId={categoryId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        return _api.GetAsync<PagedResult<InvProduct>>(q);
    }

    public Task<InvProduct> GetAsync(int id)
        => _api.GetAsync<InvProduct>($"api/inv/products/{id}");

    public Task<InvProduct> NewAsync(int? categoryId = null)
        => _api.GetAsync<InvProduct>(categoryId is > 0 ? $"api/inv/products/new?categoryId={categoryId}" : "api/inv/products/new");

    public Task<InvProduct> SaveAsync(InvProduct product)
        => _api.PostAsync<InvProduct>("api/inv/products", product);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/inv/products/{id}");

    public Task<List<LookupItem>> GetLookupsAsync(string? search = null, int? warehouseId = null)
    {
        var q = $"api/inv/products/lookups?search={Uri.EscapeDataString(search ?? "")}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        return _api.GetAsync<List<LookupItem>>(q);
    }
}

/// <summary>پیاده‌سازی سرویس انبارها.</summary>
public class InvWarehouseService : IInvWarehouseService
{
    private readonly IApiClient _api;
    public InvWarehouseService(IApiClient api) => _api = api;

    public Task<List<InvWarehouse>> GetAllAsync(bool activeOnly = false)
        => _api.GetAsync<List<InvWarehouse>>($"api/inv/warehouses?activeOnly={activeOnly}");

    public Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = true)
        => _api.GetAsync<List<LookupItem>>($"api/inv/warehouses/lookups?activeOnly={activeOnly}");

    public Task<InvWarehouse> SaveAsync(InvWarehouse warehouse)
        => _api.PostAsync<InvWarehouse>("api/inv/warehouses", warehouse);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/inv/warehouses/{id}");
}

/// <summary>پیاده‌سازی سرویس انواع رسید و حواله.</summary>
public class InvDocTypeService : IInvDocTypeService
{
    private readonly IApiClient _api;
    public InvDocTypeService(IApiClient api) => _api = api;

    public Task<List<InvDocType>> GetAllAsync(bool activeOnly = false, StockNature? nature = null)
    {
        var q = $"api/inv/doc-types?activeOnly={activeOnly}";
        if (nature.HasValue) q += $"&nature={(int)nature.Value}";
        return _api.GetAsync<List<InvDocType>>(q);
    }

    public Task<InvDocType> SaveAsync(InvDocType docType)
        => _api.PostAsync<InvDocType>("api/inv/doc-types", docType);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/inv/doc-types/{id}");
}

/// <summary>پیاده‌سازی سرویس اسناد انبار.</summary>
public class InvDocService : IInvDocService
{
    private readonly IApiClient _api;
    public InvDocService(IApiClient api) => _api = api;

    public Task<PagedResult<InvDoc>> GetAllAsync(StockNature? nature = null, int? docTypeId = null, int? warehouseId = null,
        InvDocStatus? status = null, string? search = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 15)
    {
        var q = $"api/inv/docs?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (nature.HasValue) q += $"&nature={(int)nature.Value}";
        if (docTypeId is > 0) q += $"&docTypeId={docTypeId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (status.HasValue) q += $"&status={(int)status.Value}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<InvDoc>>(q);
    }

    public Task<InvDoc> GetAsync(int id)
        => _api.GetAsync<InvDoc>($"api/inv/docs/{id}");

    public Task<InvDoc> SaveAsync(InvDoc doc)
        => _api.PostAsync<InvDoc>("api/inv/docs", doc);

    public Task<InvDoc> ConfirmAsync(int id)
        => _api.PostAsync<InvDoc>($"api/inv/docs/{id}/confirm");

    public Task<InvDoc> UnconfirmAsync(int id)
        => _api.PostAsync<InvDoc>($"api/inv/docs/{id}/unconfirm");

    public Task<InvDoc> CancelAsync(int id)
        => _api.PostAsync<InvDoc>($"api/inv/docs/{id}/cancel");

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/inv/docs/{id}");
}

/// <summary>پیاده‌سازی سرویس گزارش‌های انبارداری.</summary>
public class InvReportService : IInvReportService
{
    private readonly IApiClient _api;
    public InvReportService(IApiClient api) => _api = api;

    public Task<InvKardexResult> GetKardexAsync(int productId, int? warehouseId = null, DateTime? from = null, DateTime? to = null)
    {
        var q = $"api/inv/reports/kardex?productId={productId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<InvKardexResult>(q);
    }

    public Task<PagedResult<InvStockRow>> GetStockAsync(int? warehouseId = null, int? categoryId = null,
        string? search = null, bool below = false, int page = 1, int pageSize = 15)
    {
        var q = $"api/inv/reports/stock?search={Uri.EscapeDataString(search ?? "")}&below={below}&page={page}&pageSize={pageSize}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (categoryId is > 0) q += $"&categoryId={categoryId}";
        return _api.GetAsync<PagedResult<InvStockRow>>(q);
    }
}

/// <summary>پیاده‌سازی سرویس تنظیمات انبارداری و ساختار کدینگ.</summary>
public class InvSettingsService : IInvSettingsService
{
    private readonly IApiClient _api;
    public InvSettingsService(IApiClient api) => _api = api;

    public Task<WarehousingSettingsDto> GetSettingsAsync()
        => _api.GetAsync<WarehousingSettingsDto>("api/inv/settings");

    public Task<WarehousingSettingsDto> SaveSettingsAsync(WarehousingSettingsDto dto)
        => _api.PostAsync<WarehousingSettingsDto>("api/inv/settings", dto);
}
